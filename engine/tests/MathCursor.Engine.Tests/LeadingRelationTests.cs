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
using MathCursor.Engine;
using Xunit;

namespace MathCursor.Engine.Tests;

// Relation en TÊTE d'entrée (« =2x ») : membre gauche vide = PAS une
// expression — c'est le marqueur de la couche chaîne multiligne, qui vit
// hors moteur. Avant 2026-06-10 le membre vide devenait un trou (« □ = 2x »),
// requalifié bug par l'auteur du moteur (divergence assumée avec le JS figé ;
// aucune fixture du contrat n'était impactée).
public sealed class LeadingRelationTests
{
    [Theory]
    [InlineData("=2x")]
    [InlineData("= 2/3x")]
    [InlineData("<3")]
    [InlineData(">x+1")]
    [InlineData("->1/x")]
    [InlineData("=")]
    public void Leading_relation_yields_no_reading(string input)
    {
        var r = ForestEngine.Analyze(input);
        Assert.Equal("erreur", r.Decision);
        Assert.Empty(r.Ranked);
    }

    [Fact]
    public void Trailing_relation_keeps_right_hole_preview()
    {
        // « a= » en cours de frappe : l'aperçu a=□ reste légitime.
        var r = ForestEngine.Analyze("a=");
        Assert.NotEqual("erreur", r.Decision);
        Assert.Contains("\\square", r.Ranked[0].Latex);
        Assert.StartsWith("a=", r.Ranked[0].Latex);
    }

    [Fact]
    public void Normal_relation_unchanged()
    {
        var r = ForestEngine.Analyze("a=b");
        Assert.Equal("auto", r.Decision);
        Assert.Equal("a=b", r.Ranked[0].Latex);
    }

    // Trou GAUCHE en tête, infixes non-relation et séparateurs compris : aucune
    // lecture non plus. On tape de gauche à droite, un opérande gauche manquant
    // n'est jamais une frappe en cours mais une zone mal découpée (virgule de la
    // phrase avalée → « □, ∀x… » converti). Remplace l'ancien « /2 → □/2 gardé »
    // (commit 1a44dc5). ADR 2026-10-27-Fix-no-leading-left-hole.
    [Theory]
    [InlineData("/2")]
    [InlineData("/ x")]
    [InlineData("* x")]
    [InlineData(", x")]
    [InlineData(", V x in R/{3}")]
    [InlineData(": c'")]
    [InlineData("; x = 2")]
    public void Leading_non_relation_infix_yields_no_reading(string input)
    {
        var r = ForestEngine.Analyze(input);
        Assert.Equal("erreur", r.Decision);
        Assert.Empty(r.Ranked);
    }

    [Theory]
    [InlineData("-x", "-x")]
    [InlineData("+ 3", "+3")]
    [InlineData("^a", "\\hat{a}")]
    public void Leading_unary_sign_or_hat_unchanged(string input, string latex)
    {
        // « + », « - » et « ^ » ont une forme unaire (signe, chapeau) : pas un trou.
        var r = ForestEngine.Analyze(input);
        Assert.Equal(latex, r.Ranked[0].Latex);
    }
}
