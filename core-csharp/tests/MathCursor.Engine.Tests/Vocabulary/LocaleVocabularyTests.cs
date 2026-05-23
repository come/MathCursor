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

        // ─── Relations multi-tier ─────────────────────────────────────

        [Fact]
        public void Fr_relations_have_tiers()
        {
            var v = LocaleVocabulary.LoadEmbedded("fr");
            Assert.Equal(PrecedenceTier.Addsub, v.Relations["+"].Tier);
            Assert.Equal(PrecedenceTier.Muldiv, v.Relations["/"].Tier);
            Assert.Equal(PrecedenceTier.Comp,   v.Relations["="].Tier);
            Assert.Equal(PrecedenceTier.Rel,    v.Relations["in"].Tier);
            Assert.Equal(PrecedenceTier.Setop,  v.Relations["∪"].Tier);
            Assert.Equal(PrecedenceTier.Implies, v.Relations["=>"].Tier);
            Assert.Equal(PrecedenceTier.Iff,    v.Relations["<=>"].Tier);
        }

        [Fact]
        public void Fr_relation_congru_has_tail_mod()
        {
            var v = LocaleVocabulary.LoadEmbedded("fr");
            Assert.Equal("mod", v.Relations["congru"].Tail);
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

        // ─── Précédence ordering ──────────────────────────────────────

        [Fact]
        public void Precedence_tier_ordering_funcpow_strongest()
        {
            // Plus petit (int) = plus fort.
            Assert.True((int)PrecedenceTier.Funcpow < (int)PrecedenceTier.Muldiv);
            Assert.True((int)PrecedenceTier.Muldiv < (int)PrecedenceTier.Addsub);
            Assert.True((int)PrecedenceTier.Addsub < (int)PrecedenceTier.Comp);
            Assert.True((int)PrecedenceTier.Comp < (int)PrecedenceTier.Implies);
            Assert.True((int)PrecedenceTier.Implies < (int)PrecedenceTier.Iff);
        }
    }
}
