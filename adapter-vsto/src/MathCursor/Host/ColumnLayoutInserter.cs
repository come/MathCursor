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
    /// <para>Port DocMath (ADR 2026-05-11-Feat-ribbon-home-duo-plus-dedicated-tab),
    /// restauré par l'ADR <c>2026-06-10-Feat-ribbon-columns-settings-culture</c>.
    /// La logique de réattachement de bookmarks <c>mcEq_</c> de l'original a été
    /// SUPPRIMÉE : le pattern anchor CC (Tag JSON <see cref="CCMeta.MCMeta"/>)
    /// voyage avec la copie <c>FormattedText</c> — vérifié par POC
    /// (<c>scripts/poc-formattedtext-cc.ps1</c> : ccCount=1, tagOk=True).</para>
    ///
    /// <para>Règles d'insertion (décision user 2026-05-11) :
    /// <list type="bullet">
    /// <item><b>Caret seul</b> → table vide insérée EN DESSOUS du ¶
    /// courant, curseur en col 1.</item>
    /// <item><b>Sélection (partielle ou ¶ entier)</b> → table insérée EN
    /// DESSOUS du ¶ courant, contenu sélectionné (FormattedText) copié
    /// en col 1, source supprimée, curseur en col 1 après le contenu.</item>
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

            // Position d'insertion :
            //  - ¶ courant VIDE → table À LA PLACE du ¶ vide (= insertion à
            //    currentPara.Start). Plus naturel ergo : "je clique sur une
            //    ligne vide, la table apparaît là". Évite aussi le bug "out
            //    of bounds" quand on insère à la frontière entre 2 ¶s.
            //  - ¶ courant NON VIDE → table EN DESSOUS, à currentPara.End.
            //    Si on est sur le dernier ¶ du doc, on ajoute d'abord un
            //    saut de ligne pour avoir une zone d'accueil valide.
            bool paraIsEmpty = currentPara.Range.End - currentPara.Range.Start <= 1;
            int insertPos;
            if (paraIsEmpty)
            {
                insertPos = currentPara.Range.Start;
            }
            else
            {
                insertPos = currentPara.Range.End;
                if (insertPos >= doc.Content.End)
                {
                    currentPara.Range.InsertParagraphAfter();
                    insertPos = currentPara.Range.End;
                }
            }

            var insertRange = doc.Range(insertPos, insertPos);
            var table = doc.Tables.Add(insertRange, NumRows: 1, NumColumns: columnCount);

            bool contentInCol1 = false;
            if (hasSelection)
            {
                // Copie FormattedText (préserve OMaths + anchor CCs + styles
                // + runs) dans col 1, puis supprime la source.
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
        /// Curseur TOUJOURS en col 1 : c'est là qu'on a posé le contenu
        /// (ou la cellule vide si pas de sélection). L'utilisateur reste
        /// "au niveau" de sa sélection initiale, pas téléporté en col 2.
        /// </summary>
        private static void PlaceCursorAfterInsertion(Word.Table table, int columnCount, bool wrappedSelection)
        {
            try
            {
                var rng = table.Cell(1, 1).Range;
                // Pour une cellule contenant déjà du texte/OMath, on
                // collapse à la fin pour que le curseur soit après le
                // contenu (prêt à continuer à taper). Cell vide : End =
                // Start, no-op.
                rng.Collapse(wrappedSelection
                    ? Word.WdCollapseDirection.wdCollapseEnd
                    : Word.WdCollapseDirection.wdCollapseStart);
                rng.Select();
            }
            catch { /* placement curseur : non critique */ }
        }
    }
}
