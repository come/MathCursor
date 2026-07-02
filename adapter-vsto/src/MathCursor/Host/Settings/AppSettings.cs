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

using MathCursor.Engine;

namespace MathCursor.Host.Settings
{
    /// <summary>
    /// Réglages utilisateur MathCursor (ADR 2026-06-10-Feat-ribbon-columns-
    /// settings-culture). Modèle « culture + overrides nullables » : un champ
    /// à null SUIT la culture — il héritera donc des bons défauts si le preset
    /// évolue ou si de nouvelles options liées à la culture apparaissent.
    /// </summary>
    internal sealed class AppSettings
    {
        public const string CultureFr = "fr";
        public const string CultureUs = "us";

        /// <summary>Version du schéma settings.json. Incrémenter si on change la structure.</summary>
        public int V { get; set; } = 1;

        /// <summary>Culture de base : "fr" | "us".</summary>
        public string Culture { get; set; } = CultureFr;

        /// <summary>Séparateur d'intervalle ";" ou "," — null = suit la culture.</summary>
        public string IntervalSepOverride { get; set; }

        /// <summary>Env matriciel "pmatrix" ou "bmatrix" — null = suit la culture.</summary>
        public string MatrixEnvOverride { get; set; }

        /// <summary>Auto-détection NER en cours de frappe (popup spontanée).
        /// Ctrl+Espace reste actif quel que soit ce réglage. Cf. ADR
        /// 2026-06-10-Feat-ner-auto-detection-debounce.</summary>
        public bool AutoDetect { get; set; } = true;

        /// <summary>Tab valide le candidat sélectionné (= le 1er sans nav)
        /// quand la popup est ouverte, sans propager le Tab. Défaut OFF :
        /// Tab reste une tabulation Word normale. Cf. ADR
        /// 2026-06-10-UX-tab-validate-toggle.</summary>
        public bool TabValidate { get; set; }

        /// <summary>Envoi du compteur d'usage anonyme (nombre de formules
        /// converties — un entier, aucun contenu, aucun identifiant). Défaut ON,
        /// désactivable par l'utilisateur. Coupé → rien n'est compté ni envoyé.
        /// Cf. ADR 2026-06-18-Feat-usage-counter-telemetry.</summary>
        public bool SendUsageStats { get; set; } = true;

        /// <summary>Police math choisie pour les équations (fonte à table OpenType
        /// MATH) : "Cambria Math" | "Latin Modern Math" | "STIX Two Math".
        /// null = défaut Word (Cambria). Appliquée aux équations insérées par
        /// MathCursor (préréglage) et posable sur tout le doc via le menu ruban.
        /// Cf. ADR 2026-06-22-Feat-math-font-selector.</summary>
        public string MathFont { get; set; }

        /// <summary>Preset moteur de la culture de base.</summary>
        public EngineCulture BaseCulture =>
            Culture == CultureUs ? EngineCulture.Us : EngineCulture.Fr;

        /// <summary>Culture effective passée au moteur (preset + overrides).
        /// WithOverrides préserve les champs non réglables du preset (alias,
        /// et tout champ futur) sans que l'adapter ait à les connaître.</summary>
        public EngineCulture ToEngineCulture()
            => BaseCulture.WithOverrides(IntervalSepOverride, MatrixEnvOverride);

        public AppSettings Clone() => (AppSettings)MemberwiseClone();
    }
}
