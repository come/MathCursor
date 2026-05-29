using System.Collections.Generic;
using System.Linq;
using System.Text;
using MathCursor.Engine.Rewriting;
using MathCursor.Engine.Rewriting.Yaml;
using MathCursor.Engine.Vocabulary;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Rewriting
{
    /// <summary>
    /// Phase D-6 : audit de la bascule. Compare TopLatex de MathEngine
    /// (= BuildDefault legacy) et BuildDefaultWithRewriteEngine sur tous
    /// les inputs représentatifs. Classe les divergences par cause racine.
    /// Sert à planifier l'ordre d'attaque de la bascule.
    /// </summary>
    public class BascStrategyAuditTests
    {
        private readonly ITestOutputHelper _output;
        public BascStrategyAuditTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void Classify_divergences_by_root_cause()
        {
            var legacy = MathCursor.Engine.MathEngine.BuildDefault("fr");
            var rewriting = MathCursor.Engine.MathEngine.BuildDefaultWithRewriteEngine("fr");

            // Catégories de tests à classer.
            var groups = new Dictionary<string, List<string>>
            {
                ["A. Detector: DotVec (u.v, AB.BC, etc.)"] = new()
                    { "u.v", "AB.BC", "u.AB", "AB.u", "A=u.v", "u.v+w" },
                ["B. Detector: VectorCoords (u(1;2), AB(3;4))"] = new()
                    { "u(1;2)", "u(1, 2)", "AB(3;4)" },
                ["C. Detector: SlurpFraction (1/x+1, a/b-c)"] = new()
                    { "1/x+1", "a/b-c", "1/a+1/b" },
                ["D. Detector: LetterSupSubNum (x2, e3, y12, cos(x)2)"] = new()
                    { "x2", "e3", "y12", "x2+1", "x2+y2", "1/x2", "a/b3", "+y2", "cos(x)2" },
                ["E. Detector: Slurp SupSub (a^b+c, u_n+1)"] = new()
                    { "x^a+b", "u_n+1" },
                ["F. Detector: TripleUpper/VecAngle (ABC, ^ABC, ^a)"] = new()
                    { "ABC", "^ABC", "^a" },
                ["G. Prefix-match popup (sum, lim, som, ome, OME)"] = new()
                    { "sum", "sum k", "sum k 0", "lim", "som", "ome", "OME", "om" },
                ["H. Spacing (=, +) autour relations"] = new()
                    { "A=norm u", "1+frac a b", "x=sqrt 2", "A=u.v",
                      "f(x)=1/x+1", "x=1/y+z", "P(X=k)" },
                ["I. Cos/Sin sans backslash"] = new()
                    { "cos x", "Cos x" },
                ["J. Composition (lim x 0 f + lim x 1 g)"] = new()
                    { "lim x 0 f + lim x 1 g", "vec u+vec v" },
                ["K. Parens preservation ((0,1), (AB)/(CD))"] = new()
                    { "(0,1)", "(AB)/(CD)" },
                ["L. Half-open intervals (]0,1])"] = new()
                    { "]0,1]", "]0,1[", "[0,1[" },
                ["M. Bugs spécifiques rewriting"] = new()
                    { "som k 0 n f(k)", "limi x 0 f(x)", "f'(x)",
                      "F:x->sum k 0 n f(k)*x", "[0,1[ inter [0,1]",
                      "[0,1[ u [0,1]", "+ y2", "=1/2x+1", "=> y2" },
                ["N. Marche directement"] = new()
                    { "u", "v", "AB", "1+2", "a+b", "1/x", "frac 1 2",
                      "vec u", "sqrt 2", "P(A)", "x^2", "f'", "f''" },
            };

            var sb = new StringBuilder();
            sb.AppendLine("=== Bascule strategy audit ===");
            int totalMatch = 0, totalCount = 0;
            foreach (var kv in groups)
            {
                int gMatch = 0, gTotal = 0;
                var details = new List<string>();
                foreach (var input in kv.Value)
                {
                    gTotal++;
                    string leg, re;
                    try { leg = legacy.Resolve(input).TopLatex; } catch { leg = "<EXCEPTION>"; }
                    try { re = rewriting.Resolve(input).TopLatex; } catch { re = "<EXCEPTION>"; }
                    if (leg == re) gMatch++;
                    else details.Add($"    '{input}': legacy='{leg}' vs rewriting='{re}'");
                }
                totalCount += gTotal;
                totalMatch += gMatch;
                sb.AppendLine($"{kv.Key} — {gMatch}/{gTotal} match");
                foreach (var d in details) sb.AppendLine(d);
            }
            sb.AppendLine();
            sb.AppendLine($"TOTAL: {totalMatch}/{totalCount}  ({100.0 * totalMatch / totalCount:0.0}%)");
            _output.WriteLine(sb.ToString());

            Assert.True(totalMatch >= 0); // Always pass — audit only
        }
    }
}
