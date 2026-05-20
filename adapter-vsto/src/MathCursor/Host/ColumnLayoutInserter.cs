using System;
using System.Collections.Generic;
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
        // Source de vérité : SuggestionService.BookmarkPrefix. Dupliqué ici
        // pour éviter une dépendance circulaire ; ne pas changer sans
        // changer là-bas aussi (le lookup edit mode utilise ce préfixe).
        private const string BookmarkPrefix = "mcEq_";

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
                // Capture les handles MathCursor des OMaths présents dans
                // la sélection AVANT le move : les bookmarks Word sont
                // positionnels et NE suivent PAS le contenu copié. Sans
                // ça, après FormattedText copy + Delete, les OMaths déplacés
                // se retrouvent orphelins (plus de bookmark → plus de source
                // → plus de edit mode "revenir à la saisie initiale").
                var omathHandles = CollectOMathHandlesInRange(doc, sel.Range);

                // Copie FormattedText (préserve OMaths + styles + runs)
                // dans col 1, puis supprime la source.
                var col1 = table.Cell(1, 1).Range;
                col1.FormattedText = sel.Range.FormattedText;
                contentInCol1 = true;
                sel.Range.Delete();

                // Re-crée les bookmarks sur les OMaths déplacés en col 1.
                // L'ordre des OMaths dans la copie suit l'ordre source
                // (FormattedText est une copie séquentielle).
                ReattachOMathBookmarks(doc, table.Cell(1, 1).Range, omathHandles);
            }

            ConfigureBordersAndWidths(table, columnCount);
            PlaceCursorAfterInsertion(table, columnCount, contentInCol1);
        }

        /// <summary>
        /// Liste les handles MathCursor (= partie après <c>mcEq_</c> du
        /// nom de bookmark) des OMaths contenus dans <paramref name="range"/>,
        /// dans l'ordre du document. Un OMath sans bookmark MC (= pas à
        /// nous) est ignoré silencieusement.
        /// </summary>
        private static List<string> CollectOMathHandlesInRange(Word.Document doc, Word.Range range)
        {
            var handles = new List<string>();
            try
            {
                foreach (Word.OMath om in range.OMaths)
                {
                    int omStart = om.Range.Start;
                    int omEnd = om.Range.End;
                    foreach (Word.Bookmark bm in doc.Bookmarks)
                    {
                        if (!bm.Name.StartsWith(BookmarkPrefix, StringComparison.Ordinal)) continue;
                        var r = bm.Range;
                        // Bookmark couvre/touche l'OMath (tolérance 1 char trailing).
                        if (r.Start <= omStart && r.End >= omEnd - 1)
                        {
                            handles.Add(bm.Name.Substring(BookmarkPrefix.Length));
                            break;
                        }
                    }
                }
            }
            catch { /* best-effort : on perd au pire le edit mode */ }
            return handles;
        }

        /// <summary>
        /// Re-crée les bookmarks <c>mcEq_&lt;handle&gt;</c> sur les OMaths
        /// déplacés dans <paramref name="targetRange"/>, dans l'ordre.
        /// L'ordre source ↔ cible est préservé par <c>FormattedText</c>
        /// (copie séquentielle des éléments).
        /// </summary>
        private static void ReattachOMathBookmarks(Word.Document doc, Word.Range targetRange, List<string> handles)
        {
            if (handles == null || handles.Count == 0) return;
            try
            {
                int i = 0;
                foreach (Word.OMath om in targetRange.OMaths)
                {
                    if (i >= handles.Count) break;
                    string name = BookmarkPrefix + handles[i];
                    try
                    {
                        if (doc.Bookmarks.Exists(name)) doc.Bookmarks[name].Delete();
                        doc.Bookmarks.Add(name, om.Range);
                    }
                    catch { /* un bookmark raté n'empêche pas les autres */ }
                    i++;
                }
            }
            catch { /* idem best-effort */ }
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
