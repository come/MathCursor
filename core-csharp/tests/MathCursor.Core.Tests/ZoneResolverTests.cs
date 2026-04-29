using MathCursor.Core;
using MathCursor.Core.Lattice;
using Xunit;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Tests du ZoneResolver — point d'entrée unique pour la résolution de
    /// zone. Vérifie : passthrough sans préférence, application récursive des
    /// prefs source-mutation, calcul de IsIncomplete (Hole + opérateur final),
    /// reset des prefs.
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

        [Fact]
        public void Resolve_V_alone_no_pref_yields_ambig_spot()
        {
            // V seul → ambig (V identity / ∀ / √)
            var r = MakeResolver().Resolve("V");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVAsForall, r.Spot!.RuleId);
            Assert.Equal(3, r.Spot.Alternatives.Count);
        }

        // ---- AddPreference : V → forall ----

        [Fact]
        public void Resolve_V_with_forall_pref_mutates_to_forall()
        {
            // L'utilisateur a déjà choisi ∀ (altIdx=1) pour V.
            // Décomposition modulaire (ADR 29-04) : forall seul rend "\forall ".
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleVAsForall, 1);
            var r = resolver.Resolve("V");
            Assert.Equal("V", r.RawSource);
            Assert.Equal("forall", r.MutedSource);
            Assert.Contains("\\forall", r.TopLatex);
        }

        [Fact]
        public void Resolve_V_x_dans_R_with_forall_pref_renders_full()
        {
            // V x dans R + pref forall → forall x dans R → "\forall x \in R"
            // Décomposition modulaire : `dans` keyword explicite pour le \in.
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleVAsForall, 1);
            var r = resolver.Resolve("V x dans R");
            Assert.Equal("forall x dans R", r.MutedSource);
            Assert.Equal("\\forall x \\in R", r.TopLatex);
        }

        [Fact]
        public void Resolve_V_with_identity_pref_no_mutation()
        {
            // Pref altIdx=0 = V identity (pas de mutation). MutedSource ==
            // RawSource. Le Spot ambig reste exposé (l'utilisateur peut
            // re-changer d'avis).
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleVAsForall, 0);
            var r = resolver.Resolve("V x R");
            Assert.Equal("V x R", r.MutedSource);
        }

        [Fact]
        public void Resolve_V_with_racine_pref_mutates_to_racine()
        {
            // Pref altIdx=2 = √ (mutation V→racine).
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleVAsForall, 2);
            var r = resolver.Resolve("V x");
            Assert.Equal("racine x", r.MutedSource);
            Assert.Contains("\\sqrt", r.TopLatex);
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

        [Fact]
        public void IsIncomplete_with_forall_pref_after_dans()
        {
            // V x dans (avec pref forall) → forall x dans → "\forall x \in "
            // Le \in n'est qu'un Const, donc IsIncomplete via Hole=false. Mais
            // on a un Const " \in " qui se termine par un espace : pas
            // d'opérateur final non plus → IsIncomplete=false.
            // Pour la décomposition modulaire (29-04), c'est l'utilisateur qui
            // décide quand sa formule est complète, le résolveur ne devine pas.
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleVAsForall, 1);
            var r = resolver.Resolve("V x dans");
            // Pas de Hole (\square), pas d'op trailing dans la source brute "V x dans"
            Assert.False(r.IsIncomplete);
        }

        // ---- Clear ----

        [Fact]
        public void Clear_resets_preferences()
        {
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleVAsForall, 1);
            resolver.Clear();
            var r = resolver.Resolve("V x R");
            // Sans pref, la mutation n'est plus appliquée
            Assert.Equal("V x R", r.MutedSource);
        }

        [Fact]
        public void HasPreference_reflects_state()
        {
            var resolver = MakeResolver();
            Assert.False(resolver.HasPreference(AlternativeGenerator.RuleVAsForall));
            resolver.AddPreference(AlternativeGenerator.RuleVAsForall, 1);
            Assert.True(resolver.HasPreference(AlternativeGenerator.RuleVAsForall));
            resolver.Clear();
            Assert.False(resolver.HasPreference(AlternativeGenerator.RuleVAsForall));
        }
    }
}
