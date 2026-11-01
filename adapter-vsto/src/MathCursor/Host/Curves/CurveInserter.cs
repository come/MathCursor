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
using System.IO;
using Office = Microsoft.Office.Core;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Curves
{
    /// <summary>
    /// Pose l'image des courbes dans le document : sous le paragraphe courant (ou
    /// dans ce paragraphe s'il est vide), centrée, ou à la place d'une image
    /// existante. Une seule entrée d'annulation par opération (UndoRecord).
    /// Recette validée par POC COM le 31/10 : mémoriser la fin du paragraphe AVANT
    /// InsertParagraphAfter (la plage s'étend au nouveau paragraphe), y insérer.
    /// ADR 2026-10-31-Feat-insert-curves.
    /// </summary>
    internal static class CurveInserter
    {
        private const float PointsPerCm = 28.3465f;

        /// <summary>Image de courbes sélectionnée (clic dessus), sinon null.</summary>
        public static Word.InlineShape SelectedCurve(Word.Application app)
        {
            try
            {
                var sel = app?.Selection;
                if (sel == null || sel.InlineShapes.Count == 0) return null;
                foreach (Word.InlineShape s in sel.InlineShapes)
                    if (CurveSpec.IdFromTitle(SafeTitle(s)) != null) return s;
            }
            catch { }
            return null;
        }

        public static string SafeTitle(Word.InlineShape s)
        {
            try { return s.Title; } catch { return null; }
        }

        /// <summary>Insère (existing == null) ou remplace l'image. La largeur d'une image
        /// remplacée est conservée (l'élève a pu la redimensionner) et reportée dans la spec.</summary>
        public static Word.InlineShape Place(Word.Application app, Word.InlineShape existing, CurveSpec spec, byte[] png, string altText)
        {
            var doc = existing != null ? existing.Range.Document : app.ActiveDocument;
            string tmp = Path.Combine(Path.GetTempPath(), "mathcursor-curve-" + spec.Id + ".png");
            File.WriteAllBytes(tmp, png);
            var undo = app.UndoRecord;
            undo.StartCustomRecord(existing == null ? Strings.CurveUndoInsert : Strings.CurveUndoEdit);
            try
            {
                Word.InlineShape shape;
                if (existing != null)
                {
                    float width = existing.Width;
                    int at = existing.Range.Start;
                    existing.Range.Delete();
                    shape = doc.InlineShapes.AddPicture(tmp, false, true, doc.Range(at, at));
                    shape.LockAspectRatio = Office.MsoTriState.msoTrue;
                    shape.Width = width;
                    spec.WidthCm = Math.Round(width / PointsPerCm, 2);
                }
                else
                {
                    var p = app.Selection.Paragraphs[1].Range;
                    int at;
                    if (string.IsNullOrWhiteSpace((p.Text ?? "").Replace("\r", "").Replace("\a", "")) && p.InlineShapes.Count == 0)
                        at = p.Start;                       // paragraphe vide : l'image s'y pose
                    else
                    {
                        at = p.End;                         // sinon : nouveau paragraphe dessous
                        p.InsertParagraphAfter();
                    }
                    shape = doc.InlineShapes.AddPicture(tmp, false, true, doc.Range(at, at));
                    shape.LockAspectRatio = Office.MsoTriState.msoTrue;
                    shape.Width = (float)spec.WidthCm * PointsPerCm;
                    shape.Range.ParagraphFormat.Alignment = Word.WdParagraphAlignment.wdAlignParagraphCenter;
                    PlaceCaretAfter(doc, app.Selection, shape);
                }
                shape.Title = CurveSpec.TitlePrefix + spec.Id;
                shape.AlternativeText = altText;
                return shape;
            }
            finally
            {
                undo.EndCustomRecord();
                try { File.Delete(tmp); } catch { }
            }
        }

        // Curseur au début du paragraphe qui suit l'image, pour continuer à écrire
        // (créé, aligné à gauche, si l'image est en fin de document ou de cellule).
        internal static void PlaceCaretAfter(Word.Document doc, Word.Selection sel, Word.InlineShape shape)
        {
            try
            {
                var para = shape.Range.Paragraphs[1].Range;
                var next = para.Next(Word.WdUnits.wdParagraph, 1);
                // dans un tableau, le paragraphe suivant doit rester dans la même cellule
                int limit = (bool)para.Information[Word.WdInformation.wdWithInTable] ? para.Cells[1].Range.End : doc.Content.End;
                if (next == null || next.Start >= limit || next.InlineShapes.Count > 0)
                {
                    para.InsertParagraphAfter();
                    next = para.Next(Word.WdUnits.wdParagraph, 1);
                    next.ParagraphFormat.Alignment = Word.WdParagraphAlignment.wdAlignParagraphLeft;
                }
                sel.SetRange(next.Start, next.Start);
                int real = sel.Start;                       // readback : Word recale les positions
                sel.SetRange(real, real);
            }
            catch { /* placement du curseur : non critique */ }
        }
    }
}
