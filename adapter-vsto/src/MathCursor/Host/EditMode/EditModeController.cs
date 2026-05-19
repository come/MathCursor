using System;
using MathCursor.Core.Resolution;
using MathCursor.Host.CCMeta;
using MathCursor.HostContract;
using MathCursor.UI;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.EditMode
{
    /// <summary>
    /// Bounded context "édition d'une équation existante" (DDD).
    ///
    /// <para>Pilote le cycle complet quand le caret atterrit sur l'une de
    /// NOS OMaths (identifiée par CC MathCursor + cc.Tag MCMeta) :</para>
    /// <list type="number">
    /// <item>Détection au polling : <see cref="Sync"/> est appelé à chaque
    /// tick. Ouvre la popup edit si le caret est sur une OMath à nous,
    /// la ferme quand le caret en sort.</item>
    /// <item>Garde "déjà géré" : <see cref="_editingOMathStart"/> empêche
    /// le respawn de la popup tant que le caret reste sur la même OMath.</item>
    /// <item>Action revert : <see cref="OnRevertRequested"/> remplace
    /// l'OMath par la source brute (lue depuis cc.Tag).</item>
    /// </list>
    ///
    /// <para>Phase B (2026-05-18) : identification + source via CC.Tag
    /// au lieu de bookmark + IEquationStore.</para>
    /// </summary>
    internal sealed class EditModeController
    {
        private readonly Word.Application _app;
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
            EquationHandleRegistry handleRegistry,
            Action hideSuggestionPopup,
            Func<(double x, double y)> getCaretScreenPos,
            Action<string> log)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
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
            var (_, meta) = CcMetaResolver.ResolveAt(om);
            if (meta == null || string.IsNullOrEmpty(meta.HandleId)) return false;

            _hideSuggestionPopup();
            _editHandle = new EquationHandle(meta.HandleId);

            if (_popup == null)
            {
                _popup = new EditModePopupWindow();
                _popup.RevertRequested += OnRevertRequested;
            }

            const double OMathExtraHeightDip = 18.0;
            var caretPos = _getCaretScreenPos();
            _popup.ShowAt(caretPos.x, caretPos.y + OMathExtraHeightDip, alignRight: true);
            _log($"edit mode: handle={meta.HandleId} popup at caret-rightaligned ({caretPos.x:F0},{caretPos.y + OMathExtraHeightDip:F0})");
            return true;
        }

        // ── Action revert ────────────────────────────────────────────

        private void OnRevertRequested()
        {
            var handle = _editHandle;
            if (handle == null) { _log("revert: no _editHandle, abort"); return; }

            // 1. OMath sous le caret.
            var om = FindOMathAtCaret();
            if (om == null) { _log("revert: no OMath at caret, abort"); return; }

            // 2. Lit le CC + parse Tag → sténo initiale.
            var (cc, meta) = CcMetaResolver.ResolveAt(om);
            if (meta == null || string.IsNullOrEmpty(meta.Steno))
            {
                _log($"revert: source introuvable pour handle {handle.Id} (CC manquant ou tag corrompu)");
                return;
            }
            string source = meta.Steno;
            string revertText = source.Replace("\n", "\r");

            try
            {
                var sel = _app.Selection;

                // 3. Sélectionne TOUT l'OMath, en bornant À DROITE par
                //    om.Range.End — pas cc.Range.End ! Le CC peut être
                //    sur-étendu par auto-grow Word (test 2026-05-18 le
                //    montre : cc.Range peut englober ¶+ et OMath voisine).
                //    selStart = cc.Range.Start pour capturer le wrapper
                //    d'ouverture. selEnd clamp à l'OMath = sûr.
                int selStart = cc?.Range.Start ?? om.Range.Start;
                int selEnd = om.Range.End;
                sel.SetRange(selStart, selEnd);
                _log($"revert: select [{selStart},{selEnd}) (om.End clamped) post-snap sel=[{sel.Start},{sel.End})");

                // Unlock CC avant Delete/TypeText : la CC est LockContents=true
                // depuis le commit (anti auto-grow). Le revert doit pouvoir
                // muter son contenu.
                if (cc != null)
                {
                    try { cc.LockContents = false; } catch { }
                    try { cc.LockContentControl = false; } catch { }
                }

                // 4. Remplace : Delete + TypeText avec la sténo brute.
                sel.Delete();
                sel.TypeText(revertText);
                int newEnd = sel.Start;

                // 5. Dispose le CC wrapper (= devenu un wrapper d'OMath
                //    « ghost » que Word préserve cosmétiquement, et
                //    possiblement du contenu absorbé après l'OMath).
                //    cc.Delete(false) = wrapper-only, contenu préservé.
                if (cc != null)
                {
                    try { cc.Delete(false); } catch (Exception exCc) { _log("revert_cc_dispose_error: " + exCc.Message); }
                }

                if (source.IndexOf('\n') >= 0)
                {
                    _log($"revert: multi-ligne zone tracked [{selStart},{newEnd}] handle={handle.Id}");
                    MultiLineReverted?.Invoke(selStart, newEnd, handle.Id);
                }
                else
                {
                    InlineReverted?.Invoke();
                }

                try { _app.Activate(); } catch { }
            }
            catch (Exception ex) { _log("revert_error: " + ex.Message); return; }

            _handleRegistry.Forget(handle.Id);
            _editHandle = null;
            _editingOMathStart = -1;
            _popup?.HidePopup();
            _log($"revert: handle={handle.Id} → \"{source}\"");
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
