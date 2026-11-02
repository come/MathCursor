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
using System.IO;
using MathCursor.Host.Settings;
using Xunit;

namespace MathCursor.Tests.Host.Settings
{
    /// <summary>
    /// Round-trip de la clé <c>play_sounds</c> dans settings.json (ADR
    /// 2026-07-05-Feat-optional-popup-sounds). Retour sonore optionnel, OFF par
    /// défaut : la clé absente d'un fichier antérieur doit rester silencieuse.
    /// Fichier repointé sur un temp isolé via <see cref="SettingsStore.FilePath"/>.
    /// </summary>
    [Collection("SettingsStore")] // statique SettingsStore.FilePath partagé → pas de //
    public sealed class PlaySoundsSettingTests : IDisposable
    {
        private readonly string _path;

        public PlaySoundsSettingTests()
        {
            _path = Path.Combine(Path.GetTempPath(),
                "mc-settings-" + Guid.NewGuid().ToString("N") + ".json");
            SettingsStore.FilePath = _path; // repointe + vide le cache
        }

        public void Dispose()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        }

        [Fact]
        public void Defaut_est_desactive()
        {
            Assert.False(new AppSettings().PlaySounds);
        }

        [Fact]
        public void Clone_propage_le_reglage()
        {
            var s = new AppSettings { PlaySounds = true };
            Assert.True(s.Clone().PlaySounds);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RoundTrip_save_then_load(bool value)
        {
            var s = new AppSettings { PlaySounds = value };
            Assert.True(SettingsStore.Save(s));

            SettingsStore.FilePath = _path; // vide le cache → force une relecture disque
            Assert.Equal(value, SettingsStore.Current.PlaySounds);
        }

        [Fact]
        public void Cle_absente_dans_un_fichier_anterieur_vaut_false()
        {
            // settings.json d'une version pré-sons : la clé n'existe pas.
            File.WriteAllText(_path,
                "{\"v\":1,\"culture\":\"fr\",\"auto_detect\":true,\"tab_validate\":false}");
            SettingsStore.FilePath = _path;

            Assert.False(SettingsStore.Current.PlaySounds);
        }

        [Fact]
        public void Serialisation_contient_la_cle()
        {
            Assert.True(SettingsStore.Save(new AppSettings { PlaySounds = true }));
            var json = File.ReadAllText(_path);
            Assert.Contains("\"play_sounds\":true", json);
        }
    }
}
