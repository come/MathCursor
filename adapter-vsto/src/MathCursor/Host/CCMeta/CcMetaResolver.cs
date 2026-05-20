using System;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.CCMeta
{
    /// <summary>
    /// Résolveur central : à partir d'une OMath ou d'un caret, remonte au
    /// <see cref="Word.ContentControl"/> MathCursor associé et parse son
    /// <c>Tag</c> en <see cref="MCMeta"/>.
    ///
    /// <para>Pattern « anchor CC » (ADR 2026-05-19) : la CC est un wrapper
    /// tiny (1 char ZWSP hidden) qui vit JUSTE AVANT l'OMath, pas autour.
    /// Le backlink se fait via <b>backward probe</b> : on cherche un
    /// <c>ParentContentControl</c> en sondant les positions juste avant
    /// <c>om.Range.Start</c>.</para>
    ///
    /// <para>Pourquoi ce pattern : le wrap autour de l'OMath déclenchait
    /// l'ajout de <c>&lt;w:br/&gt;</c> en mode display, et la sticky-zone
    /// causait des auto-grow non désirés. Le CC adjacent évite ces deux
    /// problèmes en gardant l'OMath "naked".</para>
    /// </summary>
    internal static class CcMetaResolver
    {
        /// <summary>Distance max (en positions Word internes) entre <c>om.Range.Start</c>
        /// et l'anchor CC. Empiriquement, 3 suffit (= 1 ZWSP + structural markers).</summary>
        private const int AnchorProbeMaxDelta = 3;

        /// <summary>
        /// Pour une OMath donnée, retrouve son anchor CC MathCursor par
        /// backward probe et parse le Tag. Cascade :
        ///  1. Backward probe (positions -1 à -3 avant <c>om.Range.Start</c>)
        ///  2. <c>om.Range.ParentContentControl</c> — fallback rétrocompat
        ///     pour les OMaths créées avec l'ancien pattern wrap (CC autour)
        ///  3. Positions collapsed dans la range OMath (legacy)
        ///
        /// Retourne le tuple <c>(cc, meta)</c>. <c>cc</c> null = pas notre OMath.
        /// <c>meta</c> null = tag corrompu / version inconnue → CC à nous mais sans
        /// méta exploitable (= traiter comme indication, pas vérité).
        /// </summary>
        public static (Word.ContentControl cc, MCMeta meta) ResolveAt(Word.OMath om)
        {
            if (om == null) return (null, null);

            int omStart;
            try { omStart = om.Range.Start; } catch { return (null, null); }
            var doc = om.Range.Document;
            if (doc == null) return (null, null);

            Word.ContentControl cc = null;

            // 1. Backward probe : nouveau pattern anchor (ADR 2026-05-19).
            for (int delta = 1; delta <= AnchorProbeMaxDelta && cc == null; delta++)
            {
                int p = omStart - delta;
                if (p < 0) break;
                try
                {
                    var probe = doc.Range(p, p + 1).ParentContentControl;
                    if (probe != null && probe.Title == MCMetaJson.CcTitle)
                        cc = probe;
                }
                catch { }
            }

            // 2. Fallback rétrocompat : pattern legacy wrap.
            if (cc == null)
            {
                try
                {
                    var direct = om.Range.ParentContentControl;
                    if (direct != null && direct.Title == MCMetaJson.CcTitle)
                        cc = direct;
                }
                catch { }
            }

            // 3. Fallback ultime : probe inverse sur la range de l'OMath
            //    (= legacy display math avec CC sub-anchor).
            if (cc == null)
            {
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
        /// Vrai si l'OMath a un anchor CC MathCursor à proximité (= « à nous »).
        /// Backward probe identique à <see cref="ResolveAt"/> mais sans parser le Tag.
        /// </summary>
        public static bool IsOurs(Word.OMath om)
        {
            if (om == null) return false;
            int omStart;
            try { omStart = om.Range.Start; } catch { return false; }
            var doc = om.Range.Document;
            if (doc == null) return false;
            for (int delta = 1; delta <= AnchorProbeMaxDelta; delta++)
            {
                int p = omStart - delta;
                if (p < 0) break;
                try
                {
                    var probe = doc.Range(p, p + 1).ParentContentControl;
                    if (probe != null && probe.Title == MCMetaJson.CcTitle) return true;
                }
                catch { }
            }
            // Fallback wrap-legacy.
            try { return om.Range.ParentContentControl?.Title == MCMetaJson.CcTitle; }
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
