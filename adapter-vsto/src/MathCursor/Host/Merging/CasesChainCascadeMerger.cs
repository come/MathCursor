using System;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Cascade montante pour systèmes <c>{</c> (cases). Self-guarding :
    /// retourne null si current source ne commence pas par <c>{ </c>.
    /// Cf. ADR 05-05 cases-multiline-phase2 + brief 30-04 §3.4 (pas de mix
    /// avec align). Logique pure dans <c>CasesCascadeMerger</c> (helper).
    /// <para>
    /// Délègue pour ce sprint. Logique Word-side déplacera au nettoyage L4.
    /// </para>
    /// </summary>
    internal sealed class CasesChainCascadeMerger : IZoneMerger
    {
        private readonly Func<int, int, string, MergeResult> _impl;

        public CasesChainCascadeMerger(Func<int, int, string, MergeResult> impl)
        {
            _impl = impl ?? throw new ArgumentNullException(nameof(impl));
        }

        public string Name => "CasesChainCascadeMerger";

        public MergeResult TryMerge(int absStart, int absEnd, string currentSource)
            => _impl(absStart, absEnd, currentSource);
    }
}
