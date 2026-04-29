using System;
using System.Globalization;

namespace MathCursor
{
    /// <summary>
    /// Internationalisation FR/EN minimaliste pour les libellés UI de l'add-in.
    /// Stratégie : pas de Resources.resx (overhead VSTO + redéploiement),
    /// dictionnaires inline pour l'instant. La langue est détectée au premier
    /// accès : Application.LanguageSettings.LanguageID (ID 1036 = fr-FR) avec
    /// fallback sur CurrentUICulture.TwoLetterISOLanguageName.
    ///
    /// Tous les libellés UI passent par `Strings.X` au lieu de littéraux. Pour
    /// ajouter une langue : compléter le switch et la table.
    /// Cf. brief 2026-04-29-ribbon-i18n-menu-help.md.
    /// </summary>
    internal static class Strings
    {
        private static string _lang;

        /// <summary>Langue active : "fr" ou "en". Détectée au 1er appel, cachée.</summary>
        public static string Lang
        {
            get
            {
                if (_lang != null) return _lang;
                _lang = DetectLang();
                return _lang;
            }
        }

        private static string DetectLang()
        {
            // 1) Word LanguageSettings (priorité) : ID 1036 = fr-FR
            try
            {
                var langId = Globals.ThisAddIn?.Application?.LanguageSettings?
                    .LanguageID[Microsoft.Office.Core.MsoAppLanguageID.msoLanguageIDUI];
                if (langId == 1036) return "fr";
                if (langId == 1033) return "en";
                // Autres locales : fallback sur prefix CultureInfo
            }
            catch { /* Word non dispo, fallback */ }

            // 2) CultureInfo du process (Windows display lang)
            try
            {
                var iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (iso == "fr") return "fr";
            }
            catch { }

            // 3) Default EN (bonne lingua franca pour profs hors France)
            return "en";
        }

        // ---------- Ribbon ----------

        public static string GroupLabel(string version) => Lang switch
        {
            "fr" => $"MathCursor v{version}",
            _    => $"MathCursor v{version}",
        };

        public static string ReportButtonLabel => Lang switch
        {
            "fr" => "Signaler un souci",
            _    => "Report an issue",
        };

        public static string ReportButtonScreentip => Lang switch
        {
            "fr" => "Prépare un rapport (log + screenshot + contexte) prêt à envoyer",
            _    => "Builds a report (log + screenshot + context) ready to send",
        };

        public static string AboutButtonLabel => Lang switch
        {
            "fr" => "Aide",
            _    => "Help",
        };

        public static string AboutButtonScreentip => Lang switch
        {
            "fr" => "Guide rapide MathCursor",
            _    => "MathCursor quick guide",
        };

        // ---------- Dialog Aide ----------

        public static string HelpDialogTitle => Lang switch
        {
            "fr" => "MathCursor — Aide",
            _    => "MathCursor — Help",
        };

