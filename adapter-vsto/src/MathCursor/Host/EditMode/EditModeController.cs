using System;
using MathCursor.Core.Resolution;
using MathCursor.Host.Bookmarks;
using MathCursor.HostContract;
using MathCursor.UI;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.EditMode
{
    /// <summary>
    /// Bounded context "édition d'une équation existante" (DDD).
    ///
    /// <para>Pilote le cycle complet quand le caret atterrit sur l'une de
    /// NOS OMaths (identifiée par bookmark <c>mcEq_*</c>) :</para>
    /// <list type="number">
    /// <item>Détection au polling : <see cref="Sync"/> est appelé à chaque
    /// tick. Ouvre la popup edit si le caret est sur une OMath à nous,
    /// la ferme quand le caret en sort.</item>
    /// <item>Garde "déjà géré" : <see cref="_editingOMathStart"/> empêche
    /// le respawn de la popup tant que le caret reste sur la même OMath.</item>
    /// <item>Action revert : <see cref="OnRevertRequested"/> remplace
    /// l'OMath par la source brute (avec ré-injection sidecar), supprime
    /// le store entry et délète le bookmark.</item>
    /// </list>
    ///
    /// <para>P2.10 du refactor archi (continuité ADR
    /// <c>2026-05-12-Refactor-pure-merger-atomic-insert</c>).</para>
    /// </summary>
    internal sealed class EditModeController
    {
        private readonly Word.Application _app;
        private readonly IEquationStore _store;
        private readonly EquationBookmarkRegistry _bookmarks;
        private readonly EquationHandleRegistry _handleRegistry;
        private readonly Action _hideSuggestionPopup;
        private readonly Func<(double x, double y)> _getCaretScreenPos;
        private readonly Action<string> _log;

        private EquationHandle _editHandle;
        private int _editingOMathStart = -1;
        private EditModePopupWindow _popup;

        /// <summary>
        /// Émis quand un revert d'une OMath multi-ligne vient de se faire.
        /// La zone résultante (texte revert avec \r) est mémorisée par
        /// le caller pour que le prochain commit puisse re-absorber via
        /// <c>RevertedMultiLineMerger</c>. Arguments : (start, end, handleId).
        /// </summary>
        public event Action<int, int, string> MultiLineReverted;

        /// <summary>Émis après un revert inline (= sans \n). Le caller
        /// doit clear son tracking multi-ligne.</summary>
        public event Action InlineReverted;

        public bool IsPopupVisible => _popup?.IsVisible == true;
        public EquationHandle CurrentEditingHandle => _editHandle;

        public EditModeController(
            Word.Application app,
            IEquationStore store,
            EquationBookmarkRegistry bookmarks,
            EquationHandleRegistry handleRegistry,
            Action hideSuggestionPopup,
            Func<(double x, double y)> getCaretScreenPos,
            Action<string> log)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
            _handleRegistry = handleRegistry ?? throw new ArgumentNullException(nameof(handleRegistry));
            _hideSuggestionPopup = hideSuggestionPopup ?? (() => { });
            _getCaretScreenPos = getCaretScreenPos ?? throw new ArgumentNullException(nameof(getCaretScreenPos));
            _log = log ?? (s => { });
        }

        /// <summary>
        /// Polling : sync l'état de la popup edit avec la position du caret.
        /// Retourne <c>true</c> si la popup edit est désormais (ou reste)
        /// active — le caller (CheckAndUpdate) doit alors ne PAS lancer
        /// la popup de suggestion.
        /// </summary>
        public bool Sync(Word.OMath omAtCaret, bool inPostCommitCooldown)
        {
            if (omAtCaret == null)
            {
                if (_editHandle != null || _editingOMathStart != -1)
                {
                    _editHandle = null;
                    _editingOMathStart = -1;
                    _popup?.HidePopup();
                }
                return false;
            }

            if (inPostCommitCooldown)
            {
                _popup?.HidePopup();
                return true; // caret sur OMath fraîche mais cooldown : on bloque la suggestion popup
            }

            int omStart = -1;
            try { omStart = omAtCaret.Range.Start; } catch { }
            if (_editingOMathStart == omStart) return true; // déjà géré, no-op

            bool entered = TryEnter(omAtCaret);
            _editingOMathStart = omStart;
            return entered;
        }

        public void HidePopup() => _popup?.HidePopup();

        public void Close()
        {
            try { _popup?.Close(); } catch { }
            _popup = null;
            _editHandle = null;
            _editingOMathStart = -1;
        }

        // ── Entrée mode édition ──────────────────────────────────────

        private bool TryEnter(Word.OMath om)
        {
            var handleId = _bookmarks.FindHandleForOMath(om);
            if (handleId == null) return false;

            _hideSuggestionPopup();
            _editHandle = new EquationHandle(handleId);

            if (_popup == null)
            {
                _popup = new EditModePopupWindow();
                _popup.RevertRequested += OnRevertRequested;
            }

            const double OMathExtraHeightDip = 18.0;
            var caretPos = _getCaretScreenPos();
            _popup.ShowAt(caretPos.x, caretPos.y + OMathExtraHeightDip, alignRight: true);
            _log($"edit mode: handle={handleId} popup at caret-rightaligned ({caretPos.x:F0},{caretPos.y + OMathExtraHeightDip:F0})");
            return true;
        }

        // ── Action revert ────────────────────────────────────────────

        private void OnRevertRequested()
        {
            var handle = _editHandle;
            if (handle == null) { _log("revert: no _editHandle, abort"); return; }

            var om = FindOMathAtCaret();
            if (om == null) { _log("revert: no OMath at caret, abort"); return; }

            StoredEquation stored;
            try { stored = _store.RetrieveAsync(handle).GetAwaiter().GetResult(); }
            catch (Exception ex) { _log("revert_retrieve_error: " + ex.Message); return; }
            if (stored == null || string.IsNullOrEmpty(stored.Source))
            {
                _log($"revert: source introuvable pour handle {handle.Id}");
                return;
            }

            // Ré-injection sidecar si persisté côté store (sinon mémoire vierge).
            if (!string.IsNullOrEmpty(stored.Metadata?.SidecarJson))
            {
                var sc = SidecarSerializer.Deserialize(stored.Metadata!.SidecarJson);
                _handleRegistry.Restore(handle.Id, sc);
            }

            string source = stored.Source;
            int omStart, omEnd;
            try { omStart = om.Range.Start; omEnd = om.Range.End; }
            catch (Exception ex) { _log("revert_range_error: " + ex.Message); return; }

            try
            {
                var doc = _app.ActiveDocument;

                // Étendre au bookmark mcEq_ si présent.
                string bmName = EquationBookmarkRegistry.Prefix + handle.Id;
                if (doc.Bookmarks.Exists(bmName))
                {
                    var bm = doc.Bookmarks[bmName];
                    var bmRange = bm.Range;
                    omStart = Math.Min(omStart, bmRange.Start);
                    omEnd = Math.Max(omEnd, bmRange.End);
                    try { bm.Delete(); } catch { }
                }

                // Étendre au ContentControl si Word en a posé un (display mode).
                try
                {
                    foreach (Word.ContentControl cc in doc.ContentControls)
                    {
                        var ccRange = cc.Range;
                        if (ccRange.Start <= omStart && ccRange.End >= omEnd)
                        {
                            omStart = Math.Min(omStart, ccRange.Start);
                            omEnd = Math.Max(omEnd, ccRange.End);
                            try { cc.Delete(true); } catch { }
                            break;
                        }
                    }
                }
                catch (Exception ex) { _log("revert_cc_scan_error: " + ex.Message); }

                // Delete explicite de l'OMath (Word peut garder son enveloppe sinon).
                try { om.Range.Delete(); } catch { }

                // Insert via range collapsé à omStart : pure insertion, pas remplacement.
                // Convertit \n source en \r Word pour recréer la structure multi-¶.
                string revertText = source.Replace("\n", "\r");
                doc.Range(omStart, omStart).Text = revertText;

                int newEnd = omStart + revertText.Length;
                try { _app.Selection.SetRange(newEnd, newEnd); } catch { }

                // Notification au caller du tracking multi-ligne.
                if (source.IndexOf('\n') >= 0)
                {
                    _log($"revert: multi-ligne zone tracked [{omStart},{newEnd}] handle={handle.Id}");
                    MultiLineReverted?.Invoke(omStart, newEnd, handle.Id);
                }
                else
                {
                    InlineReverted?.Invoke();
                }

                // Click popup WPF a volé le focus à Word — re-focus.
                try { _app.Activate(); } catch { }
            }
            catch (Exception ex) { _log("revert_replace_error: " + ex.Message); return; }

            try { _store.RemoveAsync(handle).GetAwaiter().GetResult(); }
            catch (Exception ex) { _log("revert_store_remove_error: " + ex.Message); }

            _editHandle = null;
            _editingOMathStart = -1;
            _popup?.HidePopup();
            _log($"revert: handle={handle.Id} OMath remplacé par source=\"{source}\"");
        }

        /// <summary>Cherche un OMath inclus dans la sélection actuelle.</summary>
        private Word.OMath FindOMathAtCaret()
        {
            try
            {
                var sel = _app.Selection;
                if (sel?.OMaths != null && sel.OMaths.Count > 0)
                {
                    foreach (Word.OMath om in sel.OMaths) return om;
                }
            }
            catch { }
            return null;
        }
    }
}
