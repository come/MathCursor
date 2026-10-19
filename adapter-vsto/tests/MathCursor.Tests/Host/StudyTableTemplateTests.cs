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

using System.Linq;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Gabarit des tableaux de signe / de variation (ADR 2026-10-16-Feat-sign-
    /// variation-table-insert) : étiquettes + colonnes alternées valeur /
    /// intervalle, flèches caractère dans la ligne f(x) de variation.
    /// </summary>
    public sealed class StudyTableTemplateTests
    {
        [Theory]
        [InlineData(false, 2)]
        [InlineData(true, 3)]
        public void Grille_rectangulaire_etiquettes_plus_cinq_colonnes(bool variation, int rows)
        {
            var t = StudyTableTemplate.Build(variation ? StudyTableKind.Variation : StudyTableKind.Sign);
            Assert.Equal(rows, t.Rows.Count);
            Assert.Equal(6, t.ColumnCount);
            Assert.All(t.Rows, r => Assert.Equal(t.ColumnCount, r.Count));
        }

        [Fact]
        public void Ligne_x_bornes_infinies_et_valeur_du_milieu_a_remplir()
        {
            var x = StudyTableTemplate.Build(StudyTableKind.Sign).Rows[0];
            Assert.Equal("x", x[0].Text);
            Assert.True(x[0].Italic);
            Assert.Equal("−∞", x[1].Text);
            Assert.Equal("", x[3].Text);
            Assert.Equal("+∞", x[5].Text);
        }

        [Fact]
        public void Colonnes_impaires_valeurs_paires_intervalles()
        {
            Assert.False(StudyTableTemplate.IsValueColumn(0));
            Assert.True(StudyTableTemplate.IsValueColumn(1));
            Assert.False(StudyTableTemplate.IsValueColumn(2));
            Assert.True(StudyTableTemplate.IsValueColumn(3));
            Assert.False(StudyTableTemplate.IsValueColumn(4));
            Assert.True(StudyTableTemplate.IsValueColumn(5));
        }

        [Fact]
        public void Signe_exemple_coherent_plus_zero_moins()
        {
            var f = StudyTableTemplate.Build(StudyTableKind.Sign).Rows[1];
            Assert.Equal("f(x)", f[0].Text);
            Assert.Equal(new[] { "", "+", "0", "−", "" }, f.Skip(1).Select(c => c.Text));
            Assert.Equal(-1, StudyTableTemplate.Build(StudyTableKind.Sign).TallRowIndex);
        }

        [Fact]
        public void Variation_fleches_dans_les_intervalles_valeurs_en_haut_ou_en_bas()
        {
            var t = StudyTableTemplate.Build(StudyTableKind.Variation);
            Assert.Equal("f′(x)", t.Rows[1][0].Text);
            Assert.Equal(2, t.TallRowIndex);

            var f = t.Rows[2];
            Assert.Equal("f(x)", f[0].Text);
            Assert.True(f[2].IsArrow);
            Assert.Equal(StudyTableTemplate.ArrowUp, f[2].Text);
            Assert.True(f[4].IsArrow);
            Assert.Equal(StudyTableTemplate.ArrowDown, f[4].Text);

            // f′ : + 0 −  →  minimum, maximum, minimum.
            Assert.Equal(CellVAlign.Bottom, f[1].VAlign);
            Assert.Equal(CellVAlign.Top, f[3].VAlign);
            Assert.Equal(CellVAlign.Bottom, f[5].VAlign);
            Assert.All(new[] { 1, 3, 5 }, c => Assert.False(f[c].IsArrow));
        }
    }
}
