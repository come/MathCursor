// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
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

using MathCursor.Host.Detection;
using Xunit;

namespace MathCursor.Tests.Host.Detection
{
    /// <summary>
    /// <see cref="ZoneEngineText.WithTrailingSpaceSignal"/> (= le calcul pur
    /// derrière <c>ZoneSpan.TextForEngine</c>) — l'espace final tapé est un
    /// signal pour le lexer (étoile postfixe « R*␣ » vs multiplication
    /// incomplète « R* ») : les bornes restent trimées mais le texte envoyé
    /// au moteur doit le restituer (ADR 2026-06-10-Feat-culture-scoped-aliases).
    /// </summary>
    public sealed class ZoneSpanTests
    {
        [Fact]
        public void Espace_apres_la_zone_est_restitue()
        {
            // "V x app R* " : bornes trimées [0,10), l'espace en 10 doit revenir.
            Assert.Equal("V x app R* ",
                ZoneEngineText.WithTrailingSpaceSignal("V x app R*", "V x app R* ", 10));
        }

        [Fact]
        public void Sans_espace_texte_inchange()
        {
            Assert.Equal("V x app R*",
                ZoneEngineText.WithTrailingSpaceSignal("V x app R*", "V x app R*", 10));
        }

        [Fact]
        public void Fin_de_paragraphe_sans_debordement()
        {
            // StringEnd == longueur du ¶ : pas de lecture hors bornes.
            Assert.Equal("x+1",
                ZoneEngineText.WithTrailingSpaceSignal("x+1", "x+1", 3));
        }

        [Fact]
        public void Un_seul_espace_meme_si_plusieurs()
        {
            // Le lexer n'a besoin que du signal « détaché », pas du run entier.
            Assert.Equal("R* ",
                ZoneEngineText.WithTrailingSpaceSignal("R*", "R*   toto", 2));
        }
    }
}
