using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// User-request 2026-05-25 : en cours de frappe, si l'user tape un préfixe
    /// reconnu d'un keyword (anchor `somme`, function `omega`, relation `inter`,
    /// …), la popup doit montrer la suggestion. Min 3 chars pour éviter le spam.
    ///
    /// <para>Comportement uniforme top-level ET dans groupes (= `som` seul ou
    /// `f(som)`). Si 1 match unique → TopLatex = rendu (avec \square pour les
    /// anchors). Si N matches → TopLatex = source brut + Collisions[N].</para>
    /// </summary>
    public class PrefixMatchTypingTests
    {
        private readonly ITestOutputHelper _output;

        public PrefixMatchTypingTests(ITestOutputHelper output) { _output = output; }

        // ─── 1 match unique : TopLatex = rendu ──────────────────────

        [Fact]
        public void Som_alone_suggests_somme_with_squares()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("som");
            _output.WriteLine($"top='{r.TopLatex}' complete={r.IsComplete}");
            Assert.Contains("\\sum", r.TopLatex);
            Assert.Contains("\\square", r.TopLatex);
            Assert.False(r.IsComplete);
        }

        [Fact]
        public void Inte_alone_suggests_inter_cap_and_integrale()
        {
            // `inte` matche 2 keywords : `integrale` (anchor) + `inter`
            // (relation `\cap`). Multi-match → Collisions des deux.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("inte");
            _output.WriteLine($"top='{r.TopLatex}' collisions={r.Collisions.Count}");
            for (int i = 0; i < r.Collisions.Count; i++)
                _output.WriteLine($"  cand[{i}] latex='{r.Collisions[i].Latex}' desc='{r.Collisions[i].Description}'");
            Assert.True(r.Collisions.Count >= 2, $"Expected ≥2 collisions, got {r.Collisions.Count}");
            Assert.Contains(r.Collisions, c => c.Latex.Contains("\\cap"));
            Assert.Contains(r.Collisions, c => c.Latex.Contains("\\int"));
        }

        [Fact]
        public void Ome_alone_suggests_omega()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("ome");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\omega", r.TopLatex);
        }

        [Fact]
        public void OMG_uppercase_suggests_Omega_capital()
        {
            // User-request : `OMEGA` → `\Omega` (= grand omega).
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("OME");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\Omega", r.TopLatex);
        }

        // ─── Mot complet : pas de prefix-match (= laisse au full lookup) ──

        [Fact]
        public void Omega_complete_renders_directly()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("omega");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Equal("\\omega", r.TopLatex.Trim());
        }

        [Fact]
        public void Omega_complete_uppercase_renders_capital()
        {
            // `OMEGA` complet → `\Omega` via TryLookupFunction (uppercase retry).
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("OMEGA");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Equal("\\Omega", r.TopLatex.Trim());
        }

        // ─── < 3 chars : pas de prefix-match (= éviter spam) ────────

        [Fact]
        public void Two_chars_no_prefix_match()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("om");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.DoesNotContain("\\omega", r.TopLatex);
            Assert.Equal("om", r.TopLatex.Trim());
        }

        // ─── N matches : Collisions ─────────────────────────────────

        [Fact]
        public void Multi_match_returns_source_plus_collisions()
        {
            // `sin` est complet (= match direct via function). Mais `si` < 3 chars.
            // Pour avoir multi-match, on choisit un préfixe qui matche au moins 2.
            // `arc` matche `arcsin`, `arccos`, `arctan` (= 3 functions).
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("arc");
            _output.WriteLine($"top='{r.TopLatex}' collisions={r.Collisions.Count}");
            for (int i = 0; i < r.Collisions.Count; i++)
                _output.WriteLine($"  cand[{i}] latex='{r.Collisions[i].Latex}' desc='{r.Collisions[i].Description}'");
            Assert.True(r.Collisions.Count >= 2, $"Expected ≥2 collisions, got {r.Collisions.Count}");
            Assert.Contains(r.Collisions, c => c.Latex.Contains("\\arcsin"));
            Assert.Contains(r.Collisions, c => c.Latex.Contains("\\arccos"));
        }

        // ─── Dans un groupe : comportement uniforme ─────────────────

        [Fact]
        public void Som_inside_paren_group_suggests_somme()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("f(som)");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\sum", r.TopLatex);
        }

        [Fact]
        public void Ome_inside_paren_group_suggests_omega()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("f(ome)");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\omega", r.TopLatex);
        }

        // ─── Word standalone non-prefix : pas de fausse interception ────

        [Fact]
        public void Random_word_not_a_prefix_returns_source()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("xyz");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Equal("xyz", r.TopLatex.Trim());
        }

        // ─── Prefix-aware anchor literal : `som k 0 n f(k)` full match ──

        [Fact]
        public void Som_with_full_args_matches_somme_rule_via_prefix()
        {
            // User-report 2026-05-25 : `som k 0 n f(k)` doit produire le
            // full somme avec args remplis, pas carrés. Le prefix `som`
            // matche l'anchor literal `sum` via l'alias `somme` (= prefix-aware
            // ShapeMatcher.TryMatchOne case Literal).
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("som k 0 n f(k)");
            _output.WriteLine($"top='{r.TopLatex}' complete={r.IsComplete}");
            Assert.Equal("\\sum_{k=0}^{n} f(k)", r.TopLatex);
            Assert.True(r.IsComplete);
            Assert.DoesNotContain("\\square", r.TopLatex);
        }

        [Fact]
        public void Som_with_partial_args_keeps_squares()
        {
            // `som k 0` (= 1 arg manquant) → partial avec carrés sur les
            // slots manquants.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("som k 0");
            _output.WriteLine($"top='{r.TopLatex}' complete={r.IsComplete}");
            Assert.Contains("\\sum_{k=0}", r.TopLatex);
            Assert.Contains("\\square", r.TopLatex);
            Assert.False(r.IsComplete);
        }

        [Fact]
        public void Lim_with_full_args_matches_via_prefix()
        {
            // `lim x 0 f(x)` : `lim` est canonical anchor pour la rule
            // `limite-...`. Direct match (= pas prefix). Sanity check.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("lim x 0 f(x)");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\lim", r.TopLatex);
            Assert.DoesNotContain("\\square", r.TopLatex);
        }

        [Fact]
        public void Limi_prefix_with_full_args()
        {
            // `limi x 0 f(x)` : prefix de `limite` (alias canonical `lim`).
            // Doit matcher la rule lim.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("limi x 0 f(x)");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\lim", r.TopLatex);
            Assert.DoesNotContain("\\square", r.TopLatex);
        }
    }
}
