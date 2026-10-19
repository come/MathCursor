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
    /// pur <see cref="StudyTableTemplate"/>. Même position d'insertion que les
    /// colonnes (<see cref="ColumnLayoutInserter.ResolveInsertRange"/>) ; la
    /// sélection n'est pas absorbée. Aucune OMath ni CC posée : l'élève
    /// convertit le contenu des cellules avec MathCursor comme ailleurs.
    /// Cf. ADR 2026-10-16-Feat-sign-variation-table-insert.
    /// </summary>
    internal static class StudyTableInserter
    {
        private const float LabelColumnPct = 16f;
        private const float ValueColumnPct = 12f;
        private const float IntervalColumnPct = 24f;
        private const float TallRowHeightPt = 54f;

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
                var insertRange = ColumnLayoutInserter.ResolveInsertRange(doc, sel);
                var table = doc.Tables.Add(insertRange,
                    NumRows: template.Rows.Count, NumColumns: template.ColumnCount);

                ConfigureTable(table);
                FillCells(table, template, EnsureArrowStyle(doc));
                ConfigureBorders(table, template);
                ConfigureWidths(table, template);
                if (template.TallRowIndex >= 0) ConfigureTallRow(table, template.TallRowIndex);

                // Curseur sur la valeur de x à remplir (colonne du milieu).
                try
                {
                    var rng = table.Cell(1, 4).Range;
                    rng.Collapse(Word.WdCollapseDirection.wdCollapseStart);
                    rng.Select();
                }
                catch { /* placement curseur : non critique */ }
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────

        private static void ConfigureTable(Word.Table table)
        {
            try
            {
                table.AllowAutoFit = false;
                var pf = table.Range.ParagraphFormat;
                pf.Alignment = Word.WdParagraphAlignment.wdAlignParagraphCenter;
                pf.SpaceBefore = 2f;
                pf.SpaceAfter = 2f;
                pf.LineSpacingRule = Word.WdLineSpacing.wdLineSpaceSingle;
                table.Range.Cells.VerticalAlignment = Word.WdCellVerticalAlignment.wdCellAlignVerticalCenter;
            }
            catch { /* mise en forme cosmétique : on n'échoue pas l'insertion */ }
        }

        /// <summary>
        /// Style de paragraphe des cases de flèche (centré, grand corps) :
        /// marque la case pour la palette de flèches. Créé une fois par
        /// document. Null si la création échoue (les flèches restent posées,
        /// sans palette). Cf. ADR 2026-10-19-UX-variation-arrow-palette.
        /// </summary>
        private static Word.Style EnsureArrowStyle(Word.Document doc)
        {
            try
            {
                try { return doc.Styles[StudyTableTemplate.ArrowStyleName]; }
                catch { /* absent : on le crée */ }

                var style = doc.Styles.Add(StudyTableTemplate.ArrowStyleName,
                    Word.WdStyleType.wdStyleTypeParagraph);
                style.Font.Size = StudyTableTemplate.ArrowFontSizePt;
                style.Font.Italic = 0;
                style.ParagraphFormat.Alignment = Word.WdParagraphAlignment.wdAlignParagraphCenter;
                style.ParagraphFormat.SpaceBefore = 0f;
                style.ParagraphFormat.SpaceAfter = 0f;
                style.ParagraphFormat.LineSpacingRule = Word.WdLineSpacing.wdLineSpaceSingle;
                style.QuickStyle = false;
                return style;
            }
            catch { return null; }
        }

        private static void FillCells(Word.Table table, StudyTableTemplate template, Word.Style arrowStyle)
        {
            for (int r = 0; r < template.Rows.Count; r++)
            {
                var row = template.Rows[r];
                for (int c = 0; c < row.Count; c++)
                {
                    var spec = row[c];
                    var cell = table.Cell(r + 1, c + 1);
                    if (spec.IsArrow && arrowStyle != null)
                    {
                        try { object st = arrowStyle; cell.Range.set_Style(ref st); }
                        catch { /* sans style : flèche sans palette */ }
                    }
                    if (spec.Text.Length > 0) cell.Range.Text = spec.Text;
                    try
                    {
                        if (spec.Italic) cell.Range.Font.Italic = 1;
                        if (spec.VAlign != CellVAlign.Center)
                            cell.VerticalAlignment = spec.VAlign == CellVAlign.Top
                                ? Word.WdCellVerticalAlignment.wdCellAlignVerticalTop
                                : Word.WdCellVerticalAlignment.wdCellAlignVerticalBottom;
                    }
                    catch { /* idem */ }
                }
            }
        }

        /// <summary>
        /// Cadre extérieur + séparateurs horizontaux + un trait vertical après la
        /// colonne d'étiquettes ; pas de trait entre les colonnes de valeurs.
        /// </summary>
        private static void ConfigureBorders(Word.Table table, StudyTableTemplate template)
        {
            try
            {
                Line(table.Borders[Word.WdBorderType.wdBorderTop]);
                Line(table.Borders[Word.WdBorderType.wdBorderBottom]);
                Line(table.Borders[Word.WdBorderType.wdBorderLeft]);
                Line(table.Borders[Word.WdBorderType.wdBorderRight]);
                Line(table.Borders[Word.WdBorderType.wdBorderHorizontal]);
                table.Borders[Word.WdBorderType.wdBorderVertical].LineStyle = Word.WdLineStyle.wdLineStyleNone;
                for (int r = 1; r <= template.Rows.Count; r++)
                    Line(table.Cell(r, 1).Borders[Word.WdBorderType.wdBorderRight]);
            }
            catch { /* idem */ }
        }

        private static void Line(Word.Border border)
        {
            border.LineStyle = Word.WdLineStyle.wdLineStyleSingle;
            border.LineWidth = Word.WdLineWidth.wdLineWidth075pt;
        }

        private static void ConfigureWidths(Word.Table table, StudyTableTemplate template)
        {
            try
            {
                table.PreferredWidthType = Word.WdPreferredWidthType.wdPreferredWidthPercent;
                table.PreferredWidth = 100f;
                foreach (Word.Cell cell in table.Range.Cells)
                {
                    int col = cell.ColumnIndex - 1;
                    cell.PreferredWidthType = Word.WdPreferredWidthType.wdPreferredWidthPercent;
                    cell.PreferredWidth = col == 0 ? LabelColumnPct
                        : StudyTableTemplate.IsValueColumn(col) ? ValueColumnPct
                        : IntervalColumnPct;
                }
            }
            catch { /* idem */ }
        }

        private static void ConfigureTallRow(Word.Table table, int rowIndex)
        {
            try
            {
                var row = table.Rows[rowIndex + 1];
                row.HeightRule = Word.WdRowHeightRule.wdRowHeightAtLeast;
                row.Height = TallRowHeightPt;
            }
            catch { /* idem */ }
        }
    }
}
