using System;
using MathCursor.Host.SourceMap;
using MathCursor.HostContract;
using MathCursor.UI;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.EditMode
{
    /// <summary>
    /// Bounded context "édition d'une équation existante" (DDD).
    ///
    /// <para>Pilote le cycle complet quand le caret atterrit sur l'une de
    /// NOS OMaths — identifiée par la MAP hash→source (ADR hash-source-map,
    /// plus d'anchor CC) :</para>
    /// <list type="number">
    /// <item>Détection au polling : <see cref="Sync"/> est appelé à chaque
    /// tick. Ouvre la popup edit si le caret est sur une OMath à nous,
    /// la ferme quand le caret en sort. Coût nominal : K1 (Range.Text).</item>
    /// <item>Garde "déjà géré" : <see cref="_editingOMathStart"/> empêche
    /// le respawn de la popup tant que le caret reste sur la même OMath.</item>
    /// <item>Action revert : <see cref="OnRevertRequested"/> remplace
    /// l'OMath par la source brute — CONFIRMÉE par K2 (opération
    /// destructive, jamais sur un simple match K1).</item>
    /// </list>
    /// </summary>
    internal sealed class EditModeController
    {
        private readonly Word.Application _app;
        private readonly SourceMapResolver _resolver;
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
            SourceMapResolver resolver,
            Action hideSuggestionPopup,
            Func<(double x, double y)> getCaretScreenPos,
            Action<string> log)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
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
            var source = _resolver.ResolveAt(_app.ActiveDocument, om);
            if (source == null) return false;

            _hideSuggestionPopup();
            _editHandle = new EquationHandle(source.HandleId ?? OMathInserter.NewHandleId());

            if (_popup == null)
            {
                _popup = new EditModePopupWindow();
                _popup.RevertRequested += OnRevertRequested;
            }

            // Panneau ancré au DÉBUT de la formule (demande user 2026-06-11) :
            // bord gauche du popup = bord gauche de l'OMath, juste dessous.
            // Repli : ancien comportement caret-rightaligned si GetPoint échoue.
            if (TryGetOMathScreenAnchor(om, out double ax, out double ay))
            {
                _popup.ShowAt(ax, ay, alignRight: false);
                _log($"edit mode: handle={_editHandle.Id} popup at omath-start ({ax:F0},{ay:F0})");
            }
            else
            {
                const double OMathExtraHeightDip = 18.0;
                var caretPos = _getCaretScreenPos();
                _popup.ShowAt(caretPos.x, caretPos.y + OMathExtraHeightDip, alignRight: true);
                _log($"edit mode: handle={_editHandle.Id} popup at caret-rightaligned fallback ({caretPos.x:F0},{caretPos.y + OMathExtraHeightDip:F0})");
            }
            return true;
        }

        /// <summary>Coin bas-gauche de la formule en DIP via Window.GetPoint
        /// (coordonnées écran pixels → DIP par le scale DPI partagé).</summary>
        private bool TryGetOMathScreenAnchor(Word.OMath om, out double x, out double y)
        {
            x = y = 0;
            try
            {
                var win = _app.ActiveWindow;
                if (win == null) return false;
                int left, top, width, height;
                win.GetPoint(out left, out top, out width, out height, om.Range);
                if (height <= 0) return false;
                double scale = Caret.CaretScreenPositionReader.GetDpiScale();
                x = left / scale;
                y = (top + height) / scale + 4;
                return true;
            }
            catch { return false; }
        }

        // ── Action revert ────────────────────────────────────────────

        private void OnRevertRequested()
        {
            var handle = _editHandle;
            if (handle == null) { _log("revert: no _editHandle, abort"); return; }

            // 1. OMath sous le caret.
            var om = FindOMathAtCaret();
            if (om == null) { _log("revert: no OMath at caret, abort"); return; }

            // 2. Source CONFIRMÉE par K2 (opération destructive : jamais sur
            //    un simple match K1 — un faux positif détruirait la mauvaise
            //    équation). Lookup miss = équation éditée à la main, acté ADR.
            var sourceEntry = _resolver.ResolveConfirmed(_app.ActiveDocument, om);
            if (sourceEntry == null || string.IsNullOrEmpty(sourceEntry.Steno))
            {
                _log($"revert: source introuvable pour handle {handle.Id} (pas en map ou K2 non confirmée)");
                return;
            }
            string source = sourceEntry.Steno;
            string revertText = source.Replace("\n", "\r");

            try
            {
                var sel = _app.Selection;
                var doc = _app.ActiveDocument;

                // 3. Sélectionne TOUT l'OMath (plus d'anchor : l'OMath EST l'unité).
                int selStart = om.Range.Start;
                int selEnd = om.Range.End;
                sel.SetRange(selStart, selEnd);
                _log($"revert: select [{selStart},{selEnd}) post-snap sel=[{sel.Start},{sel.End})");

                // 4. Remplace : Delete + TypeText avec la sténo brute.
                sel.Delete();
                sel.TypeText(revertText);
                int newEnd = sel.Start;

                int afterPos = selStart;

                if (source.IndexOf('\n') >= 0)
                {
                    _log($"revert: multi-ligne zone tracked [{afterPos},{newEnd}] handle={handle.Id}");
                    MultiLineReverted?.Invoke(afterPos, newEnd, handle.Id);
                }
                else
                {
                    InlineReverted?.Invoke();
                }

                try { _app.Activate(); } catch { }
            }
            catch (Exception ex) { _log("revert_error: " + ex.Message); return; }

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
