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

using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Pause de la détection auto par document (ADR 2026-10-14-UX-escape-
    /// pauses-autodetect) : Échap pause le document actif, Ctrl+Espace la lève.
    /// </summary>
    public sealed class AutoDetectPauseTests
    {
        private const string Histoire = @"C:\Cours\histoire-geo.docx";
        private const string Maths = @"C:\Cours\maths.docx";

        [Fact]
        public void Pause_puis_reprise_du_meme_document()
        {
            var p = new AutoDetectPause();
            Assert.False(p.IsPaused(Histoire));

            Assert.True(p.Pause(Histoire));
            Assert.True(p.IsPaused(Histoire));

            Assert.True(p.Resume(Histoire));
            Assert.False(p.IsPaused(Histoire));
        }

        [Fact]
        public void La_pause_ne_touche_pas_les_autres_documents()
        {
            var p = new AutoDetectPause();
            p.Pause(Histoire);
            Assert.False(p.IsPaused(Maths));
        }

        [Fact]
        public void Second_echap_et_reprise_sans_pause_ne_signalent_rien()
        {
            var p = new AutoDetectPause();
            Assert.False(p.Resume(Histoire));       // Ctrl+Espace sans pause : rien à lever
            Assert.True(p.Pause(Histoire));
            Assert.False(p.Pause(Histoire));        // déjà en pause : pas de 2e message
        }

        [Fact]
        public void Cle_insensible_a_la_casse_du_chemin()
        {
            var p = new AutoDetectPause();
            p.Pause(Histoire);
            Assert.True(p.IsPaused(Histoire.ToUpperInvariant()));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Cle_absente_ignoree(string key)
        {
            var p = new AutoDetectPause();
            Assert.False(p.Pause(key));
            Assert.False(p.IsPaused(key));
            Assert.False(p.Resume(key));
        }
    }
}
