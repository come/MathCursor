using System;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Blocks
{
    /// <summary>
    /// POC M0 du chantier multiligne (option B, « tableau invisible, un
    /// OMath par cellule ») — cf. discussion archi 2026-06-10. Insère au
    /// caret un tableau 2 colonnes sans bordures simulant la chaîne :
    ///
    /// <code>
    ///   f(x) | = 2x+2-2
    ///        | = 2x
    ///        | = 2·x
    /// </code>
    ///
    /// Chaque cellule passe par le VRAI pipeline (OMathInserter : ZWSP +
    /// OMML + anchor CC) — conditions réelles pour le script de torture :
    /// supprimer une ligne du milieu, Backspace en début de cellule,
    /// supprimer le bloc entier, Ctrl+Z, taper avant/après le tableau,
    /// vérifier qu'aucun « tableau fantôme » ne survit à la suppression
    /// des équations. À retirer une fois M0 tranché.
    /// </summary>
    internal static class ChainTablePoc
    {
        public static void Run(Word.Application app, Action<string> log = null)
        {
            log = log ?? (_ => { });
            var doc = app.ActiveDocument;
            if (doc == null) return;
            var sel = app.Selection;

            using (new UndoRecordScope(app, "MathCursor : POC chaîne (tableau)"))
            {
                // ¶ frais pour accueillir le tableau.
                sel.TypeParagraph();
                int anchor = sel.Start;

                var table = doc.Tables.Add(doc.Range(anchor, anchor), 3, 2);
                table.Borders.Enable = 0;                    // invisible
                table.Range.ParagraphFormat.SpaceAfter = 2;  // lignes serrées
                table.Range.ParagraphFormat.SpaceBefore = 0;

                // Col 1 = membres gauches alignés à droite (bord de colonne =
                // ligne d'alignement des signes) ; col 2 = relation + membre
                // droit, alignés à gauche.
                table.Columns[1].Width = app.CentimetersToPoints(3.5f);
                table.Columns[2].Width = app.CentimetersToPoints(9f);
                for (int r = 1; r <= 3; r++)
                {
                    table.Cell(r, 1).Range.ParagraphFormat.Alignment = Word.WdParagraphAlignment.wdAlignParagraphRight;
                    table.Cell(r, 2).Range.ParagraphFormat.Alignment = Word.WdParagraphAlignment.wdAlignParagraphLeft;
                }

                // Contenu — ligne 1 SCINDÉE au signe (le = de la ligne 1
                // s'aligne avec ceux du dessous au bord de colonne).
                var inserter = new OMathInserter(app, log);
                InsertInCell(app, inserter, table.Cell(1, 1), "f(x)", "f(x)");
                InsertInCell(app, inserter, table.Cell(1, 2), "=2x+2-2", "= 2x+2-2");
                InsertInCell(app, inserter, table.Cell(2, 2), "=2x", "= 2x");
                InsertInCell(app, inserter, table.Cell(3, 2), "=2\\cdot x", "= 2.x");

                // Caret après le tableau.
                try
                {
                    int after = table.Range.End;
                    sel.SetRange(after, after);
                }
                catch { }
                log("poc-chain: tableau 3x2 inséré");
            }
        }

        private static void InsertInCell(Word.Application app, OMathInserter inserter,
            Word.Cell cell, string latex, string source)
        {
            // OMathInserter exige une plage non vide à remplacer → on tape
            // un espace placeholder dans la cellule puis on le remplace.
            var sel = app.Selection;
            int p = cell.Range.Start;
            sel.SetRange(p, p);
            sel.TypeText(" ");
            inserter.Insert(p, p + 1, latex, source);
        }
    }
}
