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
