// MathCursor: capturing mathematical intent from linear keyboard input.
// Copyright (C) 2026  Côme de Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using MathCursor.UI;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Palette de flèches des tableaux de variation : curseur (sélection
    /// réduite) dans une case de style « MathCursor Flèche » → palette en ligne
    /// ↗ ↘ → sous le caret ; la flèche choisie remplace le contenu de la case.
    /// Piloté par l'event natif <c>WindowSelectionChange</c> + les handlers du
    /// hook clavier (cf. <c>ThisAddIn</c>). Après validation ou Échap, la
    /// palette ne se rouvre pas dans la même case tant que le curseur n'en est
    /// pas sorti. Cf. ADR 2026-10-19-UX-variation-arrow-palette.
    /// </summary>
    internal sealed class ArrowCellController
    {
        private readonly Word.Application _app;
        private readonly Action<string> _log;
        private ArrowPalettePopupWindow _popup;

        // Case courante (début de sa plage) et case où la palette a été
        // fermée volontairement. -1 = aucune.
        private int _currentCellStart = -1;
        private int _dismissedCellStart = -1;
        private bool _committing;

        public ArrowCellController(Word.Application app, Action<string> log)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _log = log;
        }

        public bool IsPopupVisible => _popup?.IsVisible == true;
        public bool IsNavMode => IsPopupVisible && _popup.IsNavMode;

        public void OnSelectionChanged(Word.Selection sel)
        {
            if (_committing) return;
            int cellStart = TryGetArrowCellStart(sel, out string current);
            if (cellStart < 0)
            {
                _currentCellStart = _dismissedCellStart = -1;
                HidePopup();
                return;
            }

            if (cellStart != _currentCellStart)
            {
                _currentCellStart = cellStart;
                _dismissedCellStart = -1;
            }
            if (cellStart == _dismissedCellStart || IsPopupVisible) return;

            if (!TryGetAnchor(sel, out double x, out double bottom)) return;
            EnsurePopup().ShowAt(x, bottom + 4, current);
        }

        /// <summary>Ancre DIP sous le caret ; à défaut (Tab de cellule =
        /// contenu sélectionné, pas de caret GDI), sous la plage sélectionnée
        /// via <c>Window.GetPoint</c> (pixels écran → DIP).</summary>
        private bool TryGetAnchor(Word.Selection sel, out double x, out double bottom)
        {
            if (sel.Start == sel.End
                && Caret.CaretScreenPositionReader.TryReadRect(out x, out _, out bottom))
                return true;
            x = bottom = 0;
            try
            {
                _app.ActiveWindow.GetPoint(out int left, out int top, out int width, out int height, sel.Range);
                double scale = Caret.CaretScreenPositionReader.GetDpiScale();
                x = left / scale;
                bottom = (top + height) / scale;
                return height > 0;
            }
            catch { return false; }
        }

        public void EnterNavMode() => _popup?.EnterNavMode();
        public void ExitNavMode() => _popup?.ExitNavMode();
        public void MoveSelection(int delta) => _popup?.MoveSelection(delta);

        /// <summary>Échap / frappe dans la case : ferme sans rouvrir dans
        /// cette case.</summary>
        public void Dismiss()
        {
            _dismissedCellStart = _currentCellStart;
            HidePopup();
        }

        public void HidePopup() => _popup?.HidePopup();

        /// <summary>Écrit la flèche sélectionnée dans la case courante.</summary>
        public bool CommitSelected()
        {
            string arrow = _popup?.SelectedChoice;
            if (arrow == null) return false;
            Dismiss();
            _committing = true;
            try
            {
                var sel = _app.Selection;
                if (sel == null || sel.Cells.Count == 0) return false;
                using (new UndoRecordScope(_app, "MathCursor : flèche"))
                {
                    var content = sel.Cells[1].Range;
                    content.MoveEnd(Word.WdUnits.wdCharacter, -1); // hors marque de fin de cellule
                    content.Text = arrow;
                    content.Collapse(Word.WdCollapseDirection.wdCollapseEnd);
                    content.Select();
                }
                _log?.Invoke("arrow_cell_commit " + arrow);
                return true;
            }
            catch (Exception ex)
            {
                _log?.Invoke("arrow_cell_commit_error: " + ex.Message);
                return false;
            }
            finally { _committing = false; }
        }

        /// <summary>
        /// Début de la case si la sélection (réduite, ou contenu de la case
        /// sélectionné par Tab) est dans une case de flèche, -1 sinon.
        /// <paramref name="current"/> = texte de la case (flèche déjà posée,
        /// pour la présélection).
        /// </summary>
        private static int TryGetArrowCellStart(Word.Selection sel, out string current)
        {
            current = null;
            try
            {
                if (sel == null) return -1;
                if (!(bool)sel.get_Information(Word.WdInformation.wdWithInTable)) return -1;
                if (sel.Cells.Count != 1) return -1;
                var cellRange = sel.Cells[1].Range;
                if (sel.Start != sel.End && sel.End > cellRange.End) return -1;
                var style = sel.Paragraphs[1].get_Style() as Word.Style;
                if (style == null || style.NameLocal != StudyTableTemplate.ArrowStyleName) return -1;
                current = (cellRange.Text ?? "").TrimEnd('\r', '\a');
                return cellRange.Start;
            }
            catch { return -1; }
        }

        private ArrowPalettePopupWindow EnsurePopup()
        {
            if (_popup != null) return _popup;
            _popup = new ArrowPalettePopupWindow(StudyTableTemplate.ArrowChoices);
            _popup.CommitRequested += () => CommitSelected();
            return _popup;
        }

        public void Close()
        {
            try { _popup?.Close(); } catch { }
            _popup = null;
        }
    }
}
