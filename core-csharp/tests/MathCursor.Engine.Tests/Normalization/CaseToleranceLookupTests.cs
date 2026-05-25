using System.Collections.Generic;
using Xunit;

namespace MathCursor.Engine.Tests.Normalization
{
    /// <summary>
    /// Tests unitaires <see cref="Engine.Normalization.CaseToleranceLookup"/>.
    /// Chantier 2 — extraction du Tokenizer (2026-05-25).
    /// </summary>
    public class CaseToleranceLookupTests
    {
        private static readonly Dictionary<string, string> Dict = new Dictionary<string, string>
        {
            { "cos", "\\cos" },
            { "sin", "\\sin" },
            { "omega", "\\omega" },
            { "Omega", "\\Omega" },
        };

        [Fact]
        public void Exact_match_returns_value()
        {
            Assert.True(Engine.Normalization.CaseToleranceLookup.TryLookup(Dict, "cos", out var v));
            Assert.Equal("\\cos", v);
        }

        [Fact]
        public void Word_autocapitalize_falls_back_to_lowercase()
        {
            // Word autocorrige Cos (= début de phrase). Doit matcher cos→\cos.
            Assert.True(Engine.Normalization.CaseToleranceLookup.TryLookup(Dict, "Cos", out var v));
            Assert.Equal("\\cos", v);
        }

        [Fact]
        public void All_uppercase_falls_back_to_capitalized()
        {
            // OMEGA → Capitalized = "Omega" → \Omega (= grec majuscule).
            Assert.True(Engine.Normalization.CaseToleranceLookup.TryLookup(Dict, "OMEGA", out var v));
            Assert.Equal("\\Omega", v);
        }

        [Fact]
        public void Lowercase_preferred_over_capitalized()
        {
            // "omega" exact match avant retry capitalized.
            Assert.True(Engine.Normalization.CaseToleranceLookup.TryLookup(Dict, "omega", out var v));
            Assert.Equal("\\omega", v);
        }

        [Fact]
        public void Capitalized_match_direct()
        {
            // "Omega" exact match (= pas de retry).
            Assert.True(Engine.Normalization.CaseToleranceLookup.TryLookup(Dict, "Omega", out var v));
            Assert.Equal("\\Omega", v);
        }

        [Fact]
        public void Not_in_dict_returns_false()
        {
            Assert.False(Engine.Normalization.CaseToleranceLookup.TryLookup(Dict, "xyz", out _));
        }
    }
}
