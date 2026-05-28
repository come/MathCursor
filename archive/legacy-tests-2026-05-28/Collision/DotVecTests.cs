using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Collision
{
    /// <summary>
    /// P28 (2026-05-22) : produit scalaire explicite avec `.` + collision
    /// vec quand les 2 côtés sont des vec candidates.
    /// - `u.v` → top `u \cdot v`, alt `\vec{u} \cdot \vec{v}`
    /// - `AB.BC` → top `AB \cdot BC`, alt `\vec{AB} \cdot \vec{BC}`
    /// - `u.AB`, `AB.u` (= mixte) → top + alt mixte
    /// </summary>
    public class DotVecTests
    {
        private readonly ITestOutputHelper _output;

        public DotVecTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void U_dot_v_yields_vec_collision()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("u.v");
            _output.WriteLine($"top='{r.TopLatex}' coll={r.Collisions.Count}");
            Assert.Equal(@"u \cdot v", r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == @"\vec{u} \cdot \vec{v}");
        }

        [Fact]
        public void AB_dot_BC_yields_vec_collision()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("AB.BC");
            _output.WriteLine($"top='{r.TopLatex}' coll={r.Collisions.Count}");
            Assert.Equal(@"AB \cdot BC", r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == @"\vec{AB} \cdot \vec{BC}");
        }

        [Fact]
        public void U_dot_AB_mixed_vec_collision()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("u.AB");
            Assert.Equal(@"u \cdot AB", r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == @"\vec{u} \cdot \vec{AB}");
        }

        [Fact]
        public void AB_dot_u_mixed_vec_collision()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("AB.u");
            Assert.Equal(@"AB \cdot u", r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == @"\vec{AB} \cdot \vec{u}");
        }

        [Fact]
        public void Dot_with_non_vec_no_alt()
        {
            // `3.x` (= 3 n'est pas vec candidate) → pas de collision vec.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("3.x");
            Assert.DoesNotContain(r.Collisions, c => c.Latex.Contains(@"\vec"));
        }

        // ─── La règle doit s'appliquer DANS une expression composée ───────

        [Fact]
        public void A_equals_u_dot_v_yields_vec_collision_inside_expression()
        {
            // Brief v5 + demande user : la collision dot-vec doit fonctionner
            // partout dans l'expression, pas seulement au top-level.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("A=u.v");
            _output.WriteLine($"top='{r.TopLatex}' coll={r.Collisions.Count}");
            Assert.Equal(@"A = u \cdot v", r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == @"A = \vec{u} \cdot \vec{v}");
        }

        [Fact]
        public void U_dot_v_plus_w_vec_collision_partial()
        {
            // `u.v+w` : dot-vec sur operand 0, +w reste pareil.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("u.v+w");
            _output.WriteLine($"top='{r.TopLatex}' coll={r.Collisions.Count}");
            // top : u \cdot v+w
            Assert.Equal(@"u \cdot v+w", r.TopLatex);
            // Au moins une collision avec \vec{u} \cdot \vec{v}.
            Assert.Contains(r.Collisions, c => c.Latex.Contains(@"\vec{u} \cdot \vec{v}"));
        }
    }
}
