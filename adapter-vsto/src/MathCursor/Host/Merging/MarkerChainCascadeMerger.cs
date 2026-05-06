using System;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Cascade montante pour blocs align* (markers <c>=</c>, <c>&lt;=&gt;</c>,
    /// <c>=&gt;</c>, <c>&lt;=</c>). Self-guarding : retourne null si current
    /// source ne commence pas par un marker align. Cf. ADR 04-05 +
    /// brief 30-04 §3.2.
    /// <para>
    /// Délègue pour ce sprint. Logique Word-side déplacera au nettoyage L4.
    /// </para>
    /// </summary>
    internal sealed class MarkerChainCascadeMerger : IZoneMerger
    {
        private readonly Func<int, int, string, MergeResult> _impl;

        public MarkerChainCascadeMerger(Func<int, int, string, MergeResult> impl)
        {
            _impl = impl ?? throw new ArgumentNullException(nameof(impl));
        }

        public string Name => "MarkerChainCascadeMerger";

        public MergeResult TryMerge(int absStart, int absEnd, string currentSource)
            => _impl(absStart, absEnd, currentSource);
    }
}
