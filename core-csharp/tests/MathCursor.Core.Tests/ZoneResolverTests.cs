using MathCursor.Core;
using MathCursor.Core.Lattice;
using Xunit;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Tests du ZoneResolver — point d'entrée unique pour la résolution de
    /// zone. Vérifie : passthrough sans préférence, calcul de IsIncomplete
    /// (Hole + opérateur final), reset des prefs.
    ///
    /// <para>Note P6 (2026-05-21) : les tests <c>Resolve_V_*</c> sur le
    /// scanner legacy <c>VAsForallEAsExistsScanner</c> ont été retirés. Le
    /// comportement V→∀/√, E→∃ est désormais couvert par
    /// <c>ForallBelongsTemplateTests</c> dans le chantier Patterns.
    /// Les tests <c>Clear_resets_preferences</c> et <c>HasPreference</c>
    /// utilisent désormais <c>RuleTwoUppercase</c> comme exemple générique
    /// (rule encore branchée dans le pipeline).</para>
    /// </summary>
    public sealed class ZoneResolverTests
    {
        private static ZoneResolver MakeResolver()
            => new ZoneResolver(new LatticeEngine());

        // ---- Resolve sans préférence : passthrough ----

        [Fact]
        public void Resolve_empty_source_returns_empty()
        {
            var r = MakeResolver().Resolve("");
            Assert.Equal("", r.RawSource);
            Assert.Equal("", r.MutedSource);
            Assert.Equal("", r.TopLatex);
            Assert.Null(r.Spot);
            Assert.False(r.IsIncomplete);
        }

        [Fact]
        public void Resolve_simple_source_no_pref_passthrough()
        {
            // Sans préférence, MutedSource == RawSource
            var r = MakeResolver().Resolve("a+b");
            Assert.Equal("a+b", r.RawSource);
            Assert.Equal("a+b", r.MutedSource);
            Assert.Equal("a+b", r.TopLatex);
        }

        // ---- IsIncomplete ----

        [Fact]
        public void IsIncomplete_true_when_topLatex_has_square()
        {
            // somme k → \sum_{k=\square}^{\square} \square : 3 squares,
            // IsIncomplete=true (l'utilisateur attend de taper start/end/body)
            var r = MakeResolver().Resolve("somme k");
            Assert.True(r.IsIncomplete);
        }

        [Fact]
        public void IsIncomplete_true_when_trailing_operator()
        {
            // a+ → opérande b en attente, IsIncomplete=true
            var r = MakeResolver().Resolve("a+");
            Assert.True(r.IsIncomplete);
        }

        [Fact]
        public void IsIncomplete_true_when_trailing_equals()
        {
            // f(x) = → membre droit en attente
            var r = MakeResolver().Resolve("f(x) =");
            Assert.True(r.IsIncomplete);
        }

        [Fact]
        public void IsIncomplete_false_when_complete_formula()
        {
            // a+b = formule complète, pas de slot vacant, pas d'opérateur final
            var r = MakeResolver().Resolve("a+b");
            Assert.False(r.IsIncomplete);
        }

        [Fact]
        public void IsIncomplete_handles_trailing_whitespace()
        {
            // "a+ " → après trim whitespace, dernier char = +, IsIncomplete=true
            var r = MakeResolver().Resolve("a+ ");
            Assert.True(r.IsIncomplete);
        }

        // ---- Clear / HasPreference (utilise RuleTwoUppercase comme exemple) ----

        [Fact]
        public void Clear_resets_preferences()
        {
            // Vérifie le mécanisme générique de pref + reset. Utilise
            // RuleTwoUppercase (AB→vec/paren/bracket) comme exemple — rule
            // encore branchée dans le pipeline post-P6.
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleTwoUppercase, 0);
            Assert.True(resolver.HasPreference(AlternativeGenerator.RuleTwoUppercase));
            resolver.Clear();
            Assert.False(resolver.HasPreference(AlternativeGenerator.RuleTwoUppercase));
        }

        [Fact]
        public void Resolve_funcdef_with_space_before_colon()
        {
            // Régression : Word logs montrent top="" pour "f :x->x+1" alors
            // que RenderTop direct produit le bon rendu. Bug suspect dans
            // ZoneResolver.Resolve / le préprocesseur canonical sets ?
            var resolver = MakeResolver();
            var r = resolver.Resolve("f :x->x+1");
            Assert.Equal("f: x \\mapsto x+1", r.TopLatex);
        }

        [Fact]
        public void HasPreference_reflects_state()
        {
            // Exemple générique avec RuleTwoUppercase (vec/paren/bracket).
            var resolver = MakeResolver();
            Assert.False(resolver.HasPreference(AlternativeGenerator.RuleTwoUppercase));
            resolver.AddPreference(AlternativeGenerator.RuleTwoUppercase, 1);
            Assert.True(resolver.HasPreference(AlternativeGenerator.RuleTwoUppercase));
            resolver.Clear();
            Assert.False(resolver.HasPreference(AlternativeGenerator.RuleTwoUppercase));
        }
    }
}
