using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Collision
{
    /// <summary>
    /// P27 (2026-05-22) : collisions vecteur / angle.
    /// - Lettre seule `u` → top "u", alt "\vec{u}"
    /// - 2 majuscules `AB` → top "AB", alt "\vec{AB}"
    /// - `^a` → "\widehat{a}" (angle d'1 lettre)
    /// - `^ABC` → "\widehat{ABC}" (angle 3 points)
    /// </summary>
    public class VecAngleTests
    {
        private readonly ITestOutputHelper _output;

        public VecAngleTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // ─── Lettre seule → vec ──────────────────────────────────────

        [Fact]
        public void Lone_letter_yields_vec_alt()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("u");
            _output.WriteLine($"top='{r.TopLatex}' coll={r.Collisions.Count}");
            Assert.Equal("u", r.TopLatex);
            Assert.Single(r.Collisions);
            Assert.Equal(@"\vec{u}", r.Collisions[0].Latex);
        }

        [Fact]
        public void Single_letter_v_works()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("v");
            Assert.Equal("v", r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == @"\vec{v}");
        }

        // ─── 2 majuscules → vec ──────────────────────────────────────

        [Fact]
        public void Two_uppercase_letters_yield_vec_alt()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("AB");
            _output.WriteLine($"top='{r.TopLatex}' coll={r.Collisions.Count}");
            Assert.Equal("AB", r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == @"\vec{AB}");
        }

        [Fact]
        public void Two_lowercase_no_vec_alt()
        {
            // `ab` 2 lettres minuscules → produit implicite, pas vec.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("ab");
            Assert.DoesNotContain(r.Collisions, c => c.Latex.Contains(@"\vec"));
        }

        // ─── Angle ^a et ^ABC ────────────────────────────────────────

        [Fact]
        public void Caret_single_letter_yields_angle()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("^a");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Equal(@"\widehat{a}", r.TopLatex);
        }

        [Fact]
        public void Caret_three_uppercase_yields_angle()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("^ABC");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Equal(@"\widehat{ABC}", r.TopLatex);
        }

        [Fact]
        public void Caret_in_middle_remains_exposant()
        {
            // `x^2` (= ^ pas en début) → exposant, pas angle.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("x^2");
            Assert.Equal("x^2", r.TopLatex);
            Assert.DoesNotContain(@"\widehat", r.TopLatex);
        }

        // ─── Pas de vec collision dans contexte composé ──────────────

        [Fact]
        public void Lone_letter_inside_expression_no_vec()
        {
            // Quand `u` apparaît dans `lim u 0 f(u)`, on ne veut pas
            // proposer vec.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("lim u 0 f(u)");
            Assert.DoesNotContain(r.Collisions, c => c.Latex.Contains(@"\vec{u}"));
        }
    }
}
