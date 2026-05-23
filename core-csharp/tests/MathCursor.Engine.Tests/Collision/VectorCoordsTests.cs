using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Collision
{
    /// <summary>
    /// P31 : `u(1,2)` ou `u(1;2)` → alt vec coords colonne.
    /// </summary>
    public class VectorCoordsTests
    {
        private readonly ITestOutputHelper _output;
        public VectorCoordsTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void U_with_semicolon_coords_yields_vec_coords()
        {
            // FR : `u(1;2)` non ambigu (= `;` rowsep explicite).
            // `u(1,2)` collé serait lu comme u(1.2) (décimale).
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("u(1;2)");
            _output.WriteLine($"top='{r.TopLatex}' coll={r.Collisions.Count}");
            Assert.Contains(r.Collisions,
                c => c.Latex.Contains(@"\vec{u}\begin{pmatrix}1 \\ 2\end{pmatrix}"));
        }

        [Fact]
        public void U_comma_with_space_yields_vec_coords()
        {
            // FR : `u(1, 2)` avec espace après la virgule force le Sep
            // (= virgule décimale n'est plus possible).
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("u(1, 2)");
            _output.WriteLine($"top='{r.TopLatex}' coll={r.Collisions.Count}");
            Assert.Contains(r.Collisions,
                c => c.Latex.Contains(@"\vec{u}\begin{pmatrix}1 \\ 2\end{pmatrix}"));
        }

        [Fact]
        public void AB_with_coords_yields_vec_coords()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("AB(3;4)");
            _output.WriteLine($"top='{r.TopLatex}' coll={r.Collisions.Count}");
            Assert.Contains(r.Collisions,
                c => c.Latex.Contains(@"\vec{AB}\begin{pmatrix}3 \\ 4\end{pmatrix}"));
        }

        [Fact]
        public void Function_call_with_single_arg_no_vec_coords()
        {
            // `f(x)` = 1 arg, pas vec coords (= demande ≥ 2 coords).
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("f(x)");
            Assert.DoesNotContain(r.Collisions, c => c.Latex.Contains(@"\vec{f}\begin{pmatrix}"));
        }
    }
}
