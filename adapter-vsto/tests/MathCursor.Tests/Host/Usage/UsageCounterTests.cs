using System;
using System.IO;
using MathCursor.Host.Usage;
using Xunit;

namespace MathCursor.Tests.Host.Usage
{
    /// <summary>
    /// Tests du compteur d'usage (ADR 2026-06-18-Feat-usage-counter-telemetry).
    /// Le chemin du fichier est repointé sur un temp isolé via le seam
    /// <see cref="UsageCounter.FilePath"/> (qui réinitialise aussi l'état).
    /// </summary>
    public sealed class UsageCounterTests : IDisposable
    {
        private readonly string _path;

        public UsageCounterTests()
        {
            _path = Path.Combine(Path.GetTempPath(),
                "mc-usage-" + Guid.NewGuid().ToString("N") + ".json");
            UsageCounter.FilePath = _path; // repointe + reset état mémoire
        }

        public void Dispose()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        }

        [Fact]
        public void Fichier_absent_demarre_a_zero()
        {
            Assert.Equal(0, UsageCounter.GetPending());
        }

        [Fact]
        public void Increment_accumule_et_persiste()
        {
            UsageCounter.Increment();
            UsageCounter.Increment();
            UsageCounter.Increment();

            Assert.Equal(3, UsageCounter.GetPending());
            // Persisté : un re-pointage sur le même fichier relit la valeur.
            UsageCounter.FilePath = _path;
            Assert.Equal(3, UsageCounter.GetPending());
        }

        [Fact]
        public void ClearSent_retranche_sans_passer_sous_zero()
        {
            for (int i = 0; i < 5; i++) UsageCounter.Increment();

            UsageCounter.ClearSent(3);
            Assert.Equal(2, UsageCounter.GetPending());

            UsageCounter.ClearSent(10); // plus que le restant
            Assert.Equal(0, UsageCounter.GetPending());
        }

        [Fact]
        public void ClearSent_preserve_les_increments_arrives_pendant_l_envoi()
        {
            for (int i = 0; i < 5; i++) UsageCounter.Increment();
            int sent = UsageCounter.GetPending(); // = 5, capturé "avant POST"

            UsageCounter.Increment(); // arrive "pendant le POST" → 6

            UsageCounter.ClearSent(sent); // ne retranche que ce qui a été transmis
            Assert.Equal(1, UsageCounter.GetPending());
        }

        [Fact]
        public void ClearSent_ignore_les_valeurs_non_positives()
        {
            UsageCounter.Increment();
            UsageCounter.ClearSent(0);
            UsageCounter.ClearSent(-5);
            Assert.Equal(1, UsageCounter.GetPending());
        }

        [Fact]
        public void Fichier_corrompu_retombe_a_zero_sans_lever()
        {
            File.WriteAllText(_path, "{ pas du tout du json valide ");
            UsageCounter.FilePath = _path; // force une relecture depuis ce contenu

            Assert.Equal(0, UsageCounter.GetPending());

            // Et on peut repartir proprement.
            UsageCounter.Increment();
            Assert.Equal(1, UsageCounter.GetPending());
        }

        [Fact]
        public void Valeur_negative_dans_le_fichier_est_normalisee_a_zero()
        {
            File.WriteAllText(_path, "{\"pending\":-7}");
            UsageCounter.FilePath = _path;
            Assert.Equal(0, UsageCounter.GetPending());
        }
    }
}
