using Xunit;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Tests.Vocabulary
{
    /// <summary>
    /// Tests <see cref="LocaleVocabulary"/> sur les locales embedded
    /// <c>fr.yml</c> et <c>en.yml</c>. Cf. ADR
    /// <c>2026-05-22-Feat-engine-poc-isolation</c> (P11.2).
    /// </summary>
    public class LocaleVocabularyTests
    {
        // ─── Chargement embedded ──────────────────────────────────────

        [Fact]
        public void Loads_fr_embedded()
        {
            var v = LocaleVocabulary.LoadEmbedded("fr");
            Assert.Equal("fr", v.Code);
            Assert.NotEmpty(v.Classes);
            Assert.NotEmpty(v.Anchors);
            Assert.NotEmpty(v.Relations);
        }

        [Fact]
        public void Loads_en_embedded()
        {
            var v = LocaleVocabulary.LoadEmbedded("en");
            Assert.Equal("en", v.Code);
            Assert.Equal(".", v.Decimal);
            // EN colsep contient virgule + espace (vs FR juste espace).
            Assert.Contains(",", v.ColSep);
        }

        // ─── Classes FR ───────────────────────────────────────────────

        [Fact]
        public void Fr_class_to_contains_tend_vers()
        {
            var v = LocaleVocabulary.LoadEmbedded("fr");
            Assert.Equal("to", v.FindClass("tend vers"));
            Assert.Equal("to", v.FindClass("->"));
            Assert.Equal("to", v.FindClass("→"));
        }

        [Fact]
        public void Fr_class_filler()
        {
            var v = LocaleVocabulary.LoadEmbedded("fr");
            Assert.Equal("filler", v.FindClass("quand"));
            Assert.Equal("filler", v.FindClass("lorsque"));
        }

        [Fact]
        public void Unknown_token_yields_null_class()
        {
            var v = LocaleVocabulary.LoadEmbedded("fr");
            Assert.Null(v.FindClass("xyz"));
        }

        // ─── Anchors ──────────────────────────────────────────────────

        [Fact]
        public void Fr_anchors_canonicalize()
        {
            var v = LocaleVocabulary.LoadEmbedded("fr");
            Assert.Equal("lim", v.FindAnchor("limite"));
            Assert.Equal("sum", v.FindAnchor("somme"));
            Assert.Equal("prod", v.FindAnchor("produit"));
        }

        // ─── Relations : Tex (= rendu LaTeX pour reclassement tokenizer) ──

        [Fact]
        public void Fr_relations_expose_tex()
        {
            var v = LocaleVocabulary.LoadEmbedded("fr");
            Assert.Equal("+", v.Relations["+"].Tex);
            Assert.Equal(@"\in", v.Relations["in"].Tex);
            Assert.Equal(@"\cup", v.Relations["∪"].Tex);
            Assert.Equal(@"\iff", v.Relations["<=>"].Tex);
            Assert.Equal(@"\equiv", v.Relations["congru"].Tex);
        }

        // ─── Glue ─────────────────────────────────────────────────────

        [Fact]
        public void Fr_glue_includes_arrow_and_equals()
        {
            var v = LocaleVocabulary.LoadEmbedded("fr");
            Assert.True(v.IsGlue("->"));
            Assert.True(v.IsGlue("="));
            Assert.True(v.IsGlue("→"));
            Assert.False(v.IsGlue("+"));
        }

        // ─── Séparateurs + décimale ───────────────────────────────────

        [Fact]
        public void Fr_decimal_is_comma_and_colsep_is_space_only()
        {
            var v = LocaleVocabulary.LoadEmbedded("fr");
            Assert.Equal(",", v.Decimal);
            Assert.Single(v.ColSep);
            Assert.Contains(" ", v.ColSep);
            Assert.Contains(";", v.RowSep);
        }

        // Le test Precedence_tier_ordering a été retiré (2026-05-29) :
        // la machinerie PrecedenceTier appartenait au moteur legacy
        // (StackParser/PrecedenceClimber). Le moteur V2 compose par règles
        // YAML, sans climbing de tiers.
    }
}