        public static string HelpDialogBody(string version) => Lang switch
        {
            "fr" =>
                "MathCursor — Notation math au clavier pour Word\n" +
                $"Version {version} — beta\n\n" +
                "COMMENT ÇA MARCHE\n" +
                "  Tape simplement ton expression en texte (ex: f(x)=1/x, somme de k=1 à n, lim x→0).\n" +
                "  Quand MathCursor détecte de la math, une petite popup apparaît avec des propositions.\n\n" +
                "RACCOURCIS\n" +
                "  Ctrl+Espace  → forcer la popup sur ce que tu viens de taper\n" +
                "                 (utile si rien ne s'est ouvert tout seul)\n" +
                "                 Ctrl+Espace répété étend la zone vers la gauche\n" +
                "  Flèche bas   → entrer dans la popup et naviguer\n" +
                "  Flèche haut  → remonter dans la liste\n" +
                "  Entrée       → valider la proposition sélectionnée (en mode nav)\n" +
                "  Échap        → masquer la popup\n" +
                "  Clic souris  → clic sur une alt = la résoudre, clic sur la finale = commit\n\n" +
                "REVENIR SUR UNE ÉQUATION\n" +
                "  Replace ton curseur DANS une équation déjà insérée : la popup se rouvre avec\n" +
                "  les variantes. Valide pour remplacer, ou clique ailleurs pour garder.\n\n" +
                "UN SOUCI ? UNE IDÉE ?\n" +
                "  Bouton \"Signaler un souci\" à gauche → génère un rapport prêt à envoyer\n" +
                "  (WhatsApp ou email). Ton feedback fait avancer le produit !\n\n" +
                "Logs techniques : %AppData%\\MathCursor\\logs\\mathcursor.log",
            _ =>
                "MathCursor — Math notation at the keyboard for Word\n" +
                $"Version {version} — beta\n\n" +
                "HOW IT WORKS\n" +
                "  Just type your expression in text (e.g. f(x)=1/x, sum k=1 to n, lim x→0).\n" +
                "  When MathCursor detects math, a small popup appears with suggestions.\n\n" +
                "SHORTCUTS\n" +
                "  Ctrl+Space   → force popup on what you just typed\n" +
                "                 (useful if nothing opened automatically)\n" +
                "                 Repeated Ctrl+Space extends the zone leftward\n" +
                "  Down arrow   → enter the popup and navigate\n" +
                "  Up arrow     → go up in the list\n" +
                "  Enter        → confirm the selected suggestion (nav mode)\n" +
                "  Escape       → hide the popup\n" +
                "  Mouse click  → click an alt to resolve it, click the final to commit\n\n" +
                "EDITING AN EQUATION\n" +
                "  Place your cursor INSIDE an existing equation: the popup reopens with\n" +
                "  variants. Confirm to replace, or click elsewhere to keep.\n\n" +
                "ISSUE? IDEA?\n" +
                "  \"Report an issue\" button on the left → generates a ready-to-send report\n" +
                "  (WhatsApp or email). Your feedback drives the product forward!\n\n" +
                "Technical logs: %AppData%\\MathCursor\\logs\\mathcursor.log",
        };

        // ---------- Dialog Report ----------

        public static string ReportFailedTitle => Lang switch
        {
            "fr" => "MathCursor — Signaler un souci",
            _    => "MathCursor — Report an issue",
        };

        public static string ReportFailedBody(string contactEmail) => Lang switch
        {
            "fr" =>
                "Impossible de créer le rapport.\n" +
                $"Envoie-nous un message à {contactEmail} en décrivant ce qui s'est passé.",
            _ =>
                "Could not create the report.\n" +
                $"Send us a message at {contactEmail} describing what happened.",
        };

        public static string ReportReadyTitle => Lang switch
        {
            "fr" => "MathCursor — Rapport prêt",
            _    => "MathCursor — Report ready",
        };

        public static string ReportReadyBody(string whatsAppUrl, string contactEmail, string zipPath) => Lang switch
        {
            "fr" =>
                "Le rapport est prêt !\n\n" +
                "Fichier copié dans le presse-papier — colle-le (Ctrl+V) dans :\n" +
                $"  • Le groupe WhatsApp beta-testeurs :\n    {whatsAppUrl}\n" +
                $"  • Ou un email à {contactEmail}\n\n" +
                "Ajoute un petit mot : ce que tu voulais faire, ce que l'add-in a fait à la place.\n\n" +
                $"Chemin du fichier : {zipPath}\n\n" +
                "Ouvrir le groupe WhatsApp dans le navigateur ?",
            _ =>
                "Report is ready!\n\n" +
                "File copied to clipboard — paste it (Ctrl+V) in:\n" +
                $"  • The WhatsApp beta testers group:\n    {whatsAppUrl}\n" +
                $"  • Or an email to {contactEmail}\n\n" +
                "Add a quick note: what you wanted to do, what the add-in did instead.\n\n" +
                $"File path: {zipPath}\n\n" +
                "Open the WhatsApp group in the browser?",
        };

        // ---------- Helper version ----------

        /// <summary>Format de version standard "Major.Minor.Patch" depuis l'AssemblyVersion.</summary>
        public static string FormatVersion(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
