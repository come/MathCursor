using System.Linq;
using Xunit;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Templates;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests <see cref="PrimedDerivativeTemplate"/> (P9g, 2026-05-21) :
    /// dérivées primées Lagrange f', f'', f" (guillemet ASCII), avec
    /// args optionnels f'(x), f''(x). Cf. ADR
    /// <c>2026-05-21-Feat-primed-derivative-and-double-integral</c>.
    /// </summary>
    public class PrimedDerivativeTemplateTests
    {
        private static PatternScanContext Ctx(string source) =>
            new PatternScanContext(
                topAst: null,
                topLatex: source,
                source: source,
                caretOffset: null,
                startPos: 0,
                registry: null);

        private static PrimedDerivativeTemplate New() => new PrimedDerivativeTemplate();

        private static PatternCompletion ExpandAll(string source)
        {
            var t = New();
            var ctx = Ctx(source);
            var head = t.TryMatchHead(ctx);
            Assert.NotNull(head);
            return t.Expand(head!, ctx).First();
        }

        // ─── Détection (TryMatchHead) ─────────────────────────────────

        [Theory]
        [InlineData("f'")]
        [InlineData("g'")]
        [InlineData("h'")]
        public void Matches_single_primed(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
            Assert.Equal("primed-derivative", m!.TemplateId);
        }

        [Theory]
        [InlineData("f''")]
        [InlineData("g'''")]
        [InlineData("h''''")]
        public void Matches_multi_primed(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
        }

        [Fact]
        public void Matches_guillemet_as_double_prime()
        {
            // f" (guillemet ASCII) = f'' (= 2 primes)
            var m = New().TryMatchHead(Ctx("f\""));
            Assert.NotNull(m);
            var primes = ((FilledSlotAtom)m!.Slots["primes_count"]).Text;
            Assert.Equal("2", primes);
        }

        [Fact]
        public void Rejects_letter_alone_no_prime()
        {
            // "f" seul (sans apostrophe) → pas un primed derivative
            Assert.Null(New().TryMatchHead(Ctx("f")));
        }

        [Fact]
        public void Rejects_letter_after_letter_no_boundary()
        {
            // "xf'" : f précédé par x (lettre) → boundary échoue
            Assert.Null(New().TryMatchHead(Ctx("xf'")));
        }

        [Fact]
        public void Rejects_more_than_4_primes()
        {
            // "f'''''" (5 primes) > limite raisonnable
            Assert.Null(New().TryMatchHead(Ctx("f'''''")));
        }

        // ─── Expand : rendering ───────────────────────────────────────

        [Fact]
        public void F_prime_renders_natively()
        {
            var c = ExpandAll("f'");
            Assert.Equal("f'", c.PreviewLatex);
            Assert.Equal("f′", c.Description); // unicode prime ′
        }

        [Fact]
        public void F_double_prime_renders_two_apostrophes()
        {
            var c = ExpandAll("f''");
            Assert.Equal("f''", c.PreviewLatex);
            Assert.Equal("f″", c.Description); // unicode double prime ″
        }

        [Fact]
        public void F_guillemet_converted_to_two_apostrophes()
        {
            // L'user a tapé f" → préview rend f'' (= 2 apostrophes LaTeX)
            var c = ExpandAll("f\"");
            Assert.Equal("f''", c.PreviewLatex);
        }

        [Fact]
        public void F_prime_with_args()
        {
            // f'(x) → préserve les args
            var c = ExpandAll("f'(x)");
            Assert.Equal("f'(x)", c.PreviewLatex);
            Assert.Equal("f′(x)", c.Description);
        }

        [Fact]
        public void F_double_prime_with_args()
        {
            var c = ExpandAll("f''(x)");
            Assert.Equal("f''(x)", c.PreviewLatex);
            Assert.Equal("f″(x)", c.Description);
        }

        [Fact]
        public void F_guillemet_with_args()
        {
            // f"(x) → f''(x) canonique
            var c = ExpandAll("f\"(x)");
            Assert.Equal("f''(x)", c.PreviewLatex);
        }

        [Fact]
        public void Args_can_contain_complex_expression()
        {
            var c = ExpandAll("f'(2x+1)");
            Assert.Equal("f'(2x+1)", c.PreviewLatex);
        }

        // ─── Mutation source canonique ────────────────────────────────

        [Fact]
        public void Mutation_normalizes_guillemet_to_apostrophes()
        {
            var c = ExpandAll("f\"(x)");
            Assert.Equal("f''(x)", c.Mutation!.Replacement);
        }

        [Fact]
        public void Mutation_preserves_apostrophes_when_already_canonical()
        {
            var c = ExpandAll("f'(x)");
            Assert.Equal("f'(x)", c.Mutation!.Replacement);
        }

        // ─── Completeness ─────────────────────────────────────────────

        [Fact]
        public void Always_complete()
        {
            // Primed est always complet dès qu'il est détecté (= pas de
            // slots optionnels guidants comme forall).
            var c = ExpandAll("f'");
            Assert.Equal(100, c.CompletenessScore);
        }

        // ─── Robustesse ───────────────────────────────────────────────

        [Fact]
        public void TryMatchHead_null_ctx_returns_null()
        {
            Assert.Null(New().TryMatchHead(null!));
        }
    }
}
