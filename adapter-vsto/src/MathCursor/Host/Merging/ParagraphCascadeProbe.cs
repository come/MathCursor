using System;
using MathCursor.Host.Bookmarks;
using MathCursor.HostContract;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Helper partagé par les cross-mergers ascendants (
    /// <see cref="MarkerChainCascadeMerger"/>, <see cref="CasesChainCascadeMerger"/>).
    /// Sait trouver un OMath à NOUS en fin de paragraphe — opération
    /// commune aux 2 cascades pour identifier le sommet absorbable.
    /// </summary>
    internal sealed class ParagraphCascadeProbe
    {
        private readonly EquationBookmarkRegistry _bookmarks;
        private readonly IEquationStore _store;
        private readonly Action<string> _log;

        public ParagraphCascadeProbe(
            EquationBookmarkRegistry bookmarks,
            IEquationStore store,
            Action<string> log)
        {
            _bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _log = log ?? (s => { });
        }

        /// <summary>
        /// Cherche un OMath à nous (bookmark <c>mcEq_*</c>) qui termine le
        /// paragraphe [<paramref name="paraStart"/>, <paramref name="paraContentEnd"/>]
        /// (ce qui suit l'OMath jusqu'au ¶ mark doit être whitespace).
        /// Retourne <c>(omStart, source, handle)</c> si trouvé, <c>null</c> sinon.
        /// </summary>
        public (int omStart, string source, string handle)? FindOwnedAtEnd(
            Word.Document doc, int paraStart, int paraContentEnd)
        {
            try
            {
                foreach (Word.OMath om in doc.OMaths)
                {
                    var rng = om.Range;
                    if (rng.Start < paraStart || rng.End > paraContentEnd) continue;
                    if (rng.End < paraContentEnd)
                    {
                        string after = doc.Range(rng.End, paraContentEnd).Text ?? "";
                        if (after.Trim().Length > 0) continue;
                    }
                    string h = _bookmarks.FindHandleForOMath(om);
                    if (h == null) continue;
                    try
                    {
                        var stored = _store.RetrieveAsync(new EquationHandle(h)).GetAwaiter().GetResult();
                        if (stored != null && !string.IsNullOrEmpty(stored.Source))
                        {
                            return (rng.Start, stored.Source, h);
                        }
                    }
                    catch (Exception ex) { _log($"cascade_owned_omath_retrieve_error: {ex.Message}"); }
                }
            }
            catch (Exception ex) { _log("cascade_owned_omath_scan_error: " + ex.Message); }
            return null;
        }
    }
}
