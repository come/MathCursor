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

using System.IO;
using System.Media;
using System.Reflection;
using MathCursor.Host.Settings;

namespace MathCursor.Host.Sound
{
    /// <summary>
    /// Petit son optionnel : un « bubble pop » à la
    /// validation d'une formule (le son d'ouverture de popup a été retiré le 2026-10-26).
    /// OFF par défaut (<see cref="AppSettings.PlaySounds"/>) : chaque méthode
    /// sort immédiatement si le réglage est coupé, donc coût nul en usage réel.
    /// Le WAV est une ressource embarquée (rognée au silence près, attaque
    /// immédiate), chargée UNE fois dans un player réutilisé puis jouée via
    /// <see cref="SoundPlayer"/> (thread de fond, non bloquant). Tout est
    /// enveloppé dans un try/catch avale-tout : un son ne doit JAMAIS casser
    /// l'UX ni la conversion. Cf. ADR 2026-07-05-Feat-optional-popup-sounds.
    /// </summary>
    internal static class SoundEffects
    {
        private const string ValidateResource = "MathCursor.Sounds.validate.wav";

        // Player préchargé et réutilisé : pas de lecture/analyse du WAV au moment du
        // geste (latence perceptible sinon). Garde aussi la référence vivante pendant
        // la lecture (Play() joue sur un thread de fond).
        private static SoundPlayer _validate;

        /// <summary>« Bubble pop » de confirmation, joué à la validation d'une formule.</summary>
        public static void PlayValidate()
        {
            if (!SettingsStore.Current.PlaySounds) return;
            try
            {
                if (_validate == null)
                {
                    var wav = LoadResource(ValidateResource);
                    if (wav == null) return;
                    _validate = new SoundPlayer(new MemoryStream(wav));
                    _validate.Load();
                }
                _validate.Play(); // asynchrone (thread de fond)
            }
            catch { /* un son ne casse jamais l'UX */ }
        }

        private static byte[] LoadResource(string logicalName)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream(logicalName))
                {
                    if (s == null) return null;
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch { return null; }
        }
    }
}
