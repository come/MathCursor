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
using System.Collections.Generic;
using MathCursor.UI;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Popup des tableaux de signe / variation : quand le curseur entre dans
    /// une case d'un tableau MathCursor, propose les choix de son rôle
    /// (<see cref="StudyTableTemplate.ChoicesFor"/>) et exécute l'action
    /// choisie (<see cref="StudyTableActions"/>). Toute case reste une case où
    /// l'on tape : la popup s'efface à la première touche et cède toujours la
    /// place aux popups de conversion et d'édition. À la sortie d'une
    /// frontière modifiée, la police est ajustée (<see cref="StudyTableFormatter.Reapply"/>).
    /// Piloté par l'event natif <c>WindowSelectionChange</c> + les handlers du
    /// hook clavier (cf. <c>ThisAddIn</c>). Après un choix ou un Échap, la
    /// popup ne se rouvre pas dans la même case tant que le curseur n'en est
    /// pas sorti.
    /// Cf. ADR 2026-10-19-UX-variation-arrow-palette,
    /// 2026-10-19-Feat-study-table-cell-roles-contextual-popup.
    /// </summary>
    internal sealed class StudyTableController
    {
        private sealed class CellContext
        {
            public Word.Table Table;
            public Word.Cell Cell;
            public int Row, Col;
            public StudyCellInfo Info;
            public StudyCellRole Role;
            public StudyRowKind RowKind;
            public string Key;
        }

        private readonly Word.Application _app;
        private readonly Func<bool> _otherPopupVisible;
        private TableChoicePopupWindow _popup;
        private IReadOnlyList<StudyTableAction> _choices = Array.Empty<StudyTableAction>();

        private string _cellKey;
        private string _dismissedKey;
        private string _shownKey;   // case pour laquelle la popup affichée a été construite
        private bool _committing;

        // Frontière en cours d'édition : ajustée à la sortie si son texte a changé.
        private Word.Table _editedTable;
        private string _editedKey;
        private string _editedText;
        private Word.Cell _editedCell;

        public StudyTableController(Word.Application app, Func<bool> otherPopupVisible)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _otherPopupVisible = otherPopupVisible ?? (() => false);
        }

        public bool IsPopupVisible => _popup?.IsVisible == true;
        public bool IsNavMode => IsPopupVisible && _popup.IsNavMode;

        public void OnSelectionChanged(Word.Selection sel)
        {
            if (_committing) return;
            var ctx = TryReadContext(sel);

            // La case quittée a pu changer la structure (nouvelle valeur de x) :
            // la case d'arrivée est alors relue.
            if (_editedCell != null && ctx?.Key != _editedKey && FlushEditedFrontier())
                ctx = TryReadContext(sel);

            if (ctx == null)
            {
                if (_cellKey != null) StudyTableFormatter.Log("sel: sortie du tableau");
                _cellKey = _dismissedKey = null;
                HidePopup();
                return;
            }
            if (ctx.Key != _cellKey)
            {
                StudyTableFormatter.Log($"sel: case r{ctx.Row}c{ctx.Col} rôle={ctx.Role} ligne={ctx.RowKind} " +
                    $"'{StudyTableFormatter.CellText(ctx.Cell)}' sel={sel.Start}-{sel.End}");
                _cellKey = ctx.Key;
                _dismissedKey = null;
            }
            if (ctx.Role == StudyCellRole.Frontier) KeepCaretOnZero(sel, ctx.Cell);

            if (IsTracked(ctx) && _editedKey != ctx.Key)
            {
                _editedTable = ctx.Table;
                _editedCell = ctx.Cell;
                _editedKey = ctx.Key;
                _editedText = StudyTableFormatter.CellText(ctx.Cell);
            }

            // Popup déjà affichée pour CETTE case : rien à faire. Pour une autre
            // case, on la REMPLACE directement (ShowAt reconstruit les choix) :
            // la fermer d'abord laissait la fenêtre « visible » pendant son
            // fondu, et la case suivante n'avait jamais de popup.
            if (_dismissedKey == ctx.Key) { HidePopup(); return; }
            if (IsPopupVisible && _shownKey == ctx.Key) return;
            var choices = StudyTableTemplate.ChoicesFor(ctx.Role, ctx.RowKind,
                canDeleteValue: ctx.Info.FrontierCount > 2,
                canDeleteRow: ctx.Row > 1 && ctx.Info.RowCount > 2);
            if (choices.Count == 0) { HidePopup(); return; }
            if (_otherPopupVisible())
            {
                StudyTableFormatter.Log("popup: autre popup ouverte, on cède");
                HidePopup();
                return;
            }
            if (!TryGetAnchor(sel, out double x, out double bottom))
            {
                StudyTableFormatter.Log("popup: pas d'ancre (caret introuvable)");
                HidePopup();
                return;
            }
            StudyTableFormatter.Log($"popup: [{string.Join(", ", choices)}] à ({x:0},{bottom:0})");

            _choices = choices;
            bool forbidden = ctx.Info.Forbidden;
            var items = new List<TableChoiceItem>(choices.Count);
            int preselect = 0;
            string current = StudyTableFormatter.CellText(ctx.Cell);
            for (int i = 0; i < choices.Count; i++)
            {
                var action = choices[i];
                string arrow = StudyTableTemplate.ArrowText(action);
                if (arrow != null && arrow == current) preselect = i;
                items.Add(new TableChoiceItem(Strings.StudyTableActionLabel(action, forbidden),
                                              isSymbol: IsSymbol(action)));
            }
            EnsurePopup().ShowAt(items, x, bottom + 4, preselect);
            _shownKey = ctx.Key;
        }

        private static bool IsSymbol(StudyTableAction action) =>
            action == StudyTableAction.ArrowUp || action == StudyTableAction.ArrowDown
            || action == StudyTableAction.ArrowRight || action == StudyTableAction.Zero;

        public void EnterNavMode() => _popup?.EnterNavMode();
        public void ExitNavMode() => _popup?.ExitNavMode();
        public void MoveSelection(int delta) => _popup?.MoveSelection(delta);

        /// <summary>Échap, frappe dans la case, autre popup qui s'ouvre :
        /// ferme sans rouvrir dans cette case.</summary>
        public void Dismiss()
        {
            if (IsPopupVisible) StudyTableFormatter.Log("popup: fermée (Échap, frappe ou autre popup)");
            _dismissedKey = _cellKey;
            HidePopup();
        }

        public void HidePopup()
        {
            _shownKey = null;
            _popup?.HidePopup();
        }

        /// <summary>Exécute le choix sélectionné sur la case courante.</summary>
        public bool CommitSelected()
        {
            if (_popup == null) return false;
            int idx = _popup.SelectedIndex;
            if (idx < 0 || idx >= _choices.Count) return false;
            var action = _choices[idx];
            Dismiss();

            var ctx = TryReadContext(_app.Selection);
            if (ctx == null) { StudyTableFormatter.Log($"action {action} : plus de case sous le curseur"); return false; }
            StudyTableFormatter.Log($"action {action} sur r{ctx.Row}c{ctx.Col} ({ctx.Role}, {ctx.RowKind})");
            _committing = true;
            try
            {
                using (new UndoRecordScope(_app, "MathCursor : tableau"))
                    StudyTableActions.Run(ctx.Table, ctx.Row, ctx.Col, action);
                StudyTableFormatter.Log("study_table_action " + action);

                // La structure a pu bouger : on repart de la case où le
                // curseur a été posé, sans y rouvrir la popup.
                _editedCell = null;
                _editedTable = null;
                _editedKey = null;
                var after = TryReadContext(_app.Selection);
                _cellKey = _dismissedKey = after?.Key;
                if (after != null && IsTracked(after))
                {
                    _editedTable = after.Table;
                    _editedCell = after.Cell;
                    _editedKey = after.Key;
                    _editedText = StudyTableFormatter.CellText(after.Cell);
                }
                return true;
            }
            catch (Exception ex)
            {
                StudyTableFormatter.Log("study_table_action_error: " + ex.Message);
                return false;
            }
            finally { _committing = false; }
        }

        /// <summary>Sortie d'une frontière dont le texte a changé : police
        /// ajustée (et colonne élargie au besoin) via une réapplication.</summary>
        /// <returns>True si le tableau a été modifié.</returns>
        private bool FlushEditedFrontier()
        {
            var table = _editedTable;
            var cell = _editedCell;
            string before = _editedText;
            _editedTable = null;
            _editedCell = null;
            _editedKey = null;
            if (table == null || cell == null) return false;
            try
            {
                string after = StudyTableFormatter.CellText(cell);
                if (after == before) return false;
                // Intervalle de la ligne x : une valeur y a été écrite → elle
                // devient une nouvelle valeur de x (frontière + séparation).
                bool xInterval = cell.RowIndex == 1
                    && StudyTableTemplate.ColumnKindFromXRowStyle(StudyTableFormatter.StyleNameOf(cell.Range))
                       == StudyColumnKind.Interval;
                StudyTableFormatter.Log($"sortie de case modifiée r{cell.RowIndex}c{cell.ColumnIndex} " +
                    $"'{before}' -> '{after}'{(xInterval ? " (intervalle de la ligne x)" : "")}");
                _committing = true;
                using (new UndoRecordScope(_app, "MathCursor : ajustement du tableau"))
                {
                    // Valeur de x vidée : elle disparaît (intervalles fusionnés),
                    // sauf aux bornes — symétrique de la valeur tapée entre deux.
                    bool xValueErased = cell.RowIndex == 1 && before.Length > 0 && after.Length == 0
                        && StudyTableTemplate.ColumnKindFromXRowStyle(StudyTableFormatter.StyleNameOf(cell.Range))
                           == StudyColumnKind.Frontier;
                    if (xInterval && after.Length > 0)
                        StudyTableActions.PromoteIntervalToValue(table, cell.ColumnIndex);
                    else if (!(xValueErased && StudyTableActions.RemoveErasedValue(table, cell.ColumnIndex)))
                        StudyTableFormatter.ReapplyColumn(table, cell.ColumnIndex, "sortie de case modifiée");
                }
                return true;
            }
            catch (Exception ex)
            {
                StudyTableFormatter.Log("study_table_fit_error: " + ex.Message);
                return true;
            }
            finally { _committing = false; }
        }

        /// <summary>
        /// Case « 0 sur la ligne » = trois paragraphes [bout / 0 / bout] ; les
        /// bouts ont un interligne de quelques points : un curseur posé dedans
        /// (clic au-dessus ou en dessous du 0) écrirait du texte écrasé. On le
        /// ramène juste après le 0.
        /// </summary>
        private void KeepCaretOnZero(Word.Selection sel, Word.Cell cell)
        {
            try
            {
                if (sel.Start != sel.End) return;
                var paras = cell.Range.Paragraphs;
                if (paras.Count != 3) return;
                var zero = paras[2].Range;
                if ((zero.Text ?? "").Trim('\r', '\a', ' ') != "0") return;
                if (sel.Start >= zero.Start && sel.Start < zero.End) return;
                var target = zero.Duplicate;
                target.MoveEnd(Word.WdUnits.wdCharacter, -1);
                target.Collapse(Word.WdCollapseDirection.wdCollapseEnd);
                StudyTableFormatter.Log($"curseur ramené sur le 0 ({sel.Start} -> {target.Start})");
                _committing = true;
                try { target.Select(); }
                finally { _committing = false; }
            }
            catch { }
        }

        /// <summary>Cases suivies jusqu'à la sortie : frontières (police,
        /// séparations), étiquettes (style d'en-tête) et intervalles de la
        /// ligne x (une valeur y devient une nouvelle valeur de x).</summary>
        private static bool IsTracked(CellContext ctx) =>
            ctx.Role == StudyCellRole.Frontier
            || ctx.Role == StudyCellRole.Label   // étiquette tapée : gras / couleur d'en-tête
            || (ctx.Role == StudyCellRole.Interval && ctx.RowKind == StudyRowKind.X);

        /// <summary>
        /// Contexte si la sélection (réduite, ou contenu de la case
        /// sélectionné par Tab) est dans une seule case d'un tableau
        /// MathCursor, null sinon.
        /// </summary>
        private static CellContext TryReadContext(Word.Selection sel)
        {
            try
            {
                if (sel == null) return null;
                if (!(bool)sel.get_Information(Word.WdInformation.wdWithInTable)) return null;
                if (sel.Cells.Count != 1) return null;
                var cell = sel.Cells[1];
                if (sel.Start != sel.End && sel.End > cell.Range.End) return null;
                var table = sel.Tables[1];
                int row = cell.RowIndex, col = cell.ColumnIndex;
                // Lecture légère (à chaque clic) : quelques styles, pas tout le tableau.
                var info = StudyTableFormatter.ReadCell(table, row, col);
                if (info == null) return null;
                var rowKind = info.RowKind;
                var role = StudyTableTemplate.RoleFor(rowKind, info.ColumnKind, info.Forbidden);
                if (role == StudyCellRole.None) return null;
                return new CellContext
                {
                    Table = table,
                    Cell = cell,
                    Row = row,
                    Col = col,
                    Info = info,
                    Role = role,
                    RowKind = rowKind,
                    Key = table.Range.Start + ":" + row + ":" + col,
                };
            }
            catch { return null; }
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

        private TableChoicePopupWindow EnsurePopup()
        {
            if (_popup != null) return _popup;
            _popup = new TableChoicePopupWindow();
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
