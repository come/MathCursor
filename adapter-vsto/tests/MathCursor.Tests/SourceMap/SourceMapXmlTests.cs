// MathCursor: capturing mathematical intent from linear keyboard input.
// Copyright (C) 2026  Côme de Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using MathCursor.Host.SourceMap;
using Xunit;

namespace MathCursor.Tests.SourceMap
{
    public class SourceMapXmlTests
    {
        private static EquationSource E(string k1, string k2, string steno = "s",
            string latex = "l", string type = null, DateTime? at = null) => new EquationSource
        {
            K1 = k1, K2 = k2, Steno = steno, Latex = latex, Type = type,
            HandleId = "eq_abc123def456", Version = "0.7.0",
            ParsedAt = at ?? new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc),
        };

        [Fact]
        public void Roundtrip_ChampsPreserves()
        {
            var entries = new List<EquationSource>
            {
                E("k1a", "k2a", steno: "rac 1 sur x", latex: "\\sqrt{1/x}"),
                E("k1b", "k2b", steno: "ligne1\nligne2", latex: "a\nb", type: "chain"),
            };
            var back = SourceMapXml.Deserialize(SourceMapXml.Serialize(entries));

            Assert.Equal(2, back.Count);
            Assert.Equal("rac 1 sur x", back[0].Steno);
            Assert.Equal("\\sqrt{1/x}", back[0].Latex);
            Assert.Null(back[0].Type);
            Assert.Equal("chain", back[1].Type);
            Assert.Equal("ligne1\nligne2", back[1].Steno);   // \n des blocs survit
            Assert.Equal("eq_abc123def456", back[0].HandleId);
            Assert.Equal("0.7.0", back[0].Version);
            Assert.Equal(entries[0].ParsedAt, back[0].ParsedAt);
            Assert.Equal(DateTimeKind.Utc, back[0].ParsedAt.Kind);
        }

        [Fact]
        public void Roundtrip_CaracteresSpeciaux()
        {
            // Sténo/LaTeX avec ce que le XML doit échapper + Unicode math.
            var entries = new List<EquationSource>
            {
                E("k1", "k2", steno: "a < b & \"c\" 'd' ⟺ ∀x", latex: "a<b\\&\\text{«é»}"),
            };
            var back = SourceMapXml.Deserialize(SourceMapXml.Serialize(entries));
            Assert.Equal(entries[0].Steno, back[0].Steno);
            Assert.Equal(entries[0].Latex, back[0].Latex);
        }

        [Fact]
        public void Deserialize_XmlInvalide_ListeVide()
        {
            Assert.Empty(SourceMapXml.Deserialize(null));
            Assert.Empty(SourceMapXml.Deserialize(""));
            Assert.Empty(SourceMapXml.Deserialize("<pas-fermé"));
            Assert.Empty(SourceMapXml.Deserialize("<autre xmlns=\"urn:autre\"/>"));
        }

        [Fact]
        public void Upsert_DernierEcritGagne()
        {
            var entries = new List<EquationSource> { E("k1", "k2", steno: "un demi") };
            SourceMapXml.Upsert(entries, E("k1", "k2", steno: "1 sur 2"));

            var only = Assert.Single(entries);
            Assert.Equal("1 sur 2", only.Steno);   // l'entrée est REMPLACÉE
        }

        [Fact]
        public void Upsert_MemeK1_K2Different_DeuxEntrees()
        {
            // Collision K1 (x² vs x₂) : les deux entrées coexistent, K2 départage.
            var entries = new List<EquationSource> { E("k1", "k2-sup") };
            SourceMapXml.Upsert(entries, E("k1", "k2-sub"));
            Assert.Equal(2, entries.Count);
        }

        [Fact]
        public void Upsert_EvictionAuCap_ParAnciennete()
        {
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var entries = new List<EquationSource>();
            for (int i = 0; i < SourceMapXml.Cap; i++)
                SourceMapXml.Upsert(entries, E("k1-" + i, "k2-" + i, at: t0.AddMinutes(i)));
            Assert.Equal(SourceMapXml.Cap, entries.Count);

            SourceMapXml.Upsert(entries, E("k1-new", "k2-new", at: t0.AddDays(1)));

            Assert.Equal(SourceMapXml.Cap, entries.Count);
            Assert.DoesNotContain(entries, x => x.K1 == "k1-0");      // le plus ancien évincé
            Assert.Contains(entries, x => x.K1 == "k1-new");
            Assert.Contains(entries, x => x.K1 == "k1-1");
        }

        [Fact]
        public void Lookup_ParK1_PuisConfirmationK2()
        {
            // Le schéma d'accès du resolver : K1 → liste, K2 départage.
            var entries = new List<EquationSource> { E("k1", "k2-sup", steno: "x^2"), E("k1", "k2-sub", steno: "x_2") };
            var byK1 = entries.Where(x => x.K1 == "k1").ToList();
            Assert.Equal(2, byK1.Count);
            Assert.Equal("x_2", byK1.Single(x => x.K2 == "k2-sub").Steno);
        }
    }
}
