using System;
using System.Collections.Generic;
using System.IO;
using MathCursor.Host.CCMeta;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Hygiène de SUPPRESSION du pattern anchor CC (ADR
    /// 2026-06-10-Fix-anchor-cc-deletion-hygiene). Trois défenses, toutes
    /// LOCALES au caret (jamais de scan du document) :
    ///
    /// <list type="bullet">
    /// <item><b>H1</b> — Backspace juste après une de nos OMaths (resp.
    ///   Suppr juste avant son anchor) → sélectionne anchor + OMath comme
    ///   une UNITÉ ; la frappe suivante supprime le tout.</item>
    /// <item><b>H2</b> — anchor orpheline (OMath disparue) à proximité du
    ///   caret → supprimée avec son ZWSP.</item>
    /// <item><b>H3</b> — caret entré DANS une de nos CC (police cachée !)
    ///   → éjecté juste avant ; plus jamais de frappe invisible.</item>
    /// </list>
    /// </summary>
    internal sealed class AnchorHygiene
    {
        private readonly Word.Application _app;
        private readonly Action<string> _log;
        private bool _busy; // anti-réentrance : nos SetRange/Delete déclenchent SelectionChange

        public AnchorHygiene(Word.Application app, Action<string> log = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _log = log ?? LogDiag;
        }

        // ── H1 : suppression atomique ────────────────────────────────────

        /// <summary>Backspace : caret réduit juste APRÈS une de nos OMaths →
        /// sélectionne anchor+OMath (consomme la touche). Équation étrangère
        /// ou pas d'équation → false (Word gère).</summary>
        public bool TrySelectEquationBeforeCaret()
        {
            try
            {
                var doc = _app.ActiveDocument;
                var sel = _app.Selection;
                if (doc == null || sel == null || sel.Start != sel.End) return false;

                var (om, _) = CcMetaResolver.ResolveBehindCaret(doc, sel);
                if (om == null) return false;
                var (cc, _) = CcMetaResolver.ResolveAt(om);
                if (cc == null) return false; // OMath étrangère : Word natif

                int start = cc.Range.Start, end = om.Range.End;
                _busy = true;
                try { sel.SetRange(start, end); }
                finally { _busy = false; }
                _log($"hygiene: backspace → équation sélectionnée [{start},{end})");
                return true;
            }
            catch { return false; }
        }

        /// <summary>Suppr : caret réduit juste AVANT une de nos anchors →
        /// sélectionne anchor+OMath (consomme la touche).</summary>
        public bool TrySelectEquationAfterCaret()
        {
            try
            {
                var doc = _app.ActiveDocument;
                var sel = _app.Selection;
                if (doc == null || sel == null || sel.Start != sel.End) return false;

                // Probe avant : la CC commence à caret (ou +1 wrapper).
                Word.ContentControl cc = null;
                for (int delta = 0; delta <= 1 && cc == null; delta++)
                {
                    int p = sel.Start + delta;
                    if (p + 1 > doc.Content.End) break;
                    try
                    {
                        var probe = doc.Range(p, p + 1).ParentContentControl;
                        if (probe != null && probe.Title == MCMetaJson.CcTitle) cc = probe;
                    }
                    catch { }
                }
                if (cc == null) return false;

                // L'OMath adjacente (sinon orpheline : sélection de la CC seule).
                int end = cc.Range.End;
                try
                {
                    foreach (Word.OMath o in doc.Range(cc.Range.End, Math.Min(doc.Content.End, cc.Range.End + 3)).OMaths)
                    { end = o.Range.End; break; }
                }
                catch { }

                int start = cc.Range.Start;
                _busy = true;
                try { sel.SetRange(start, end); }
                finally { _busy = false; }
                _log($"hygiene: suppr → équation sélectionnée [{start},{end})");
                return true;
            }
            catch { return false; }
        }

        // ── H2 + H3 : sur WindowSelectionChange ──────────────────────────

        public void OnSelectionChanged()
        {
            if (_busy) return;
            try
            {
                var doc = _app.ActiveDocument;
                var sel = _app.Selection;
                if (doc == null || sel == null) return;

                // H3 — caret DANS une de nos CC → éjection juste avant
                // (sinon la frappe hérite de Font.Hidden = texte invisible).
                Word.ContentControl inCc = null;
                try { inCc = sel.Range.ParentContentControl; } catch { }
                if (inCc != null && inCc.Title == MCMetaJson.CcTitle && sel.Start == sel.End)
                {
                    int target = Math.Max(0, inCc.Range.Start - 1);
                    _busy = true;
                    try { sel.SetRange(target, target); }
                    finally { _busy = false; }
                    _log("hygiene: caret éjecté de la CC anchor");
                }

                // H2 — anchors ORPHELINES à proximité (OMath disparue).
                int s = Math.Max(0, sel.Start - 4);
                int e = Math.Min(doc.Content.End, sel.Start + 4);
                if (e <= s) return;
                List<Word.ContentControl> orphans = null;
                foreach (Word.ContentControl cc in doc.Range(s, e).ContentControls)
                {
                    string title = null;
                    try { title = cc.Title; } catch { }
                    if (title != MCMetaJson.CcTitle) continue;
                    bool hasOm = false;
                    try
                    {
                        foreach (Word.OMath o in doc.Range(cc.Range.End, Math.Min(doc.Content.End, cc.Range.End + 3)).OMaths)
                        { hasOm = true; break; }
                    }
                    catch { }
                    if (!hasOm) (orphans ?? (orphans = new List<Word.ContentControl>())).Add(cc);
                }
                if (orphans == null) return;
                foreach (var cc in orphans)
                {
                    _busy = true;
                    try { cc.Delete(true); _log("hygiene: anchor orpheline supprimée"); }
                    catch (Exception ex) { _log("hygiene_orphan_delete_error: " + ex.Message); }
                    finally { _busy = false; }
                }
            }
            catch (Exception ex) { _log("hygiene_error: " + ex.Message); }
        }

        private static void LogDiag(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} hygiene {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
