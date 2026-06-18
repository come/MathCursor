using System;
using System.IO;
using MathCursor.Host.Settings;
using Xunit;

namespace MathCursor.Tests.Host.Settings
{
    /// <summary>
    /// Round-trip de la clé <c>send_usage_stats</c> dans settings.json
    /// (ADR 2026-06-18-Feat-usage-counter-telemetry). Le fichier est repointé
    /// sur un temp isolé via le seam <see cref="SettingsStore.FilePath"/>.
    /// </summary>
    public sealed class SendUsageStatsSettingTests : IDisposable
    {
        private readonly string _path;

        public SendUsageStatsSettingTests()
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
        public void Defaut_est_active()
        {
            Assert.True(new AppSettings().SendUsageStats);
        }

        [Fact]
        public void Clone_propage_le_reglage()
        {
            var s = new AppSettings { SendUsageStats = false };
            Assert.False(s.Clone().SendUsageStats);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RoundTrip_save_then_load(bool value)
        {
            var s = new AppSettings { SendUsageStats = value };
            Assert.True(SettingsStore.Save(s));

            SettingsStore.FilePath = _path; // vide le cache → force une relecture disque
            Assert.Equal(value, SettingsStore.Current.SendUsageStats);
        }

        [Fact]
        public void Cle_absente_dans_un_fichier_anterieur_vaut_true()
        {
            // settings.json d'une version pré-télémétrie : la clé n'existe pas.
            File.WriteAllText(_path,
                "{\"v\":1,\"culture\":\"fr\",\"auto_detect\":true,\"tab_validate\":false}");
            SettingsStore.FilePath = _path;

            Assert.True(SettingsStore.Current.SendUsageStats);
        }

        [Fact]
        public void Serialisation_contient_la_cle()
        {
            Assert.True(SettingsStore.Save(new AppSettings { SendUsageStats = false }));
            var json = File.ReadAllText(_path);
            Assert.Contains("\"send_usage_stats\":false", json);
        }
    }
}
