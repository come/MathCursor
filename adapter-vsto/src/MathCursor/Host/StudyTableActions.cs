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

using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Actions de la popup du tableau sur une case (ligne / colonne 1-based).
    /// Chaque action modifie la structure, réapplique la mise en forme
    /// (<see cref="StudyTableFormatter.Reapply"/>) et pose le curseur. Aucune
    /// fusion de cellules (bloquerait <c>Columns.Add</c>). L'appelant ouvre
    /// l'enregistrement d'annulation.
    /// Cf. ADR 2026-10-19-Feat-study-table-cell-roles-contextual-popup.
    /// </summary>
    internal static class StudyTableActions
    {
        public static void Run(Word.Table table, int row, int col, StudyTableAction action)
        {
            var s = StudyTableFormatter.Read(table);
            if (s == null) return;

            switch (action)
            {
                case StudyTableAction.ArrowUp:
                case StudyTableAction.ArrowDown:
                case StudyTableAction.ArrowRight:
                    WriteAndStay(table, row, col, StudyTableTemplate.ArrowText(action));
                    break;
                case StudyTableAction.Zero:
                    WriteZero(table, row, col);
                    break;
                case StudyTableAction.ToggleForbidden:
                    ToggleForbidden(table, s, col);
                    break;
                case StudyTableAction.AddValueLeft:
                case StudyTableAction.AddValueRight:
                    AddValue(table, s, col, action == StudyTableAction.AddValueRight);
                    break;
                case StudyTableAction.DeleteValue:
                    DeleteValue(table, s, col);
                    break;
                case StudyTableAction.AddSignRow:
                case StudyTableAction.AddVariationRow:
                    AddRow(table, row, action == StudyTableAction.AddVariationRow
                        ? StudyRowKind.Variation : StudyRowKind.Sign);
                    break;
                case StudyTableAction.DeleteRow:
                    DeleteRow(table, s, row);
                    break;
            }
        }

        private static void WriteAndStay(Word.Table table, int row, int col, string text)
        {
            var written = StudyTableFormatter.SetCellText(table.Cell(row, col), text);
            // Contenu seul : on ne réaligne que la ligne (extremums), sans
            // toucher la ligne x, les largeurs ni les polices.
            StudyTableFormatter.ReapplyRowAlignments(table, row, $"'{text}' en r{row}c{col}");
            written.Collapse(Word.WdCollapseDirection.wdCollapseEnd);
            written.Select();
        }

        /// <summary>« 0 » dans une frontière : la colonne prend la simple
        /// ligne (le 0 l'interrompt), curseur juste après le 0.</summary>
        private static void WriteZero(Word.Table table, int row, int col)
        {
            var cell = table.Cell(row, col);
            StudyTableFormatter.SetCellText(cell, "0");
            StudyTableFormatter.ReapplyColumnShapes(table, col, $"'0' en r{row}c{col}");
            try
            {
                foreach (Word.Paragraph p in cell.Range.Paragraphs)
                {
                    if ((p.Range.Text ?? "").Trim('\r', '\a', ' ') != "0") continue;
                    var rng = p.Range;
                    rng.MoveEnd(Word.WdUnits.wdCharacter, -1);
                    rng.Collapse(Word.WdCollapseDirection.wdCollapseEnd);
                    rng.Select();
                    break;
                }
            }
            catch { }
        }

        /// <summary>Bascule toute la colonne (lignes sous x) : valeur
        /// interdite ↔ frontière normale. Le contenu des cases est effacé
        /// à l'activation (la double barre remplace le 0 / l'extremum).</summary>
        private static void ToggleForbidden(Word.Table table, StudyTableStructure s, int col)
        {
            bool makeForbidden = !s.Forbidden.Contains(col - 1);
            string style = makeForbidden ? StudyTableTemplate.ForbiddenStyle : StudyTableTemplate.FrontierStyle;
            for (int r = 2; r <= s.Rows.Count; r++)
            {
                var cell = table.Cell(r, col);
                if (makeForbidden) StudyTableFormatter.SetCellText(cell, "");
                StudyTableFormatter.SetStyle(cell, style);
            }
            StudyTableFormatter.Reapply(table, "valeur interdite c" + col);
            SelectCellStart(table, 1, col);
        }

        private static void AddValue(Word.Table table, StudyTableStructure s, int col, bool right)
        {
            var (before, first, second) = StudyTableTemplate.PlanAddValue(col - 1, right, s.Columns.Count);
            int p = before + 1;  // 1-based : position de la première nouvelle colonne
            if (before >= s.Columns.Count)
            {
                table.Columns.Add();
                table.Columns.Add();
            }
            else
            {
                table.Columns.Add(table.Columns[p]);
                table.Columns.Add(table.Columns[p + 1]);
            }

            // Le rôle des colonnes se lit sur la ligne x : on l'y pose avant
            // la réapplication (Word a recopié le style de la voisine).
            SetXRowKind(table, p, first);
            SetXRowKind(table, p + 1, second);
            for (int r = 1; r <= table.Rows.Count; r++)
            {
                StudyTableFormatter.SetCellText(table.Cell(r, p), "");
                StudyTableFormatter.SetCellText(table.Cell(r, p + 1), "");
            }
            StudyTableFormatter.Reapply(table, (right ? "valeur à droite de c" : "valeur à gauche de c") + col);
            SelectCellStart(table, 1, first == StudyColumnKind.Frontier ? p : p + 1);
        }

        /// <summary>
        /// Valeur écrite dans un INTERVALLE de la ligne x (case vide entre deux
        /// valeurs) : elle devient une nouvelle valeur de x. On insère un
        /// intervalle de chaque côté, la case devient frontière ; dans les
        /// autres lignes, le contenu de l'intervalle d'origine (signe, flèche)
        /// est recopié dans les deux nouveaux intervalles et la nouvelle
        /// frontière reste vide. Le curseur n'est pas déplacé (l'élève est déjà
        /// ailleurs : appelé à la sortie de la case).
        /// </summary>
        public static void PromoteIntervalToValue(Word.Table table, int col)
        {
            var s = StudyTableFormatter.Read(table);
            if (s == null || col < 2 || col > s.Columns.Count
                || s.Columns[col - 1] != StudyColumnKind.Interval) return;

            table.Columns.Add(table.Columns[col]);       // nouvel intervalle à gauche (col)
            int value = col + 1;                          // la case écrite, décalée d'un cran
            if (value + 1 <= table.Columns.Count) table.Columns.Add(table.Columns[value + 1]);
            else table.Columns.Add();                     // nouvel intervalle à droite (value + 1)

            SetXRowKind(table, col, StudyColumnKind.Interval);
            SetXRowKind(table, value, StudyColumnKind.Frontier);
            SetXRowKind(table, value + 1, StudyColumnKind.Interval);
            StudyTableFormatter.SetCellText(table.Cell(1, col), "");
            StudyTableFormatter.SetCellText(table.Cell(1, value + 1), "");

            for (int r = 2; r <= table.Rows.Count; r++)
            {
                var middle = table.Cell(r, value);
                var content = middle.Range;
                content.MoveEnd(Word.WdUnits.wdCharacter, -1);
                foreach (int target in new[] { col, value + 1 })
                {
                    var dest = table.Cell(r, target).Range;
                    dest.MoveEnd(Word.WdUnits.wdCharacter, -1);
                    dest.FormattedText = content.FormattedText;
                }
                StudyTableFormatter.SetCellText(middle, "");
            }
            StudyTableFormatter.Reapply(table, "valeur écrite dans l'intervalle c" + col);
        }

        /// <summary>
        /// Valeur intérieure de la ligne x VIDÉE (symétrique de
        /// <see cref="PromoteIntervalToValue"/>) : la valeur disparaît et ses
        /// deux intervalles fusionnent — on supprime la frontière et
        /// l'intervalle de DROITE, celui de gauche garde son contenu (signe,
        /// flèche). Les bornes (première / dernière valeur) ne sont jamais
        /// supprimées. False si rien n'a été supprimé.
        /// </summary>
        public static bool RemoveErasedValue(Word.Table table, int col)
        {
            var s = StudyTableFormatter.Read(table);
            if (s == null || col < 1 || col > s.Columns.Count) return false;
            int first = s.Columns.IndexOf(StudyColumnKind.Frontier) + 1;
            int last = s.Columns.LastIndexOf(StudyColumnKind.Frontier) + 1;
            if (col == first || col == last) return false;
            var plan = StudyTableTemplate.PlanDeleteValue(s.Columns, col - 1);
            if (plan == null) return false;
            foreach (int c in plan) table.Columns[c + 1].Delete();
            StudyTableFormatter.Reapply(table, "valeur effacée c" + col);
            return true;
        }

        private static void SetXRowKind(Word.Table table, int col, StudyColumnKind kind)
        {
            StudyTableFormatter.SetStyle(table.Cell(1, col), kind == StudyColumnKind.Frontier
                ? StudyTableTemplate.HeaderFrontierStyle : StudyTableTemplate.HeaderIntervalStyle);
        }

        private static void DeleteValue(Word.Table table, StudyTableStructure s, int col)
        {
            var plan = StudyTableTemplate.PlanDeleteValue(s.Columns, col - 1);
            if (plan == null) return;
            foreach (int c in plan) table.Columns[c + 1].Delete();
            StudyTableFormatter.Reapply(table, "suppression valeur c" + col);
            int target = System.Math.Min(plan[1] + 1, table.Columns.Count);
            SelectCellStart(table, 1, target);
        }

        private static void AddRow(Word.Table table, int afterRow, StudyRowKind kind)
        {
            int newRow = afterRow + 1;
            if (afterRow >= table.Rows.Count) table.Rows.Add();
            else table.Rows.Add(table.Rows[newRow]);

            // Word recopie la ligne voisine (styles, hauteur) : on vide et on
            // pose le type de la nouvelle ligne sur son étiquette.
            for (int c = 1; c <= table.Columns.Count; c++)
                StudyTableFormatter.SetCellText(table.Cell(newRow, c), "");
            StudyTableFormatter.SetStyle(table.Cell(newRow, 1), kind == StudyRowKind.Variation
                ? StudyTableTemplate.LabelVariationStyle : StudyTableTemplate.LabelSignStyle);
            StudyTableFormatter.Reapply(table, "ligne " + kind + " après r" + afterRow);
            SelectCellStart(table, newRow, 1);
        }

        private static void DeleteRow(Word.Table table, StudyTableStructure s, int row)
        {
            if (row <= 1 || s.Rows.Count <= 2) return;
            table.Rows[row].Delete();
            StudyTableFormatter.Reapply(table, "suppression ligne r" + row);
            SelectCellStart(table, row - 1, 1);
        }

        private static void SelectCellStart(Word.Table table, int row, int col)
        {
            try
            {
                var rng = table.Cell(row, col).Range;
                rng.Collapse(Word.WdCollapseDirection.wdCollapseStart);
                rng.Select();
            }
            catch { }
        }
    }
}
