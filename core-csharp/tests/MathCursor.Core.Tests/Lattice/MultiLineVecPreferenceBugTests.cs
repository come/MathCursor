using MathCursor.Core;
using MathCursor.Core.Lattice;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    /// <summary>
    /// Régression du bug user 06-05 (multi-ligne + désambiguïsation vecteur).
    ///
    /// Bug initial : <c>AddPreference(RuleTwoUppercase, 0)</c> (vec) +
    /// <c>Resolve("AB")</c> rendait <c>"AB"</c> au lieu de <c>"\vec{AB}"</c>,
    /// parce que les alts vec/paren/bracket de <c>RuleTwoUppercase</c>
    /// n'avaient pas de <see cref="SourceMutation"/> — <c>ApplyPreferences</c>
    /// court-circuitait sur <c>alt.Mutation == null</c>.
    ///
    /// Fix S1 (ADR <c>2026-05-13-Refactor-source-mutation-pins-sidecar</c>) :
    /// <c>ScanUppercaseSequences</c> scanne la source et émet les alts vec
    /// et paren avec une <see cref="SourceMutation"/>. <c>ApplyPreferences</c>
    /// les applique au fixpoint, le top render contient les <c>\vec{}</c>
    /// attendus.
    ///
    /// Ces tests verrouillent le comportement post-fix pour empêcher la
    /// régression. Cf. SuggestionService.TryCascadeAbsorbMarkerChain pour
    /// le chemin cross-merge.
    /// </summary>
    public sealed class MultiLineVecPreferenceBugTests
    {
        private static ZoneResolver MakeResolver()
            => new ZoneResolver(new LatticeEngine());

        [Fact(DisplayName = "Default sans pref : `AB+BC=CD` rend les paires telles quelles")]
        public void NoPreference_uppercase_pairs_render_default()
        {
            // Confirme que le default = pas de vec (= comportement attendu
            // sans choix utilisateur).
            var r = MakeResolver().Resolve("AB+BC=CD");
            Assert.DoesNotContain("\\vec", r.TopLatex);
            Assert.NotNull(r.Spot); // ambig est bien proposée
            Assert.Equal(AlternativeGenerator.RuleTwoUppercase, r.Spot!.RuleId);
        }

        [Fact(DisplayName = "Pref vec sur RuleTwoUppercase mute la source : `AB` → `\\vec{AB}`")]
        public void VecPreference_on_uppercase_pair_propagates_to_top()
        {
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleTwoUppercase, 0);

            var r = resolver.Resolve("AB");

            Assert.Equal("\\vec{AB}", r.TopLatex);
        }

        [Fact(
            DisplayName = "Cross-merge `AB+BC=CD\\n= CH + HD` conserve les vec après pref",
            Skip = "S1 fixe le single-line (vec sur AB seul + chaîne mono-ligne). " +
                   "Le cross-merge multi-ligne demande l'extension ApplyAllMutations " +
                   "(sous-livraison S2 de l'ADR Refactor-source-mutation-pins-sidecar).")]
        public void CrossMerge_multiline_keeps_vec_after_pref()
        {
            // Reproduit la phase 3 du scénario user :
            // SuggestionService a construit le mergedSource après le commit
            // ligne 2, et appelle Resolve avec les préférences accumulées.
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleTwoUppercase, 0);

            const string mergedSource = "AB+BC=CD\n= CH + HD";
            var r = resolver.Resolve(mergedSource);

            // align* avec \vec sur TOUTES les paires.
            Assert.Contains("\\begin{align*}", r.TopLatex);
            Assert.Contains("\\vec{AB}", r.TopLatex);
            Assert.Contains("\\vec{BC}", r.TopLatex);
            Assert.Contains("\\vec{CD}", r.TopLatex);
            Assert.Contains("\\vec{CH}", r.TopLatex);
            Assert.Contains("\\vec{HD}", r.TopLatex);
        }

        [Fact(DisplayName = "Sanity single-line : `AB+BC=CD` + pref vec → vec sur toutes les paires")]
        public void SingleLine_uppercase_chain_with_vec_pref()
        {
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleTwoUppercase, 0);

            var r = resolver.Resolve("AB+BC=CD");

            Assert.Contains("\\vec{AB}", r.TopLatex);
            Assert.Contains("\\vec{BC}", r.TopLatex);
            Assert.Contains("\\vec{CD}", r.TopLatex);
        }
    }
}
