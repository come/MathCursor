using System.Collections.Generic;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Resolution;
using Xunit;

namespace MathCursor.Core.Tests.Resolution
{
    /// <summary>
    /// Tests RED de la future API <see cref="ZoneResolver.Resolve(string, ResolutionSidecar)"/>.
    /// Ces tests **doivent échouer** tant que le refactor sidecar n'est pas
    /// livré — actuellement la méthode throw NotImplementedException.
    ///
    /// Ils encodent le **contrat post-refactor** :
    /// <list type="number">
    ///   <item>SpanPin pour <c>RuleTwoUppercase</c> applique l'alt vec sur
    ///     le span précis dans le top LaTeX.</item>
    ///   <item>Cascade vec : 3 pins vec sur AB/BC/CD → top contient les 3
    ///     <c>\vec{}</c>.</item>
    ///   <item>ZoneVotes domine le ranking : ≥2 votes vec sur 2-uppercase →
    ///     un nouveau span sans pin tombe auto sur vec.</item>
    ///   <item>Multi-ligne avec sidecar fusionné : align* contient les vec
    ///     sur les 5 paires.</item>
    ///   <item>Sidecar Empty → comportement identique à Resolve(source) sans
    ///     sidecar (régression garantie pour les zones non touchées).</item>
    /// </list>
    /// </summary>
    public sealed class ZoneResolverWithSidecarTests
    {
        private static ZoneResolver MakeResolver()
            => new ZoneResolver(new LatticeEngine());

        // ─── 1. Contrat de base : SpanPin substitue le LaTeX ──────────

        [Fact(DisplayName = "RED : SpanPin vec sur AB → top contient \\vec{AB}")]
        public void SpanPin_vec_applies_to_AB()
        {
            var resolver = MakeResolver();
            var sidecar = new ResolutionSidecar(
                new[] { new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            var r = resolver.Resolve("AB", sidecar);

            Assert.Contains("\\vec{AB}", r.TopLatex);
        }

        // ─── 2. Cascade pins : 3 pins → 3 \vec ───────────────────────

        [Fact(DisplayName = "RED : 3 pins vec sur AB+BC=CD → top a les 3 \\vec")]
        public void Three_pins_render_three_vecs()
        {
            var resolver = MakeResolver();
            var sidecar = new ResolutionSidecar(
                new[]
                {
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 0),
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 3, 2, 0),
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 6, 2, 0),
                },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            var r = resolver.Resolve("AB+BC=CD", sidecar);

            Assert.Contains("\\vec{AB}", r.TopLatex);
            Assert.Contains("\\vec{BC}", r.TopLatex);
            Assert.Contains("\\vec{CD}", r.TopLatex);
        }

        // ─── 3. ZoneVotes boost le ranking ────────────────────────────

        [Fact(DisplayName = "Phase 2 : 3 votes vec → un nouveau span 2-uppercase tombe auto sur vec")]
        public void Zone_votes_boost_ranking_to_auto_resolve()
        {
            var resolver = MakeResolver();
            var votes = new Dictionary<string, IReadOnlyDictionary<int, int>>
            {
                [AlternativeGenerator.RuleTwoUppercase] = new Dictionary<int, int> { [0] = 3 },
            };
            var sidecar = new ResolutionSidecar(new SpanPin[0], votes);

            // Aucun pin sur "XY" mais 3 votes vec → boost domine, top = \vec{XY}
            var r = resolver.Resolve("XY", sidecar);

            Assert.Contains("\\vec{XY}", r.TopLatex);
            // Note Phase 2 MVP : on n'éteint pas le Spot (la popup peut
            // toujours s'ouvrir si l'user veut changer d'avis). Seul le
            // top render est ajusté. À reconsidérer si UX nécessite
            // « no popup » pour les spans auto-résolus.
        }

        // ─── 4. Bug 06-05 : multi-ligne cross-merge avec vec préservés ────

