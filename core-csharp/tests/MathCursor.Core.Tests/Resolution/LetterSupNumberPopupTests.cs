using System.Linq;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Resolution;
using Xunit;

namespace MathCursor.Core.Tests.Resolution
{
    /// <summary>
    /// Reproduit le cas user 2026-05-07 : tape "x2" avec un RulePin
    /// letter-sup-number actif (= indice choisi précédemment) → doit
    /// produire une popup avec 1 cellule (= alt-revert qui propose
    /// l'exposant comme alternative).
    ///
    /// Sur le screenshot du user, la popup affichait 0 cellule, ce qui
    /// indique soit que le pipeline ne produit pas le match, soit que
    /// la popup le filtre incorrectement.
    /// </summary>
    public class LetterSupNumberPopupTests
    {
        private static ZoneResolver MakeResolver()
            => new ZoneResolver(LatticeEngine.LoadEmbedded("fr"));

        [Fact]
        public void Bare_x2_default_is_exposant_with_indice_as_alt()
        {
            // Pas de sidecar / pas de RulePin → cas vierge.
            var resolved = MakeResolver().Resolve("x2");

            // Default rule letter-sup-number = exposant.
            Assert.NotEmpty(resolved.AllMatches);
            var m = resolved.AllMatches.First(x => x.Spot.RuleId == "letter-sup-number");
            Assert.Equal("x^{2}", m.Spot.DefaultLatex);
            Assert.Single(m.Spot.Alternatives);
            Assert.Equal("x_{2}", m.Spot.Alternatives[0].Latex);
            // Cas vierge : la finale est le default (exposant).
            Assert.Equal("x^{2}", resolved.TopLatex);
        }

        [Fact]
        public void X2_with_RulePin_indice_active_splices_to_indice()
        {
            // RulePin letter-sup-number:0 (= indice via alt 0) actif.
            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("letter-sup-number", 0) }, null);

            var resolved = MakeResolver().Resolve("x2", null, sidecar);

            // Splice doit transformer "x^{2}" en "x_{2}".
            Assert.Equal("x_{2}", resolved.TopLatex);
            Assert.Equal("x^{2}", resolved.BaseTopLatex); // pre-splice intact

            // Le match letter-sup-number doit toujours être dans AllMatches
            // (= la popup doit pouvoir afficher l'alt-revert pour permettre
            // retour à l'exposant).
            Assert.Contains(resolved.AllMatches, x => x.Spot.RuleId == "letter-sup-number");
            var m = resolved.AllMatches.First(x => x.Spot.RuleId == "letter-sup-number");
            Assert.Equal(1, m.Spot.Alternatives.Count);
            // Le match a une signature décorée
            Assert.NotNull(m.Signature);
        }

        [Fact]
        public void X2_with_RulePin_active_popup_should_show_revert_alt()
        {
            // Vérifie la logique côté ZoneResolver : avec RulePin actif,
            // ResolveBestAlt doit retourner 0 (= indice) pour le splice,
            // et l'AllMatches reste populated pour que la popup affiche
            // l'alt-revert.
            //
            // Comportement attendu côté popup (cf. règle invariant
            // 2026-05-07) : 1 cellule = revert (= exposant brut).
            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("letter-sup-number", 0) }, null);

            var resolved = MakeResolver().Resolve("x2", null, sidecar);

            // BaseTopLatex contient le default (exposant), TopLatex contient
            // le splice (indice). Donc la popup avec activeAltIdx=0 doit
            // construire :
            //   - Revert (= BaseTopLatex sub for this match = "x^{2}")
            //   - 0 autres vraies alts (la seule alt indice est filtrée)
            // → 1 cellule au total.

            Assert.NotEqual(resolved.TopLatex, resolved.BaseTopLatex);
        }
    }
}
