using System;
using MathCursor.Host.CCMeta;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Helper partagé par les cross-mergers ascendants (
    /// <see cref="MarkerChainCascadeMerger"/>, <see cref="CasesChainCascadeMerger"/>).
    /// Sait trouver un OMath à NOUS en fin de paragraphe — opération
    /// commune aux 2 cascades pour identifier le sommet absorbable.
    ///
    /// <para>Phase B (2026-05-18) : identification via CC MathCursor +
    /// cc.Tag MCMeta au lieu de bookmark <c>mcEq_*</c> + IEquationStore.
    /// Scoped scan : itère <c>doc.Range(paraStart, paraContentEnd).OMaths</c>
    /// au lieu de <c>doc.OMaths</c> global.</para>
    /// </summary>
    internal sealed class ParagraphCascadeProbe
    {
        private readonly Action<string> _log;

        public ParagraphCascadeProbe(Action<string> log)
        {
            _log = log ?? (s => { });
        }

        /// <summary>
        /// Cherche un OMath à nous (wrappé dans un CC MathCursor) qui termine
        /// le paragraphe [<paramref name="paraStart"/>, <paramref name="paraContentEnd"/>]
        /// (ce qui suit l'OMath jusqu'au ¶ mark doit être whitespace).
        /// Retourne <c>(omStart, source, handle)</c> si trouvé, <c>null</c> sinon.
        /// </summary>
        public (int omStart, string source, string handle)? FindOwnedAtEnd(
            Word.Document doc, int paraStart, int paraContentEnd)
        {
            try
            {
                foreach (Word.OMath om in doc.Range(paraStart, paraContentEnd).OMaths)
                {
                    var rng = om.Range;
                    if (rng.Start < paraStart || rng.End > paraContentEnd) continue;
                    if (rng.End < paraContentEnd)
                    {
                        string after = doc.Range(rng.End, paraContentEnd).Text ?? "";
                        if (after.Trim().Length > 0) continue;
                    }
                    var (_, meta) = CcMetaResolver.ResolveAt(om);
                    if (meta == null) continue;
                    if (string.IsNullOrEmpty(meta.HandleId) || string.IsNullOrEmpty(meta.Steno)) continue;
                    return (rng.Start, meta.Steno, meta.HandleId);
                }
            }
            catch (Exception ex) { _log("cascade_owned_omath_scan_error: " + ex.Message); }
            return null;
        }
    }
}
