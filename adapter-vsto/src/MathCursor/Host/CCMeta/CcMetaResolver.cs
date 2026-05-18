using System;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.CCMeta
{
    /// <summary>
    /// Résolveur central : à partir d'une OMath ou d'un caret, remonte au
    /// <see cref="Word.ContentControl"/> MathCursor parent (probe O(1) via
    /// <c>om.Range.ParentContentControl</c>) et parse son <c>Tag</c> en
    /// <see cref="MCMeta"/>.
    ///
    /// <para>Brief 2026-05-18 « probe minimale + backlink natif ». Remplace
    /// l'ancien <c>EquationBookmarkRegistry.FindHandleForOMath</c> qui
    /// scannait <c>doc.Bookmarks</c> à chaque appel (O(N)).</para>
    /// </summary>
    internal static class CcMetaResolver
    {
        /// <summary>
        /// Pour une OMath donnée, remonte au CC MathCursor et parse le Tag.
        /// Cascade :
        ///  1. <c>om.Range.ParentContentControl</c> — backlink natif principal
        ///  2. positions collapsed dans la range (omStart, omStart+1, omEnd-1, mid)
        ///     pour les cas display math où la CC est sub-anchor
        ///  3. <c>om.Range.ContentControls</c> — probe inverse pour OMath ⊃ CC
        /// Retourne le tuple <c>(cc, meta)</c>. <c>cc</c> null = pas notre OMath.
        /// <c>meta</c> null = tag corrompu / version inconnue → CC à nous mais sans
        /// méta exploitable (= traiter comme indication, pas vérité).
        /// </summary>
        public static (Word.ContentControl cc, MCMeta meta) ResolveAt(Word.OMath om)
        {
            if (om == null) return (null, null);

            Word.ContentControl cc = null;
            try { cc = om.Range.ParentContentControl; } catch { }

            if (cc == null)
            {
                // Fallback : positions collapsed dans la range OMath.
                int omStart = -1, omEnd = -1;
                try { omStart = om.Range.Start; omEnd = om.Range.End; } catch { return (null, null); }
                var doc = om.Range.Document;
                int mid = (omStart + omEnd) / 2;
                int[] probes = { omStart, omStart + 1, omEnd - 1, mid };
                foreach (var p in probes)
                {
                    if (p < 0) continue;
                    try
                    {
                        var c = doc.Range(p, p).ParentContentControl;
                        if (c != null) { cc = c; break; }
                    }
                    catch { }
                }
            }

            if (cc == null)
            {
                // Probe inverse : CCs DANS la range de l'OMath (display
                // math avec CC en sub-anchor → om.Range ⊃ cc.Range).
                try
                {
                    foreach (Word.ContentControl c in om.Range.ContentControls)
                    {
                        if (c.Title == MCMetaJson.CcTitle) { cc = c; break; }
                    }
                }
                catch { }
            }

            if (cc == null) return (null, null);
            if (cc.Title != MCMetaJson.CcTitle) return (null, null);  // CC pas à nous

            MCMeta meta = null;
            try { meta = MCMetaJson.TryParse(cc.Tag); } catch { }
            return (cc, meta);
        }

        /// <summary>
        /// Vrai si l'OMath est wrappée dans un CC MathCursor (= « à nous »).
        /// Helper rapide quand on veut juste l'identification, sans le Tag.
        /// </summary>
        public static bool IsOurs(Word.OMath om)
        {
            try { return om?.Range?.ParentContentControl?.Title == MCMetaJson.CcTitle; }
            catch { return false; }
        }

        /// <summary>
        /// Probe locale : OMath collée juste avant le caret (brief §1).
        /// Retourne <c>(om, meta)</c> ou <c>(null, null)</c> si le caret n'est
        /// pas derrière une OMath. Filtre <c>StoryType == wdMainTextStory</c>
        /// pour ignorer headers/footers/footnotes.
        /// </summary>
        public static (Word.OMath om, MCMeta meta) ResolveBehindCaret(Word.Document doc, Word.Selection sel)
        {
            if (doc == null || sel == null) return (null, null);
            try
            {
                if (sel.StoryType != Word.WdStoryType.wdMainTextStory) return (null, null);
                int caret = sel.Range.Start;
                if (caret <= 0) return (null, null);

                var probe = doc.Range(caret - 1, caret);
                if (probe.OMaths.Count == 0) return (null, null);

                Word.OMath om = null;
                foreach (Word.OMath o in probe.OMaths) { om = o; break; }
                if (om == null) return (null, null);

                // Garde : OMath collée pile derrière le caret (pas un
                // chevauchement lointain). Tolérance ±1 char pour absorber
                // d'éventuels wrappers Word.
                int omEnd = om.Range.End;
                if (omEnd != caret && omEnd != caret - 1) return (om, null);

                var (_, meta) = ResolveAt(om);
                return (om, meta);
            }
            catch { return (null, null); }
        }
    }
}
