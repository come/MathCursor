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
using System.IO;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// POC TEMPORAIRE — banc d'essai « groupage undo » (2026-06-10).
    ///
    /// Constat (sondes log) : <c>Range.InsertXML</c> FERME le custom record
    /// (<c>IsRecordingCustomRecord</c> passe à false) → un commit = 3-4
    /// entrées undo au lieu d'une. Le « 1 Ctrl+Z » de DocMath datait de
    /// l'ère TypeText+BuildUp (recette mai), cassé sans bruit par le
    /// passage à l'OMML chirurgical (ADR 2026-06-02).
    ///
    /// Chaque variante insère « avant [x=1/2] après » au caret, DANS un
    /// custom record, avec sondes à chaque étape. Protocole : cliquer le
    /// bouton, faire UN Ctrl+Z, vérifier que TOUT disparaît, lire le log.
    ///
    /// V1 — InsertXML direct dans le doc user (état actuel) : témoin négatif.
    /// V2 — TypeText UnicodeMath + OMaths.Add + BuildUp : témoin positif
    ///      (recette DocMath mai — re-parse, pas un candidat prod).
    /// V3 — Ghost doc invisible : InsertXML DANS LE FANTÔME (hors record,
    ///      avant), puis transplant FormattedText dans le doc user (édition
    ///      normale, groupable ?). Candidat prod n°1 : réutilise LatexToOmml.
    /// V4 — OMaths.Add + Functions.Add structurel (sans BuildUp ni
    ///      InsertXML). Candidat prod n°2 : demanderait un walker OMML→OM.
    ///
    /// À SUPPRIMER (avec les boutons ribbon) une fois la variante actée.
    /// </summary>
    internal static class UndoPoc
    {
        public static void Run(Word.Application app, int variant)
        {
            if (app?.ActiveDocument == null) return;
            Log($"=== POC V{variant} ===");
            try
            {
                switch (variant)
                {
                    case 1: RunV1InsertXml(app); break;
                    case 2: RunV2BuildUp(app); break;
                    case 3: RunV3GhostTransplant(app); break;
                    case 4: RunV4FunctionsAdd(app); break;
                }
                app.StatusBar = $"POC V{variant} inséré → UN Ctrl+Z doit TOUT effacer";
            }
            catch (Exception ex)
            {
                Log($"V{variant} EXCEPTION: {ex.Message}");
                app.StatusBar = $"POC V{variant} : erreur (voir log)";
            }
        }

        // ── V1 — InsertXML direct (témoin négatif) ───────────────────────

        private static void RunV1InsertXml(Word.Application app)
        {
            var doc = app.ActiveDocument;
            var sel = app.Selection;
            using (new UndoRecordScope(app, "POC V1 InsertXML"))
            {
                sel.TypeText("V1 avant ");
                UndoRecordScope.Probe(app, "V1 après TypeText avant");

                int phStart = sel.Start;
                sel.TypeText("□");
                int phEnd = sel.Start;
                var phRange = doc.Range(phStart, phEnd);
                var xdoc = System.Xml.Linq.XDocument.Parse(phRange.WordOpenXML);
                var w = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
                System.Xml.Linq.XElement phRun = null;
                foreach (var r in xdoc.Descendants(w + "r"))
                {
                    var t = r.Element(w + "t");
                    if (t != null && t.Value == "□") { phRun = r; break; }
                }
                if (phRun == null) { Log("V1: run placeholder introuvable"); return; }
                phRun.ReplaceWith(MathCursor.Serialization.LatexToOmml.Convert("x=\\frac{1}{2}"));
                phRange.InsertXML(xdoc.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
                UndoRecordScope.Probe(app, "V1 après InsertXML");

                MoveCaretAfterOMathNear(app, doc, phStart);
                sel.TypeText(" après");
                UndoRecordScope.Probe(app, "V1 après TypeText après");
            }
        }

        // ── V2 — TypeText + OMaths.Add + BuildUp (témoin positif) ────────

        private static void RunV2BuildUp(Word.Application app)
        {
            var doc = app.ActiveDocument;
            var sel = app.Selection;
            using (new UndoRecordScope(app, "POC V2 BuildUp"))
            {
                sel.TypeText("V2 avant ");
                UndoRecordScope.Probe(app, "V2 après TypeText avant");

                int mStart = sel.Start;
                sel.TypeText("x=1/2");
                int mEnd = sel.Start;
                var built = doc.OMaths.Add(doc.Range(mStart, mEnd));
                UndoRecordScope.Probe(app, "V2 après OMaths.Add");
                built.OMaths.BuildUp();
                UndoRecordScope.Probe(app, "V2 après BuildUp");

                MoveCaretAfterOMathNear(app, doc, mStart);
                sel.TypeText(" après");
                UndoRecordScope.Probe(app, "V2 après TypeText après");
            }
        }

        // ── V3 — Ghost doc + transplant FormattedText (candidat n°1) ─────

        private static void RunV3GhostTransplant(Word.Application app)
        {
            var doc = app.ActiveDocument;
            var sel = app.Selection;

            // Préparation HORS record : l'équation est construite dans un
            // doc fantôme invisible (l'InsertXML y pourrit l'undo du
            // FANTÔME, pas celui du doc user). Cf. ADR DocMath
            // 2026-05-13-Fix-ghost-doc-invisible pour le Visible:false à l'Add.
            Word.Document ghost = null;
            try
            {
                object missing = Type.Missing;
                object vis = false;
                ghost = app.Documents.Add(ref missing, ref missing, ref missing, ref vis);
                Log($"V3: ghost créé, ActiveDocument={(app.ActiveDocument == doc ? "user (ok)" : "GHOST (focus volé !)")}");

                ghost.Content.Text = "□";
                var phRange = ghost.Range(0, 1);
                var xdoc = System.Xml.Linq.XDocument.Parse(phRange.WordOpenXML);
                var w = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
                System.Xml.Linq.XElement phRun = null;
                foreach (var r in xdoc.Descendants(w + "r"))
                {
                    var t = r.Element(w + "t");
                    if (t != null && t.Value == "□") { phRun = r; break; }
                }
                if (phRun == null) { Log("V3: run placeholder introuvable dans le ghost"); return; }
                phRun.ReplaceWith(MathCursor.Serialization.LatexToOmml.Convert("x=\\frac{1}{2}"));
                phRange.InsertXML(xdoc.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
                if (ghost.Content.OMaths.Count == 0) { Log("V3: pas d'OMath dans le ghost après InsertXML"); return; }
                var ghostOm = ghost.Content.OMaths[1];
                Log($"V3: OMath prête dans le ghost [{ghostOm.Range.Start},{ghostOm.Range.End})");

                // Seul le doc USER est touché dans le record : 2 TypeText +
                // 1 affectation FormattedText (transplant).
                using (new UndoRecordScope(app, "POC V3 transplant"))
                {
                    sel.TypeText("V3 avant ");
                    UndoRecordScope.Probe(app, "V3 après TypeText avant");

                    int tStart = sel.Start;
                    sel.TypeText("¤");
                    int tEnd = sel.Start;
                    doc.Range(tStart, tEnd).FormattedText = ghostOm.Range.FormattedText;
                    UndoRecordScope.Probe(app, "V3 après FormattedText transplant");

                    MoveCaretAfterOMathNear(app, doc, tStart);
                    sel.TypeText(" après");
                    UndoRecordScope.Probe(app, "V3 après TypeText après");
                }
            }
            finally
            {
                if (ghost != null)
                {
                    object dontSave = Word.WdSaveOptions.wdDoNotSaveChanges;
                    object missing = Type.Missing;
                    try { ghost.Close(ref dontSave, ref missing, ref missing); }
                    catch (Exception exC) { Log("V3: ghost close error: " + exC.Message); }
                }
            }
        }

        // ── V4 — OMaths.Add + Functions.Add structurel (candidat n°2) ────

        private static void RunV4FunctionsAdd(Word.Application app)
        {
            var doc = app.ActiveDocument;
            var sel = app.Selection;
            using (new UndoRecordScope(app, "POC V4 Functions.Add"))
            {
                sel.TypeText("V4 avant ");
                UndoRecordScope.Probe(app, "V4 après TypeText avant");

                int p = sel.Start;
                var omRange = doc.OMaths.Add(doc.Range(p, p));
                var om = omRange.OMaths[1];
                UndoRecordScope.Probe(app, "V4 après OMaths.Add");

                // x= en texte math, puis fraction structurelle 1/2.
                om.Range.Text = "x=";
                var insertAt = doc.Range(om.Range.End, om.Range.End);
                var func = om.Functions.Add(insertAt, Word.WdOMathFunctionType.wdOMathFunctionFrac);
                func.Frac.Num.Range.Text = "1";
                func.Frac.Den.Range.Text = "2";
                UndoRecordScope.Probe(app, "V4 après Functions.Add frac");

                MoveCaretAfterOMathNear(app, doc, p);
                sel.TypeText(" après");
                UndoRecordScope.Probe(app, "V4 après TypeText après");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>Caret juste APRÈS la 1ʳᵉ OMath trouvée près de
        /// <paramref name="nearPos"/> + MoveRight pour clore la saisie math.</summary>
        private static void MoveCaretAfterOMathNear(Word.Application app, Word.Document doc, int nearPos)
        {
            try
            {
                Word.OMath om = null;
                int probeEnd = Math.Min(doc.Content.End, nearPos + 200);
                foreach (Word.OMath o in doc.Range(nearPos, probeEnd).OMaths) { om = o; break; }
                if (om == null) { Log("helper: OMath introuvable au re-probe"); return; }
                app.Selection.SetRange(om.Range.End, om.Range.End);
                app.Selection.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove);
            }
            catch (Exception ex) { Log("helper: " + ex.Message); }
        }

        private static void Log(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} poc-undo {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