        [Fact(DisplayName = "RED : multi-line AB+BC=CD\\n= CH+HD avec sidecar fusionné → align* avec vec")]
        public void Multiline_with_merged_sidecar_keeps_vecs()
        {
            var resolver = MakeResolver();
            // Sidecar fusionné comme produit par SidecarMerger après cross-merge
            // (offsets recalibrés sur la mergedSource "AB+BC=CD\n= CH + HD")
            var sidecar = new ResolutionSidecar(
                new[]
                {
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 0),
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 3, 2, 0),
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 6, 2, 0),
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 11, 2, 0),
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 16, 2, 0),
                },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            var r = resolver.Resolve("AB+BC=CD\n= CH + HD", sidecar);

            Assert.Contains("\\begin{align*}", r.TopLatex);
            Assert.Contains("\\vec{AB}", r.TopLatex);
            Assert.Contains("\\vec{BC}", r.TopLatex);
            Assert.Contains("\\vec{CD}", r.TopLatex);
            Assert.Contains("\\vec{CH}", r.TopLatex);
            Assert.Contains("\\vec{HD}", r.TopLatex);
        }

        // ─── 5. Régression : sidecar empty == comportement actuel ─────

        [Fact(DisplayName = "RED : sidecar Empty → comportement identique à Resolve(source) classique")]
        public void Empty_sidecar_matches_classic_resolve()
        {
            var resolver = MakeResolver();
            var classic = resolver.Resolve("AB+BC=CD");
            var withEmpty = resolver.Resolve("AB+BC=CD", ResolutionSidecar.Empty);

            // Top render identique sans sidecar et avec sidecar vide
            Assert.Equal(classic.TopLatex, withEmpty.TopLatex);
            Assert.Equal(classic.RawSource, withEmpty.RawSource);
        }

        // ─── 6. Pins dans le mauvais offset → ignorés silencieusement ───

        // ─── Cascade par votes : 1 résolution propage aux voisins ────────

        [Fact(DisplayName = "AB + BC = AC : pin AC + 1 vote vec → AB et BC tombent auto sur vec")]
        public void Pin_on_AC_with_vote_propagates_to_AB_and_BC()
        {
            // Scénario : l'user tape `AB + BC = AC` puis désambigue AC en vec.
            // Au commit, la popup envoie au minimum un pin sur AC + un vote
            // vec sur RuleTwoUppercase. AB et BC, non pinés, doivent
            // bénéficier du boost et tomber aussi sur vec.
            var resolver = MakeResolver();
            const string source = "AB + BC = AC";
            int offsetAC = source.IndexOf("AC");

            var votes = new Dictionary<string, IReadOnlyDictionary<int, int>>
            {
                [AlternativeGenerator.RuleTwoUppercase] = new Dictionary<int, int> { [0] = 1 },
            };
            var sidecar = new ResolutionSidecar(
                new[] { new SpanPin(AlternativeGenerator.RuleTwoUppercase, offsetAC, 2, 0) },
                votes);

            var r = resolver.Resolve(source, sidecar);

            Assert.Contains("\\vec{AB}", r.TopLatex);
            Assert.Contains("\\vec{BC}", r.TopLatex);
            Assert.Contains("\\vec{AC}", r.TopLatex);
        }

        [Fact(DisplayName = "AB + BC = AC : 3 pins via cascade popup → 3 vec dans le top")]
        public void Three_pins_from_popup_cascade_render_three_vecs()
        {
            // Variante : la popup elle-même cascade en interne et envoie 3 pins
            // (cf. SuggestionPopupWindow ligne 474-481). Test que ce mode marche
            // aussi côté ZoneResolver indépendamment du boost.
            var resolver = MakeResolver();
            const string source = "AB + BC = AC";
            int oAB = source.IndexOf("AB");
            int oBC = source.IndexOf("BC");
            int oAC = source.IndexOf("AC");

            var sidecar = new ResolutionSidecar(
                new[]
                {
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, oAB, 2, 0),
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, oBC, 2, 0),
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, oAC, 2, 0),
                },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            var r = resolver.Resolve(source, sidecar);

            Assert.Contains("\\vec{AB}", r.TopLatex);
            Assert.Contains("\\vec{BC}", r.TopLatex);
            Assert.Contains("\\vec{AC}", r.TopLatex);
        }

        // ─── Intra-merge (single-paragraph, OMaths adjacents fusionnés) ────

        [Fact(DisplayName = "Bug 06-05 (intra-merge) : `AB+BC` + ` = AC` fusionnés sur la même ligne → 3 vec préservés")]
        public void IntraMerge_two_omaths_same_line_preserves_vec_via_merged_sidecar()
        {
            // Scénario user : commit ligne 1 = `AB+BC` avec vec → sidecar A
            // (pins AB+BC). User rajoute ` = AC`, vec sur AC → commit. Le
            // TryMergeWithAdjacentOMaths fusionne avec l'OMath gauche → mergedSource
            // = `AB+BC = AC`. Si le sidecar fusionné est calculé correctement
            // (offsets recalibrés sur la mergedSource), `_resolver.Resolve`
            // doit produire les 3 vec.
            var resolver = MakeResolver();
            const string mergedSource = "AB+BC = AC";

            // Sidecar fusionné comme produit par SidecarMerger côté
            // TryMergeWithAdjacentOMaths : pins ligne 1 (offsets 0, 3) +
            // pin ligne 2 (offset 8 dans la mergedSource).
            var sidecar = new ResolutionSidecar(
                new[]
                {
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 0),  // AB
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 3, 2, 0),  // BC
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 8, 2, 0),  // AC
                },
                new Dictionary<string, IReadOnlyDictionary<int, int>>
                {
                    [AlternativeGenerator.RuleTwoUppercase] = new Dictionary<int, int> { [0] = 3 },
                });

            var r = resolver.Resolve(mergedSource, sidecar);

            Assert.Contains("\\vec{AB}", r.TopLatex);
            Assert.Contains("\\vec{BC}", r.TopLatex);
            Assert.Contains("\\vec{AC}", r.TopLatex);
        }

        [Fact(DisplayName = "Bug 06-05 (user) : `AB+AC` (vec) puis `= AD` (vec) → `\\vec{AD}` (pas `\\vec{DC}`)")]
        public void IntraMerge_AB_AC_then_eq_AD_renders_AD_not_DC()
        {
            // Reproduit le bug user : commit AB+AC vec, puis tape = AD vec
            // → l'image montre \vec{DC} à la place de \vec{AD}. Test ciblé
            // pour isoler : pipeline core ? offsets ? substitution ?
            var resolver = MakeResolver();
            const string mergedSource = "AB+AC = AD";

            var sidecar = new ResolutionSidecar(
                new[]
                {
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 0),  // AB
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 3, 2, 0),  // AC
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 8, 2, 0),  // AD
                },
                new Dictionary<string, IReadOnlyDictionary<int, int>>
                {
                    [AlternativeGenerator.RuleTwoUppercase] = new Dictionary<int, int> { [0] = 3 },
                });

            var r = resolver.Resolve(mergedSource, sidecar);

            Assert.Contains("\\vec{AB}", r.TopLatex);
            Assert.Contains("\\vec{AC}", r.TopLatex);
            Assert.Contains("\\vec{AD}", r.TopLatex);
            Assert.DoesNotContain("\\vec{DC}", r.TopLatex);
            Assert.DoesNotContain("DC", r.TopLatex);
        }

        [Fact(DisplayName = "HYPOTHÈSE 2 : source avec 2 occurrences AB → AllMatches en a 2 → Replace empile sur les 2")]
        public void Source_with_repeated_token_should_not_double_vec()
        {
            // Source avec deux occurrences "AB" (cas user qui peut arriver
            // par re-frappe ou par merge bizarre côté adapter).
            // Si baseResolved.AllMatches contient 2 entrées avec
            // DefaultLatex="AB", la boucle actuelle fait Replace 2 fois
            // → "AB+AB" → "\vec{AB}+\vec{AB}" au 1er passage, puis le 2e
            // passage refait Replace("AB", "\vec{AB}") sur la string déjà
            // transformée → "\vec{\vec{AB}}+\vec{\vec{AB}}". Empilement.
            var resolver = MakeResolver();
            var sidecar = new ResolutionSidecar(
                new[] { new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            var r = resolver.Resolve("AB+AB", sidecar);

            // Attendu : 1 niveau de \vec sur chaque AB, pas d'empilement.
            Assert.DoesNotContain("\\vec{\\vec{AB}}", r.TopLatex);
            // Vérifie que les 2 AB sont bien marqués (chaque occurrence en \vec).
            int vecAbCount = CountOccurrences(r.TopLatex, "\\vec{AB}");
            Assert.Equal(2, vecAbCount);
        }

        [Fact(DisplayName = "Last-write-wins : 2 pins divergents sur AB (vec puis paren) → paren gagne")]
        public void Two_divergent_pins_last_one_wins()
        {
            // Sémantique UX : si user désambigue AB en vec puis change d'avis
            // pour paren, c'est paren qui doit gagner. Le dernier pin chrono
            // exprime l'intention courante. Pas d'addition (sinon user devrait
            // re-cliquer plusieurs fois pour faire pencher la balance).
            //
            // Vérifie qu'on génère bien une seule alt à choisir parmi les 2 :
            // RuleTwoUppercase a vec (alt 0) en défaut, paren (alt 1) en alt.
            var resolver = MakeResolver();
            var sidecar = new ResolutionSidecar(
                new[]
                {
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 0), // AB → vec
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 1), // AB → paren (LAST)
                },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            var r = resolver.Resolve("AB", sidecar);

            // Le dernier pin (alt 1 = paren) doit gagner.
            Assert.DoesNotContain("\\vec{AB}", r.TopLatex);
            // Note : on ne vérifie pas le LaTeX exact de l'alt paren ici
            // (peut être `(AB)` ou similaire selon AlternativeGenerator) —
            // l'invariant testé est juste : pas de \vec.
        }

        [Fact(DisplayName = "HYPOTHÈSE bug user : pins AB dupliqués (× 3) → flèches empilées \\vec{\\vec{\\vec{AB}}}")]
        public void Duplicate_pins_cause_stacked_vec_substitutions()
        {
            // Simule une fusion de sidecars où le popup `_sessionSpanPins`
            // n'a pas été reset entre commits → pins AB dupliqués dans le
            // sidecar fusionné. L'image user montre 3 flèches superposées
            // sur AB. Si l'hypothèse est correcte, ce test reproduit le
            // bug : 3 pins AB → topLatex.Replace appelé 3× → \vec{\vec{\vec{AB}}}.
            //
            // Si le test échoue (top contient un seul \vec{AB}), alors la
            // cause est ailleurs et il faut chercher autre part.
            var resolver = MakeResolver();
            const string source = "AB+AC = AD";
            var sidecar = new ResolutionSidecar(
                new[]
                {
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 0),  // AB pin 1
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 0),  // AB pin 2 (DOUBLON)
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 0),  // AB pin 3 (DOUBLON)
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 3, 2, 0),  // AC
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 8, 2, 0),  // AD
                },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            var r = resolver.Resolve(source, sidecar);

            // Si l'hypothèse est correcte, on observe \vec{\vec{\vec{AB}}}
            // dans r.TopLatex. Le test échouera — ce qui prouve l'hypothèse.
            // Le fix attendu : Resolve(source, sidecar) doit être idempotent
            // (déduplique par defaultLatex unique avant de Replace).
            int abVecCount = CountOccurrences(r.TopLatex, "\\vec{AB}");
            Assert.Equal(1, abVecCount);
            Assert.DoesNotContain("\\vec{\\vec{AB}}", r.TopLatex);
            Assert.DoesNotContain("\\vec{\\vec{\\vec{AB}}}", r.TopLatex);
        }

        private static int CountOccurrences(string source, string needle)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(needle)) return 0;
            int count = 0, idx = 0;
            while ((idx = source.IndexOf(needle, idx, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }

        // ─── Stale pin (edge case) ─────────────────────────────────────

        [Fact(DisplayName = "Stale pin (offset hors range) → fallback default sans crash")]
        public void Stale_pin_at_wrong_offset_falls_back_silently()
        {
            var resolver = MakeResolver();
            var sidecar = new ResolutionSidecar(
                new[] { new SpanPin(AlternativeGenerator.RuleTwoUppercase, 99, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            // Source modifiée par l'user → l'ancien pin offset 99 ne matche
            // plus aucun span. Doit pas planter, doit fallback default.
            var r = resolver.Resolve("AB", sidecar);
            Assert.NotNull(r.TopLatex);
            // Default = "AB" sans vec puisque le pin ne matche pas un span existant.
            Assert.Equal("AB", r.TopLatex);
        }
    }
}
