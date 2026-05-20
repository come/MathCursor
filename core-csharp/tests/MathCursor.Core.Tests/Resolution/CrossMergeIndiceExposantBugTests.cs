using System.Linq;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Resolution;
using Xunit;

namespace MathCursor.Core.Tests.Resolution
{
    /// <summary>
    /// Bug 2026-05-07 reporté par l'utilisateur :
    ///   1. Tape "X2", popup → sélectionne X_2 (indice)
    ///   2. Tape " et Y2", popup → sélectionne Y² (alt-revert, retour exposant)
    ///   3. Commit
    ///   → "le merge mange X2" : visuellement X2 disparaît / colle au "et".
    ///
    /// Probe diagnostique (tests Probe_*) : on observe deux pathologies
    /// distinctes mais corrélées sur le rawSource mergé :
    ///   (a) Les espaces sont SYSTÉMATIQUEMENT mangés du TopLatex.
    ///       Ex. <c>"x2 et y2"</c> → <c>"x^{2}ety^{2}"</c> (X2 et et collés).
    ///   (b) Les ambig matches DISPARAISSENT dès qu'il y a du texte
    ///       non-opérateur entre 2 ambigs ("et", " ", lettres). Sans
    ///       matches, RulePin/SpanOverride ne peuvent plus s'appliquer →
    ///       X2 perd son indice et redevient l'exposant default. Effet
    ///       visible : le commit a "mangé" la résolution antérieure.
    ///
    /// Les deux ensemble expliquent le ressenti user "merge mange X2".
    /// </summary>
    public class CrossMergeIndiceExposantBugTests
    {
        private static ZoneResolver MakeResolver()
            => new ZoneResolver(LatticeEngine.LoadEmbedded("fr"));

        // ─── Bug (a) : spaces mangés dans le TopLatex ─────────────────

        [Fact]
        public void Spaces_must_survive_in_TopLatex()
        {
            var resolved = MakeResolver().Resolve("x2 et y2");
            // Bug observé : "x^{2}ety^{2}" (espaces mangés autour de "et").
            // Attendu : les 2 espaces présents dans le rawSource doivent
            // se retrouver dans le LaTeX final.
            Assert.Contains("x^{2} et y^{2}", resolved.TopLatex);
        }

        [Fact]
        public void Multiple_separator_kinds_should_all_preserve_spaces()
        {
            // Sanity : autres mots-glue qui doivent préserver les espaces.
            foreach (var glue in new[] { "et", "ou", "donc" })
            {
                var src = $"x2 {glue} y2";
                var resolved = MakeResolver().Resolve(src);
                Assert.True(
                    resolved.TopLatex.Contains($" {glue} "),
                    $"glue \"{glue}\" : espaces perdus dans \"{resolved.TopLatex}\"");
            }
        }

        // ─── Bug (b) : ambig matches perdus avec texte non-opérateur ──

        [Fact]
        public void Both_ambigs_must_be_detected_when_text_between()
        {
            var resolved = MakeResolver().Resolve("x2 et y2");
            var letterMatches = resolved.AllMatches
                .Where(m => m.Spot.RuleId == "letter-sup-number")
                .ToList();
            // Bug observé : 0 matches (alors que x2+y2 en donne 2).
            // Attendu : 2 matches (un pour chaque occurrence).
            Assert.Equal(2, letterMatches.Count);
        }

        // ─── Conséquence aval : le RulePin / SpanOverride ne s'applique plus ─

        [Fact]
        public void X2_indice_then_Y2_revert_merge_should_keep_X2()
        {
            // Sondage initial pour récupérer la signature du Y2 (= 2ème
            // letter-sup-number occurence dans le rawSource mergé).
            var probe = MakeResolver().Resolve("x2 et y2");
            var letterMatches = probe.AllMatches
                .Where(m => m.Spot.RuleId == "letter-sup-number")
                .OrderBy(m => m.Signature!.RawSourcePos)
                .ToList();
            // Pré-condition (= bug (b)) : 2 matches doivent exister.
            Assert.Equal(2, letterMatches.Count);
            var sigY2 = letterMatches[1].Signature!;
            Assert.Equal("y^{2}", sigY2.DefaultLatex);

            // Sidecar reproduisant l'état après les 2 commits :
            //  - RulePin letter-sup-number:0 (= indice) posé au commit du X2
            //  - SpanOverride revert sur Y2 posé au commit du Y² (alt-revert)
            var sidecar = new ResolutionSidecar(
                spanPins: null,
                zoneVotes: null,
                rulePins: new[] { new RulePin("letter-sup-number", 0) },
                spanOverrides: new[] { new SpanOverride(sigY2, SpanOverride.AltIdxRevert) });

            var resolved = MakeResolver().Resolve("x2 et y2", null, sidecar);

            // x2 doit être en indice, y2 doit rester en exposant (revert),
            // et l'espace " et " doit survivre.
            Assert.Contains("x_{2}", resolved.TopLatex);
            Assert.Contains("y^{2}", resolved.TopLatex);
            Assert.Contains(" et ", resolved.TopLatex);
        }
    }
}
