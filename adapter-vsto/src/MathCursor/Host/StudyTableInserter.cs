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
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Insère au curseur un tableau de signe / de variation préformaté (vrai
    /// tableau Word, texte simple, flèches caractère ↗ ↘) à partir du gabarit
    /// pur <see cref="StudyTableTemplate"/>. Chaque case reçoit le style de
    /// son rôle, puis <see cref="StudyTableFormatter.Reapply"/> pose bordures,
    /// largeurs, hauteurs et alignements. Même position d'insertion que les
    /// colonnes (<see cref="ColumnLayoutInserter.ResolveInsertRange"/>) ; la
    /// sélection n'est pas absorbée. Aucune OMath ni CC posée.
    /// Cf. ADR 2026-10-16-Feat-sign-variation-table-insert,
    /// 2026-10-19-Feat-study-table-cell-roles-contextual-popup.
    /// </summary>
    internal static class StudyTableInserter
    {
        public static void Insert(Word.Application app, StudyTableKind kind)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            var doc = app.ActiveDocument;
            if (doc == null) return;
            var sel = app.Selection;
            if (sel == null) return;

            var template = StudyTableTemplate.Build(kind);
            string undoName = kind == StudyTableKind.Sign
                ? "MathCursor : tableau de signe"
                : "MathCursor : tableau de variation";

            using (new UndoRecordScope(app, undoName))
            {
                StudyTableFormatter.EnsureStyles(doc);
                var insertRange = ColumnLayoutInserter.ResolveInsertRange(doc, sel);
                var table = doc.Tables.Add(insertRange,
                    NumRows: template.Rows.Count, NumColumns: template.ColumnCount);

                FillCells(table, template);
                StudyTableFormatter.Reapply(table, "insertion " + kind);

                // Valeur de x du milieu (« 0 » par défaut) sélectionnée : il
                // suffit de taper pour la remplacer.
                try
                {
                    var rng = table.Cell(1, 4).Range;
                    rng.MoveEnd(Word.WdUnits.wdCharacter, -1);
                    rng.Select();
                }
                catch { /* placement curseur : non critique */ }
            }
        }

        /// <summary>
        /// Étiquette du gabarit (« f(x) ») posée en équation : le texte est déjà
        /// dans la case, <c>OMaths.Add</c> sur cette plage NON vide (règle
        /// word-api-helpers §4 : jamais sur une plage vide) puis <c>BuildUp</c>,
        /// display centrée, police math du réglage. Hors pipeline de conversion
        /// (pas d'entrée de source : le mode édition est coupé sur les
        /// étiquettes). Échec = le texte reste, sans équation.
        /// </summary>
        private static void PutEquation(Word.Document doc, Word.Cell cell)
        {
            try
            {
                var text = cell.Range;
                text.MoveEnd(Word.WdUnits.wdCharacter, -1);
                if (text.Start >= text.End) return;
                doc.OMaths.Add(text);
                var om = cell.Range.OMaths[1];
                om.BuildUp();
                om = cell.Range.OMaths[1];
                if (om.Type != Word.WdOMathType.wdOMathDisplay) om.Type = Word.WdOMathType.wdOMathDisplay;
                om.Justification = Word.WdOMathJc.wdOMathJcCenter;
                var mathFont = Settings.SettingsStore.Current?.MathFont;
                if (!string.IsNullOrEmpty(mathFont)) MathFontApplier.ApplyToRange(om.Range, mathFont);
            }
            catch (Exception ex) { StudyTableFormatter.Log("étiquette_équation_error: " + ex.Message); }
        }

        private static void FillCells(Word.Table table, StudyTableTemplate template)
        {
            for (int r = 0; r < template.Rows.Count; r++)
            {
                var rowKind = template.RowKinds[r];
                var row = template.Rows[r];
                for (int c = 0; c < row.Count; c++)
                {
                    var cell = table.Cell(r + 1, c + 1);
                    var role = StudyTableTemplate.RoleFor(rowKind, StudyTableTemplate.TemplateColumnKind(c));
                    StudyTableFormatter.SetStyle(cell, StudyTableTemplate.StyleFor(role, rowKind));

                    var spec = row[c];
                    if (spec.Text.Length > 0) cell.Range.Text = spec.Text;
                    if (spec.IsMath && spec.Text.Length > 0) PutEquation(table.Range.Document, cell);
                    else if (spec.Italic)
                    {
                        try { cell.Range.Font.Italic = 1; } catch { }
                    }
                }
            }
        }
    }
}
