// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
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

    [Fact]
    public void Leading_non_relation_infix_keeps_left_hole()
    {
        // Les infixes NON-relation gardent l'aperçu de saisie : « /2 » tapé
        // en édition → □/2 (comportement JS conservé).
        var r = ForestEngine.Analyze("/2");
        Assert.NotEqual("erreur", r.Decision);
        Assert.Contains("\\square", r.Ranked[0].Latex);
    }
}
