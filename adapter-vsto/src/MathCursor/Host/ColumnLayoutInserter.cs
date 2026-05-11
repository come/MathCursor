using System;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Insère un tableau 1×N au curseur configuré "barres séparatrices
    /// uniquement" : toutes les bordures externes off, seules les
    /// bordures verticales internes entre cellules sont visibles.
    /// Cellules de largeur égale (100% / N).
    ///
    /// <para>Cf. ADR <c>2026-05-11-Feat-ribbon-home-duo-plus-dedicated-tab</c>.</para>
    ///
    /// <para>Règles d'insertion (cf. décision user 2026-05-11) :
    /// <list type="bullet">
    /// <item><b>Caret seul</b> → table vide insérée EN DESSOUS du ¶
    /// courant, curseur en col 1.</item>
    /// <item><b>Sélection (partielle ou ¶ entier)</b> → table insérée EN
    /// DESSOUS du ¶ courant, contenu sélectionné (FormattedText) copié
    /// en col 1, source supprimée, curseur en col 2.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static class ColumnLayoutInserter
    {
        public static void Insert(Word.Application app, int columnCount)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (columnCount < 1) throw new ArgumentOutOfRangeException(nameof(columnCount));

            var doc = app.ActiveDocument;
            if (doc == null) return;
            var sel = app.Selection;
            if (sel == null) return;

            var currentPara = sel.Paragraphs[1];
            bool hasSelection = sel.Start != sel.End;

            // Position d'insertion = juste après le ¶ courant. Si on est
            // sur le DERNIER ¶ (Range.End == doc.Content.End), on insère
            // un saut de ligne pour avoir une zone valide d'accueil de
            // la table. Sinon on insère à currentPara.Range.End (= début
            // du ¶ suivant), pas de modification de structure.
            int afterPara = currentPara.Range.End;
            int docEnd = doc.Content.End;
            if (afterPara >= docEnd)
            {
                // Dernier ¶ du doc : on doit créer une zone après pour
                // pouvoir y caler la table. InsertParagraphAfter étend le doc.
                currentPara.Range.InsertParagraphAfter();
                afterPara = currentPara.Range.End;
            }

            var insertRange = doc.Range(afterPara, afterPara);
            var table = doc.Tables.Add(insertRange, NumRows: 1, NumColumns: columnCount);

            bool contentInCol1 = false;
            if (hasSelection)
            {
                // Copie FormattedText (préserve OMaths + styles + runs)
                // dans col 1, puis supprime la source. Ordre : copy avant
                // delete pour que Word fasse une vraie copie interne.
                var col1 = table.Cell(1, 1).Range;
                col1.FormattedText = sel.Range.FormattedText;
                contentInCol1 = true;
                sel.Range.Delete();
            }

            ConfigureBordersAndWidths(table, columnCount);
            PlaceCursorAfterInsertion(table, columnCount, contentInCol1);
        }

        // ─── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Désactive toutes les bordures externes + internes horizontales,
        /// active la séparatrice verticale interne entre cellules (single
        /// 0.5pt) si plus d'une colonne. Cellules à largeur égale en %.
        /// </summary>
        private static void ConfigureBordersAndWidths(Word.Table table, int columnCount)
        {
            try
            {
                table.Borders[Word.WdBorderType.wdBorderTop].LineStyle = Word.WdLineStyle.wdLineStyleNone;
                table.Borders[Word.WdBorderType.wdBorderBottom].LineStyle = Word.WdLineStyle.wdLineStyleNone;
                table.Borders[Word.WdBorderType.wdBorderLeft].LineStyle = Word.WdLineStyle.wdLineStyleNone;
                table.Borders[Word.WdBorderType.wdBorderRight].LineStyle = Word.WdLineStyle.wdLineStyleNone;
                table.Borders[Word.WdBorderType.wdBorderHorizontal].LineStyle = Word.WdLineStyle.wdLineStyleNone;
                table.Borders[Word.WdBorderType.wdBorderVertical].LineStyle = columnCount > 1
                    ? Word.WdLineStyle.wdLineStyleSingle
                    : Word.WdLineStyle.wdLineStyleNone;
                if (columnCount > 1)
                    table.Borders[Word.WdBorderType.wdBorderVertical].LineWidth = Word.WdLineWidth.wdLineWidth050pt;
            }
            catch { /* styling cosmétique : on n'échoue pas l'insertion pour ça */ }

            try
            {
                table.PreferredWidthType = Word.WdPreferredWidthType.wdPreferredWidthPercent;
                table.PreferredWidth = 100f;
                float cellPct = 100f / columnCount;
                foreach (Word.Cell cell in table.Range.Cells)
                {
                    cell.PreferredWidthType = Word.WdPreferredWidthType.wdPreferredWidthPercent;
                    cell.PreferredWidth = cellPct;
                }
            }
            catch { /* idem largeurs */ }
        }

        /// <summary>
        /// Curseur en col 1 si table vide, sinon col 2 (ou col 1 si une
        /// seule colonne — fallback safe).
        /// </summary>
        private static void PlaceCursorAfterInsertion(Word.Table table, int columnCount, bool wrappedSelection)
        {
            try
            {
                int targetCol = (wrappedSelection && columnCount >= 2) ? 2 : 1;
                if (targetCol > columnCount) targetCol = columnCount;
                var rng = table.Cell(1, targetCol).Range;
                rng.Collapse(Word.WdCollapseDirection.wdCollapseStart);
                rng.Select();
            }
            catch { /* placement curseur : non critique */ }
        }
    }
}
