using MathCursor.Host.Update;
using Xunit;

namespace MathCursor.Tests.Host.Update
{
    /// <summary>
    /// Comparateur de versions pour l'indicateur « MAJ dispo » (ADR
    /// 2026-06-18-Feat-ribbon-update-badge). Règle clé : jamais de faux positif.
    /// </summary>
    public sealed class VersionCompareTests
    {
        [Theory]
        [InlineData("0.11.1", "0.10.3", true)]   // patch + minor en avance
        [InlineData("0.11.0", "0.10.3", true)]
        [InlineData("1.0.0", "0.99.99", true)]   // major
        [InlineData("0.11.1", "0.11.0", true)]   // patch
        [InlineData("0.11.0", "0.11.0", false)]  // égal → pas de MAJ
        [InlineData("0.10.3", "0.11.0", false)]  // serveur plus vieux → pas de MAJ
        [InlineData("0.11.0", "0.11.1", false)]  // build dev en avance
        public void IsNewer_compare_major_minor_build(string latest, string current, bool expected)
        {
            Assert.Equal(expected, VersionCompare.IsNewer(latest, current));
        }

        [Fact]
        public void Revision_ignoree()
        {
            // 0.11.0.0 (assembly) vs 0.11.0 (serveur) → pas de MAJ (égalité M.m.B).
            Assert.False(VersionCompare.IsNewer("0.11.0", "0.11.0.0"));
            Assert.False(VersionCompare.IsNewer("0.11.0.0", "0.11.0"));
        }

        [Theory]
        [InlineData("v0.11.1", "0.11.0", true)]  // préfixe v toléré
        [InlineData("", "0.11.0", false)]        // vide → pas de faux positif
        [InlineData("nawak", "0.11.0", false)]   // non parsable → pas de faux positif
        [InlineData("0.11", "0.11.0", false)]    // pas 3 composants → ignoré
        public void Parsing_tolerant_jamais_de_faux_positif(string latest, string current, bool expected)
        {
            Assert.Equal(expected, VersionCompare.IsNewer(latest, current));
        }

        [Theory]
        [InlineData("{\"latest\":\"0.11.0\"}", "0.11.0")]
        [InlineData("{ \"latest\": \"1.2.3\" }", "1.2.3")]
        [InlineData("{\"other\":1,\"latest\":\"0.9.9\"}", "0.9.9")]
        public void ExtractLatest_lit_le_champ(string json, string expected)
        {
            Assert.Equal(expected, VersionCompare.ExtractLatest(json));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("")]
        [InlineData("pas du json")]
        public void ExtractLatest_absent_renvoie_null(string json)
        {
            Assert.Null(VersionCompare.ExtractLatest(json));
        }
    }
}
