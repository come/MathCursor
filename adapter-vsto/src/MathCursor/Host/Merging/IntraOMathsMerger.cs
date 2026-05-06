using System;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Merge des OMaths adjacents même paragraphe (intra-merge). Ex : commit
    /// <c>AB+BC</c> à gauche + <c>= AC</c> committé maintenant → fusion en
    /// un OMath <c>AB+BC = AC</c>. Priorité max dans le pipeline (gagne
    /// toujours sur cross-merge si applicable).
    /// <para>
    /// Pour ce sprint, délègue à la méthode privée
    /// <c>SuggestionService.TryMergeWithAdjacentOMaths</c> via délégué injecté.
    /// La logique sera complètement déplacée ici lors du futur ADR de
    /// nettoyage L4.
    /// </para>
    /// </summary>
    internal sealed class IntraOMathsMerger : IZoneMerger
    {
        private readonly Func<int, int, string, MergeResult> _impl;

        public IntraOMathsMerger(Func<int, int, string, MergeResult> impl)
        {
            _impl = impl ?? throw new ArgumentNullException(nameof(impl));
        }

        public string Name => "IntraOMathsMerger";

        public MergeResult TryMerge(int absStart, int absEnd, string currentSource)
            => _impl(absStart, absEnd, currentSource);
    }
}
