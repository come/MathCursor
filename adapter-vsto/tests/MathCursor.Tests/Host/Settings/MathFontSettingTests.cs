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

using System;
using System.IO;
using MathCursor.Host;
using MathCursor.Host.Settings;
using Xunit;

namespace MathCursor.Tests.Host.Settings
{
    /// <summary>
    /// Catalogue de polices math (liste curatée stable) + round-trip de la clé
    /// <c>math_font</c> dans settings.json (ADR 2026-06-22-Feat-math-font-selector).
    /// </summary>
    [Collection("SettingsStore")] // statique SettingsStore.FilePath partagé → pas de //
    public sealed class MathFontSettingTests : IDisposable
    {
        private readonly string _path;

        public MathFontSettingTests()
        {
            _path = Path.Combine(Path.GetTempPath(),
                "mc-mathfont-" + Guid.NewGuid().ToString("N") + ".json");
            SettingsStore.FilePath = _path; // repointe + vide le cache
        }

        public void Dispose()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        }

        // ── Catalogue curaté ────────────────────────────────────────────

        [Fact]
        public void Catalogue_contient_les_fontes_demandees()
        {
            Assert.Contains("Cambria Math", MathFontCatalog.Fonts);
            Assert.Contains("Latin Modern Math", MathFontCatalog.Fonts);
            Assert.Contains("STIX Two Math", MathFontCatalog.Fonts);
        }

        [Fact]
        public void Cambria_est_en_tete_et_defaut()
        {
            Assert.Equal("Cambria Math", MathFontCatalog.Fonts[0]);
            Assert.Equal("Cambria Math", MathFontCatalog.Default);
            Assert.True(MathFontCatalog.IsDefault("Cambria Math"));
            Assert.True(MathFontCatalog.IsDefault(null));
            Assert.True(MathFontCatalog.IsDefault(""));
            Assert.False(MathFontCatalog.IsDefault("STIX Two Math"));
        }

        [Theory]
        [InlineData("Cambria Math", 0)]
        [InlineData("Latin Modern Math", 1)]
        [InlineData("STIX Two Math", 2)]
        [InlineData("cambria math", 0)]   // insensible à la casse
        [InlineData("Inconnue", 0)]       // repli défaut
        [InlineData(null, 0)]
        public void IndexOf_resout_ou_retombe_sur_le_defaut(string font, int expected)
        {
            Assert.Equal(expected, MathFontCatalog.IndexOf(font));
        }

        [Fact]
        public void DownloadUrl_pour_les_fontes_a_installer_sinon_null()
        {
            Assert.Null(MathFontCatalog.DownloadUrl("Cambria Math")); // fourni par Word
            Assert.Null(MathFontCatalog.DownloadUrl(null));
            Assert.Null(MathFontCatalog.DownloadUrl("Inconnue"));
            Assert.StartsWith("https://", MathFontCatalog.DownloadUrl("Latin Modern Math"));
            Assert.StartsWith("https://", MathFontCatalog.DownloadUrl("STIX Two Math"));
        }

        // ── Persistance settings ────────────────────────────────────────

        [Fact]
        public void Defaut_est_null()
        {
            Assert.Null(new AppSettings().MathFont);
        }

        [Fact]
        public void Clone_propage_la_police()
        {
            var s = new AppSettings { MathFont = "STIX Two Math" };
            Assert.Equal("STIX Two Math", s.Clone().MathFont);
        }

        [Theory]
        [InlineData("Latin Modern Math")]
        [InlineData("STIX Two Math")]
        [InlineData("Cambria Math")]
        public void RoundTrip_save_then_load(string font)
        {
            var s = new AppSettings { MathFont = font };
            Assert.True(SettingsStore.Save(s));

            SettingsStore.FilePath = _path; // vide le cache → relecture disque
            Assert.Equal(font, SettingsStore.Current.MathFont);
        }

        [Fact]
        public void Police_nulle_nest_pas_serialisee()
        {
            Assert.True(SettingsStore.Save(new AppSettings { MathFont = null }));
            var json = File.ReadAllText(_path);
            Assert.DoesNotContain("math_font", json);
        }

        [Fact]
        public void Cle_absente_dans_un_fichier_anterieur_vaut_null()
        {
            // settings.json d'une version pré-sélecteur : la clé n'existe pas.
            File.WriteAllText(_path,
                "{\"v\":1,\"culture\":\"fr\",\"auto_detect\":true,\"tab_validate\":false}");
            SettingsStore.FilePath = _path;

            Assert.Null(SettingsStore.Current.MathFont);
        }

        [Fact]
        public void Serialisation_contient_la_cle_si_non_nulle()
        {
            Assert.True(SettingsStore.Save(new AppSettings { MathFont = "Latin Modern Math" }));
            var json = File.ReadAllText(_path);
            Assert.Contains("\"math_font\":\"Latin Modern Math\"", json);
        }
    }
}
