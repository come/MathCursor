using System.Collections.Generic;
using System.Text;
using MathCursor.Engine.Rewriting;
using MathCursor.Engine.Rewriting.Yaml;
using MathCursor.Engine.Vocabulary;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Rewriting
{
    /// <summary>
    /// Phase D-6 — shadow parity audit : compare <see cref="MathCursor.Engine.MathEngine.Resolve"/>
    /// (= legacy) vs <see cref="RewriteEngine.Resolve"/> sur ~80 inputs réels
    /// extraits des tests existants. Liste les divergences sans les faire
    /// échouer. Pour mesurer le périmètre de la bascule prod.
    /// </summary>
    public class ShadowParityAuditTests
    {
        private readonly ITestOutputHelper _output;
        public ShadowParityAuditTests(ITestOutputHelper output) { _output = output; }

        /// <summary>Inputs représentatifs extraits par grep des tests
        /// engine existants (Collision, Concepts, etc.).</summary>
        private static readonly string[] AuditInputs = new[]
        {
            // Collision/LegacyCoverage
            "x^a+b", "u_n+1", "ABC", "^ABC", "^a",
            // Collision/VectorCoords
            "u(1;2)", "u(1, 2)", "AB(3;4)", "f(x)",
            // Collision/CollisionTests
            "lim x 0 f(x)", "1+2", "lim x 0 f", "sum k 1 n (1/k)", "lim x 0",
            // Collision/SlurpFraction
            "1/x+1", "a/b-c", "a+b", "1/x",
            // Collision/VecAngle
            "u", "v", "AB", "ab", "x^2", "lim u 0 f(u)",
            // Collision/DotVec
            "u.v", "AB.BC", "u.AB", "AB.u", "3.x", "A=u.v", "u.v+w",
            // Concepts/AnchorInCell
            "(somme k 0 1 f(k))", "(lim x 0 f(x))",
            // Concepts/CosX
            "cos x", "Cos x", "cos(x)2",
            // Concepts/IntervalHalfOpen
            "[0,1[", "]0,1]", "]0,1[", "[0,1]",
            // Concepts/FuncDef
            "G:x->1/x", "G :x->1/x", "f:x,y->x+y", "a:b",
            // Concepts/AnchorInExpression
            "A=norm u", "1+frac a b", "x=sqrt 2", "vec u+vec v",
            // Concepts/Composition
            "lim x 0 f + lim x 1 g", "lim x 0 1/x+1",
            // Concepts/Intervalles
            "(0,1)", "[0,1)", "[a,b]",
            // Concepts/Geometrie
            "[AB]",
            // Concepts/OneOverXN
            "1/x2", "a/b3",
            // Concepts/PlusY2
            "+y2", "+ y2", "x2+y2", "=1/2x+1", "=> y2",
            // Concepts/LetterDigitSupSub
            "x2", "e3", "y12", "x_2", "x2+1",
            // Concepts/PopupGuideSquare
            "sum", "sum k", "sum k 0", "lim", "sum k 0 n f(k)",
            // Concepts/PrefixMatch
            "som", "inte", "ome", "OME", "om", "arc",
            "f(som)", "xyz", "som k 0 n f(k)", "limi x 0 f(x)",
            // Concepts/PrimedDerivative
            "f'", "f''", "f'(x)",
            // Concepts/Salve2
            "[0,1[ inter [0,1]", "[0,1[ u [0,1]", "f(u)", "2u",
            "F:x->sum k 0 n f(k)*x",
            // Concepts/Probabilite
            "P(A)", "P(X=k)",
            // Concepts/RealWorld
            "f(x)=1/x+1", "AB/AC", "(AB)/(CD)", "x=1/y+z", "a/b", "a/b*c", "1/a+1/b",
        };

        [Fact]
        public void Audit_parity_MathEngine_vs_RewriteEngine()
        {
            var legacy = MathCursor.Engine.MathEngine.BuildDefault("fr");
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            var rules = new List<RewriteRule>();
            rules.AddRange(PrimitiveRules.All);
            rules.AddRange(RewriteRuleLoader.LoadAllEmbedded(vocab));
            var rewrite = new RewriteEngine(vocab, rules);

            int total = 0, match = 0;
            var divergences = new List<string>();
            foreach (var input in AuditInputs)
            {
                total++;
                string leg, re;
                try { leg = legacy.Resolve(input).TopLatex; } catch { leg = "<EXCEPTION>"; }
                try { re = rewrite.Resolve(input).TopLatex; } catch { re = "<EXCEPTION>"; }
                if (leg == re)
                {
                    match++;
                }
                else
                {
                    divergences.Add($"  ❌ '{input}'");
                    divergences.Add($"      legacy:    {leg}");
                    divergences.Add($"      rewriting: {re}");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== Shadow parity audit ===");
            sb.AppendLine($"Match: {match}/{total}  ({100.0 * match / total:0.0}%)");
            if (divergences.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Divergences ({divergences.Count / 3}) :");
                foreach (var d in divergences) sb.AppendLine(d);
            }
            _output.WriteLine(sb.ToString());

            // Pas d'assertion stricte — c'est un audit, pas un blocker.
            Assert.True(match >= 1, "Au moins 1 input doit matcher pour confirmer que l'audit fonctionne.");
        }
    }
}
