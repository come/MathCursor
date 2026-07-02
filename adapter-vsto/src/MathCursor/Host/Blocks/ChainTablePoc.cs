// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
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
            log = log ?? LogDiag;
            var doc = app.ActiveDocument;
            if (doc == null) { log("poc-chain: pas de document actif"); return; }
            var sel = app.Selection;
            log("poc-chain: START");

            using (new UndoRecordScope(app, "MathCursor : POC chaîne (tableau)"))
            {
                // ¶ frais pour accueillir le tableau.
                sel.TypeParagraph();
                int anchor = sel.Start;

                var table = doc.Tables.Add(doc.Range(anchor, anchor), 3, 2);
                table.Borders.Enable = 0;                    // invisible
                table.Range.ParagraphFormat.SpaceAfter = 2;  // lignes serrées
                table.Range.ParagraphFormat.SpaceBefore = 0;
                log($"poc-chain: table 3x2 créée à {anchor}");

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
                InsertInCell(app, inserter, table, 1, 1, "f(x)", "f(x)", log);
                InsertInCell(app, inserter, table, 1, 2, "=2x+2-2", "= 2x+2-2", log);
                InsertInCell(app, inserter, table, 2, 2, "=2x", "= 2x", log);
                InsertInCell(app, inserter, table, 3, 2, "=2\\cdot x", "= 2.x", log);

                // Caret après le tableau.
                try
                {
                    int after = table.Range.End;
                    sel.SetRange(after, after);
                }
                catch (Exception ex) { log("poc-chain: caret après table KO: " + ex.Message); }
                log("poc-chain: DONE");
            }
            try { app.StatusBar = "MathCursor : POC chaîne inséré — à torturer (suppressions, Ctrl+Z, frappe autour)"; } catch { }
        }

        private static void InsertInCell(Word.Application app, OMathInserter inserter,
            Word.Table table, int row, int col, string latex, string source, Action<string> log)
        {
            try
            {
                // Cellule re-résolue à CHAQUE insert : les inserts précédents
                // décalent les positions, un Cell gardé d'avant est périmé.
                var cell = table.Cell(row, col);
                int p = cell.Range.Start;
                var sel = app.Selection;
                sel.SetRange(p, p);
                // Placeholder NON-BLANC : OMathInserter trim les espaces aux
                // bords de la plage (un espace seul → plage vide → abandon,
                // bug du 1er essai POC).
                sel.TypeText("¤");
                log($"poc-chain: cell({row},{col}) insert à {p} latex=\"{latex}\"");
                var (s, e, h) = inserter.Insert(p, p + 1, latex, source);
                log($"poc-chain: cell({row},{col}) → [{s},{e}) handle={(h ?? "NULL")}");
            }
            catch (Exception ex) { log($"poc-chain: cell({row},{col}) EXCEPTION: {ex.Message}"); }
        }

        private static void LogDiag(string message)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} poc {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
