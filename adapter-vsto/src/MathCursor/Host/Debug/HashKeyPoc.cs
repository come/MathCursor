using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using MathCursor.Host.SourceMap;
using Office = Microsoft.Office.Core;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Debug
{
    /// <summary>
    /// Sondes POC de l'expérience hash-source-map (ADR
    /// 2026-06-11-Feat-hash-source-map-no-cc) — gate G1-G6 AVANT toute
    /// bascule. Boutons ribbon groupe debug (à retirer après verdict).
    ///
    /// P1  InsertBaseline / InsertNoAnchor : même équation, pipeline actuel
    ///     (ZWSP+CC+Tag) vs pipeline nu — timings par étape au log (G4),
    ///     type Display/Inline loggé (G5). L'eqArr sans anchor = bouton
    ///     ChainEqArrPoc existant.
    /// P2  SnapshotKeys : K1/K2 de TOUTES les OMaths du doc, persistées dans
    ///     une CustomXMLPart dédiée (survit save/reopen) + timings
    ///     Range.Text vs WordOpenXML.
    /// P3  VerifyDrift : recompare les OMaths du doc aux snapshots — à
    ///     rejouer après chaque scénario (frappe ailleurs, caret dedans,
    ///     save/reopen, copy-paste) → verdict G1/G2.
    /// P4  Discrimination : insère les paires piégeuses (x² vs x₂, frac vs
    ///     linéaire…) et vérifie collisions K1 / départage K2 → G3.
    /// P5  PartRoundtrip : 100 entrées write/read, timing, record undo
    ///     intact (UndoRecordScope.Probe) → G6.
    ///
    /// Verdicts : log %APPDATA%\MathCursor\logs\mathcursor.log + StatusBar.
    /// </summary>
    internal static class HashKeyPoc
    {
        private const string SnapNs = "urn:mathcursor:poc-snapshots:v1";
        private static readonly XNamespace SX = SnapNs;

        // ── P1a — baseline : pipeline ACTUEL (ZWSP + CC + Tag) ─────────────
        public static void RunInsertBaseline(Word.Application app, Action<string> log = null)
        {
            log = log ?? LogDiag;
            var doc = app.ActiveDocument; var sel = app.Selection;
            if (doc == null || sel == null) { log("poc-hash P1a: pas de doc/sel"); return; }

            var sw = Stopwatch.StartNew();
            int s0 = sel.Start;
            sel.TypeText("g(x)=1/x");
            int s1 = sel.Start;
            long tType = sw.ElapsedMilliseconds;

            var inserter = new OMathInserter(app, log);
            using (new UndoRecordScope(app, "MathCursor : POC baseline"))
                inserter.Insert(s0, s1, "g(x)=\\frac{1}{x}", "g(x)=1/x");
            sw.Stop();
            log($"poc-hash P1a BASELINE: total={sw.ElapsedMilliseconds}ms (dont TypeText={tType}ms)");
            Status(app, $"POC P1a baseline : {sw.ElapsedMilliseconds} ms (log pour le détail)");
        }

        // ── P1b — variante NUE : ni ZWSP, ni CC, ni Tag ; Record() en map ──
        public static void RunInsertNoAnchor(Word.Application app, Action<string> log = null)
        {
            log = log ?? LogDiag;
            var doc = app.ActiveDocument; var sel = app.Selection;
            if (doc == null || sel == null) { log("poc-hash P1b: pas de doc/sel"); return; }

            var sw = Stopwatch.StartNew();
            int s0 = sel.Start;
            sel.TypeText("g(x)=1/x");
            int s1 = sel.Start;
            long tType = sw.ElapsedMilliseconds;

            using (new UndoRecordScope(app, "MathCursor : POC no-anchor"))
            {
                // 1. Normalisation bornes (SetRange + readback).
                sel.SetRange(s0, s0); int internalStart = sel.Start;
                sel.SetRange(s1, s1); int internalEnd = sel.Start;
                long tNorm = sw.ElapsedMilliseconds;

                // 2. Cleanup structurel.
                int pos = ZoneCleaner.ClearZone(doc, internalStart, internalEnd, log);
                sel.SetRange(pos, pos);
                long tClear = sw.ElapsedMilliseconds;

                // 3. OMML chirurgical (placeholder 1-char), AUCUN ZWSP avant.
                XElement oMathEl;
                try { oMathEl = MathCursor.Serialization.LatexToOmml.Convert("g(x)=\\frac{1}{x}"); }
                catch (Exception ex) { log("poc-hash P1b: LatexToOmml KO: " + ex.Message); return; }
                Word.OMath om = InsertOmmlAt(doc, sel, oMathEl, pos, log);
                long tOmml = sw.ElapsedMilliseconds;
                if (om == null) { log("poc-hash P1b: OMath introuvable post-InsertXML"); return; }

                // 4. Typage Display/Inline — SANS ZWSP dans le ¶ (sonde G5 :
                //    la promotion display reposait-elle sur l'anchor ?).
                bool alone = ParagraphAloneWithOMath(om);
                if (alone)
                {
                    try { om.Type = Word.WdOMathType.wdOMathDisplay; }
                    catch (Exception exT) { log("poc-hash P1b: Type=Display KO: " + exT.Message); }
                }
                Word.WdOMathType finalType;
                try { finalType = om.Type; } catch { finalType = Word.WdOMathType.wdOMathInline; }
                long tTyping = sw.ElapsedMilliseconds;

                // 5. Échappement caret (clôt la saisie math).
                try
                {
                    sel.SetRange(om.Range.End, om.Range.End);
                    sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove);
                }
                catch { }
                long tCaret = sw.ElapsedMilliseconds;

                // 6. Record map (post-settle) — remplace CC.Add + Tag.
                var store = new SourceMapStore(log);
                var entry = store.Record(doc, om, "g(x)=1/x", "g(x)=\\frac{1}{x}", null,
                    "eq_" + Guid.NewGuid().ToString("N").Substring(0, 12));
                sw.Stop();

                log($"poc-hash P1b NO-ANCHOR: total={sw.ElapsedMilliseconds}ms — TypeText={tType} | "
                    + $"norm=+{tNorm - tType} | clear=+{tClear - tNorm} | omml=+{tOmml - tClear} | "
                    + $"typing=+{tTyping - tOmml} | caret=+{tCaret - tTyping} | record=+{sw.ElapsedMilliseconds - tCaret} "
                    + $"| type={finalType} (alone={alone}) | recordOk={entry != null}");
                Status(app, $"POC P1b no-anchor : {sw.ElapsedMilliseconds} ms, type={finalType}");
            }
        }

        // ── P2 — snapshot des clés de toutes les OMaths du doc ─────────────
        public static void RunSnapshotKeys(Word.Application app, Action<string> log = null)
        {
            log = log ?? LogDiag;
            var doc = app.ActiveDocument;
            if (doc == null) { log("poc-hash P2: pas de doc"); return; }

            var root = new XElement(SX + "pocSnapshots");
            int idx = 0;
            long sumT1 = 0, sumT2 = 0;
            foreach (Word.OMath om in doc.OMaths)
            {
                idx++;
                var sw1 = Stopwatch.StartNew();
                string text; try { text = om.Range.Text ?? ""; } catch { text = ""; }
                string k1 = SourceMap.Sha1Helper.Compute(text);
                sw1.Stop(); sumT1 += sw1.ElapsedMilliseconds;

                var sw2 = Stopwatch.StartNew();
                string k2 = SourceMapStore.ComputeK2(om) ?? "";
                sw2.Stop(); sumT2 += sw2.ElapsedMilliseconds;

                string codes = string.Join(",", text.Take(24).Select(c => ((int)c).ToString("x4")));
                log($"poc-hash P2 snap #{idx}: text=\"{Preview(text)}\" codes=[{codes}] "
                    + $"k1={Short(k1)} ({sw1.ElapsedMilliseconds}ms) k2={Short(k2)} ({sw2.ElapsedMilliseconds}ms)");
                root.Add(new XElement(SX + "snap",
                    new XAttribute("idx", idx), new XAttribute("k1", k1), new XAttribute("k2", k2),
                    new XElement(SX + "text", text)));
            }
            try
            {
                foreach (Office.CustomXMLPart p in doc.CustomXMLParts.SelectByNamespace(SnapNs)) { p.Delete(); break; }
                doc.CustomXMLParts.Add(new XDocument(root).ToString(SaveOptions.DisableFormatting));
            }
            catch (Exception ex) { log("poc-hash P2: écriture part snapshots KO: " + ex.Message); }
            log($"poc-hash P2: {idx} OMath(s) snapshotée(s) — Σ K1={sumT1}ms, Σ K2={sumT2}ms");
            Status(app, $"POC P2 : {idx} snapshot(s) (K1 {sumT1} ms, K2 {sumT2} ms)");
        }

        // ── P3 — vérification de drift contre les snapshots persistés ──────
        public static void RunVerifyDrift(Word.Application app, Action<string> log = null)
        {
            log = log ?? LogDiag;
            var doc = app.ActiveDocument;
            if (doc == null) { log("poc-hash P3: pas de doc"); return; }

            XDocument snaps = null;
            try
            {
                foreach (Office.CustomXMLPart p in doc.CustomXMLParts.SelectByNamespace(SnapNs))
                { snaps = XDocument.Parse(p.XML); break; }
            }
            catch (Exception ex) { log("poc-hash P3: lecture snapshots KO: " + ex.Message); }
            if (snaps == null) { log("poc-hash P3: AUCUN snapshot — jouer P2 d'abord"); Status(app, "POC P3 : pas de snapshot (P2 d'abord)"); return; }

            var current = new List<(string k1, string k2)>();
            foreach (Word.OMath om in doc.OMaths)
                current.Add((SourceMapStore.ComputeK1(om), SourceMapStore.ComputeK2(om) ?? ""));

            int okBoth = 0, driftK1 = 0, driftK2 = 0, total = 0;
            foreach (var snap in snaps.Root.Elements(SX + "snap"))
            {
                total++;
                string k1 = (string)snap.Attribute("k1"), k2 = (string)snap.Attribute("k2");
                string text = (string)snap.Element(SX + "text") ?? "";
                bool f1 = current.Any(c => c.k1 == k1);
                bool f2 = current.Any(c => c.k2 == k2);
                if (f1 && f2) okBoth++;
                if (!f1) driftK1++;
                if (!f2) driftK2++;
                log($"poc-hash P3 snap idx={(string)snap.Attribute("idx")} \"{Preview(text)}\" : "
                    + $"K1 {(f1 ? "OK" : "DRIFT ✗")} | K2 {(f2 ? "OK" : "DRIFT ✗")}");
            }
            string verdict = $"P3: {okBoth}/{total} stables — driftK1={driftK1} (G2) driftK2={driftK2} (G1)";
            log("poc-hash " + verdict);
            Status(app, "POC " + verdict);
        }

        // ── P4 — pouvoir discriminant : paires piégeuses ────────────────────
        public static void RunDiscrimination(Word.Application app, Action<string> log = null)
        {
            log = log ?? LogDiag;
            var doc = app.ActiveDocument; var sel = app.Selection;
            if (doc == null || sel == null) { log("poc-hash P4: pas de doc/sel"); return; }

            var pairs = new[]
            {
                ("x^{2}", "x_{2}"),
                ("\\frac{1}{2}", "1/2"),
                ("\\vec{u}", "\\bar{u}"),
                ("x^{2n}", "x^{2}n"),
            };
            int k1Collisions = 0, k2Collisions = 0, pairsOk = 0;
            using (new UndoRecordScope(app, "MathCursor : POC discrimination"))
            {
                foreach (var (la, lb) in pairs)
                {
                    var omA = InsertLatexOnFreshParagraph(doc, sel, la, log);
                    var omB = InsertLatexOnFreshParagraph(doc, sel, lb, log);
                    if (omA == null || omB == null) { log($"poc-hash P4: paire ({la} | {lb}) insertion KO"); continue; }
                    string k1a = SourceMapStore.ComputeK1(omA), k1b = SourceMapStore.ComputeK1(omB);
                    string k2a = SourceMapStore.ComputeK2(omA) ?? "?", k2b = SourceMapStore.ComputeK2(omB) ?? "??";
                    bool c1 = k1a == k1b, c2 = k2a == k2b;
                    if (c1) k1Collisions++;
                    if (c2) k2Collisions++; else pairsOk++;
                    log($"poc-hash P4 ({la} | {lb}) : K1 {(c1 ? "COLLISION" : "distincts")} | "
                        + $"K2 {(c2 ? "COLLISION ✗✗" : "départage OK")}");
                }
            }
            string verdict = $"P4: K2 départage {pairsOk}/{pairs.Length} (G3 exige 100 %) — collisions K1={k1Collisions} (info), K2={k2Collisions} (doit être 0)";
            log("poc-hash " + verdict);
            Status(app, "POC " + verdict);
        }

        // ── P5 — roundtrip CustomXMLPart + record undo ──────────────────────
        public static void RunPartRoundtrip(Word.Application app, Action<string> log = null)
        {
            log = log ?? LogDiag;
            var doc = app.ActiveDocument;
            if (doc == null) { log("poc-hash P5: pas de doc"); return; }

            var entries = new List<EquationSource>();
            for (int i = 0; i < 100; i++)
                entries.Add(new EquationSource
                {
                    K1 = "k1-" + i, K2 = "k2-" + i,
                    Steno = "steno avec accents é·" + i, Latex = "\\frac{" + i + "}{x}",
                    HandleId = "eq_poc" + i, Version = "poc", ParsedAt = DateTime.UtcNow,
                });

            var store = new SourceMapStore(log);
            using (new UndoRecordScope(app, "MathCursor : POC part roundtrip"))
            {
                UndoRecordScope.Probe(app, "P5 avant write part");
                var sw = Stopwatch.StartNew();
                store.Save(doc, entries);
                sw.Stop();
                UndoRecordScope.Probe(app, "P5 après write part");   // G6 : record toujours ouvert ?

                var sw2 = Stopwatch.StartNew();
                var back = store.Load(doc);
                sw2.Stop();
                log($"poc-hash P5: write 100 entrées={sw.ElapsedMilliseconds}ms (G6 ≤10ms), "
                    + $"read={sw2.ElapsedMilliseconds}ms, relues={back.Count}/100, doc.Saved={SafeSaved(doc)}");
                Status(app, $"POC P5 : write {sw.ElapsedMilliseconds} ms, read {sw2.ElapsedMilliseconds} ms, {back.Count}/100 — Probe au log pour le record undo");
            }
        }

        // ── primitives partagées ────────────────────────────────────────────

        /// <summary>Insertion OMML chirurgicale par placeholder 1-char à
        /// <paramref name="pos"/> — même technique que OMathInserter mais
        /// SANS ZWSP/CC (cœur de l'expérience).</summary>
        private static Word.OMath InsertOmmlAt(Word.Document doc, Word.Selection sel,
            XElement oMathEl, int pos, Action<string> log)
        {
            var w = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
            sel.SetRange(pos, pos);
            int phStart = sel.Start;
            sel.TypeText("□");
            int phEnd = sel.Start;
            var phRange = doc.Range(phStart, phEnd);

            XDocument xdoc;
            try { xdoc = XDocument.Parse(phRange.WordOpenXML); }
            catch (Exception ex) { log("poc-hash insert: parse KO: " + ex.Message); SafeDelete(doc, phStart, phEnd); return null; }

            XElement phRun = null;
            foreach (var r in xdoc.Descendants(w + "r"))
            {
                var t = r.Element(w + "t");
                if (t != null && t.Value == "□") { phRun = r; break; }
            }
            if (phRun == null) { log("poc-hash insert: placeholder introuvable"); SafeDelete(doc, phStart, phEnd); return null; }
            phRun.ReplaceWith(oMathEl);

            try { phRange.InsertXML(xdoc.ToString(SaveOptions.DisableFormatting)); }
            catch (Exception ex) { log("poc-hash insert: InsertXML KO: " + ex.Message); SafeDelete(doc, phStart, phEnd); return null; }

            try
            {
                int probeEnd = Math.Min(doc.Content.End, phStart + 200);
                foreach (Word.OMath o in doc.Range(phStart, probeEnd).OMaths) return o;
            }
            catch (Exception ex) { log("poc-hash insert: probe KO: " + ex.Message); }
            return null;
        }

        private static Word.OMath InsertLatexOnFreshParagraph(Word.Document doc, Word.Selection sel,
            string latex, Action<string> log)
        {
            XElement el;
            try { el = MathCursor.Serialization.LatexToOmml.Convert(latex); }
            catch (Exception ex) { log($"poc-hash: LatexToOmml(\"{latex}\") KO: " + ex.Message); return null; }
            try { sel.SetRange(doc.Content.End - 1, doc.Content.End - 1); sel.TypeParagraph(); }
            catch { }
            var om = InsertOmmlAt(doc, sel, el, sel.Start, log);
            try
            {
                if (om != null)
                {
                    sel.SetRange(om.Range.End, om.Range.End);
                    sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove);
                }
            }
            catch { }
            return om;
        }

        /// <summary>OMath seule dans son ¶ (mêmes strips qu'OMathInserter,
        /// sans le ZWSP qui n'existe plus dans ce flux).</summary>
        private static bool ParagraphAloneWithOMath(Word.OMath om)
        {
            try
            {
                string paraText = om.Range.Paragraphs[1].Range.Text ?? "";
                string omText = om.Range.Text ?? "";
                string remaining = paraText.Replace(omText, "")
                    .Replace("\r", "").Replace("\n", "").Replace("\v", "")
                    .Replace("\a", "").Replace("\t", "").Replace("\f", "").Trim();
                return string.IsNullOrEmpty(remaining);
            }
            catch { return false; }
        }

        private static void SafeDelete(Word.Document doc, int start, int end)
        {
            try { doc.Range(start, end).Delete(); } catch { }
        }

        private static bool SafeSaved(Word.Document doc)
        {
            try { return doc.Saved; } catch { return false; }
        }

        private static void Status(Word.Application app, string msg)
        {
            try { app.StatusBar = msg; } catch { }
        }

        private static string Short(string hash) =>
            string.IsNullOrEmpty(hash) ? "<vide>" : hash.Substring(0, Math.Min(10, hash.Length));

        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "\\r").Replace("\n", "\\n");
            return s.Length > 40 ? s.Substring(0, 40) + "…" : s;
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
                    $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
