using System;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Merge mode 2 (revert) : si l'user vient de revert un OMath multi-ligne,
    /// absorbe TOUS les paragraphes de la zone reverted (cf. ADR 04-05
    /// multiline-edit-cascade). Self-guarding : retourne null si la zone
    /// reverted n'est pas active ou si le commit courant est hors d'elle.
    /// <para>
    /// Délègue pour ce sprint (squelette pipeline). Logique métier déplacera
    /// lors du nettoyage L4.
    /// </para>
    /// </summary>
    internal sealed class RevertedMultiLineMerger : IZoneMerger
    {
        private readonly Func<int, int, string, MergeResult> _impl;

        public RevertedMultiLineMerger(Func<int, int, string, MergeResult> impl)
        {
            _impl = impl ?? throw new ArgumentNullException(nameof(impl));
        }

        public string Name => "RevertedMultiLineMerger";

        public MergeResult TryMerge(int absStart, int absEnd, string currentSource)
            => _impl(absStart, absEnd, currentSource);
    }
}
