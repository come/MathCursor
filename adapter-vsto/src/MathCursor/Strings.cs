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

        /// <summary>Label du group MathCursor dans l'onglet Accueil de Word
        /// (cf. ADR 2026-05-06-Feat-ribbon-pane-examples-pivot).</summary>
        public static string ToolsGroupLabel(string version) => Lang switch
        {
            "fr" => $"MathCursor — v{version}",
            _    => $"MathCursor — v{version}",
        };

        // ---------- Onglet dédié + duo TabHome (ADR 2026-05-11) ----------

        public static string HomeGroupLabel(string version) => Lang switch
        {
            "fr" => $"MathCursor v{version}",
            _    => $"MathCursor v{version}",
        };

        public static string MathCursorTabLabel => Lang switch
        {
            "fr" => "MathCursor",
            _    => "MathCursor",
        };

        public static string InputGroupLabel => Lang switch
        {
            "fr" => "Saisie",
            _    => "Input",
        };

        public static string LayoutGroupLabel => Lang switch
        {
            "fr" => "Mise en page",
            _    => "Layout",
        };

        public static string ConstructionsGroupLabel => Lang switch
        {
            "fr" => "Constructions",
            _    => "Constructions",
        };

        public static string ToolsTabGroupLabel => Lang switch
        {
            "fr" => "Outils",
            _    => "Tools",
        };

        public static string ColumnsMenuLabel => Lang switch
        {
            "fr" => "Colonnes",
            _    => "Columns",
        };

        public static string ColumnsMenuScreentip => Lang switch
        {
            "fr" => "Insère un tableau N colonnes (barres séparatrices visibles, pas de bordures externes).",
            _    => "Insert an N-column table (visible separator bars, no outer borders).",
        };

        public static string Columns1Label => Lang switch
        {
            "fr" => "1 colonne",
            _    => "1 column",
        };

        public static string Columns2Label => Lang switch
        {
            "fr" => "2 colonnes",
            _    => "2 columns",
        };

        public static string Columns3Label => Lang switch
        {
            "fr" => "3 colonnes",
            _    => "3 columns",
        };

        public static string Columns4Label => Lang switch
        {
            "fr" => "4 colonnes",
            _    => "4 columns",
        };

        public static string CheatsheetButtonLabel => Lang switch
        {
            "fr" => "Exemples",
            _    => "Examples",
        };

        public static string CheatsheetButtonScreentip => Lang switch
        {
            "fr" => "Galerie d'exemples concrets multi-syntaxes (à venir).",
            _    => "Concrete multi-syntax examples gallery (coming).",
        };

        public static string ConstructionSignTableLabel => Lang switch
        {
            "fr" => "Tableau de signe",
            _    => "Sign table",
        };

        public static string ConstructionVariationTableLabel => Lang switch
        {
            "fr" => "Tableau de variation",
            _    => "Variation table",
        };

        public static string ConstructionCurveLabel => Lang switch
        {
            "fr" => "Courbe",
            _    => "Curve",
        };

        public static string ConstructionFigureLabel => Lang switch
        {
            "fr" => "Figure",
            _    => "Figure",
        };

        public static string ConstructionComingSoonScreentip => Lang switch
        {
            "fr" => "À venir — roadmap v0.6+",
            _    => "Coming soon — roadmap v0.6+",
        };

        public static string SettingsButtonLabel => Lang switch
        {
            "fr" => "Paramètres",
            _    => "Settings",
        };

        public static string SettingsButtonScreentip => Lang switch
        {
            "fr" => "Préférences MathCursor : culture (FR/US), séparateur d'intervalle, affichage des matrices.",
            _    => "MathCursor preferences: culture (FR/US), interval separator, matrix display.",
        };

        public static string AutoDetectToggleLabel => Lang switch
        {
            "fr" => "Détection auto",
            _    => "Auto detection",
        };

        public static string AutoDetectToggleScreentip => Lang switch
        {
            "fr" => "Activée : MathCursor propose tout seul pendant la frappe. Désactivée (cours sans maths) : plus aucune popup spontanée — Ctrl+Espace reste disponible pour convertir à la demande.",
            _    => "On: MathCursor makes suggestions as you type. Off (no-math classes): no more spontaneous popup — Ctrl+Space stays available to convert on demand.",
        };

        public static string TabValidateToggleLabel => Lang switch
        {
            "fr" => "Tab valide",
            _    => "Tab confirms",
        };

        public static string TabValidateToggleScreentip => Lang switch
        {
            "fr" => "Activé : quand la popup est ouverte, Tab insère la proposition sélectionnée (la première par défaut) au lieu d'une tabulation. Désactivé : Tab reste une tabulation Word normale.",
            _    => "On: when the popup is open, Tab inserts the selected suggestion (the first by default) instead of a tabulation. Off: Tab stays a normal Word tabulation.",
        };

        public static string SettingsTabValidateLabel => Lang switch
        {
            "fr" => "Tab valide la proposition quand la popup est ouverte",
            _    => "Tab confirms the suggestion when the popup is open",
        };

        // ---------- Fenêtre Paramètres (SettingsWindow.cs, ADR 2026-06-10) ----------

        public static string SettingsWindowTitle => Lang switch
        {
            "fr" => "MathCursor — Paramètres",
            _    => "MathCursor — Settings",
        };

        public static string SettingsIntro => Lang switch
        {
            "fr" => "La culture choisit les défauts de notation. Tu peux ajuster chaque réglage individuellement — un réglage non modifié suit la culture.",
            _    => "The culture sets the notation defaults. You can adjust each setting individually — an unmodified setting follows the culture.",
        };

        public static string SettingsSectionCulture => Lang switch
        {
            "fr" => "Culture",
            _    => "Culture",
        };

        public static string SettingsCultureFr => Lang switch
        {
            "fr" => "Français (FR) — virgule décimale, intervalles [0;1], matrices ( )",
            _    => "French (FR) — decimal comma, intervals [0;1], matrices ( )",
        };

        public static string SettingsCultureUs => Lang switch
        {
            "fr" => "Anglais (US) — point décimal, intervalles [0,1], matrices [ ]",
            _    => "English (US) — decimal point, intervals [0,1], matrices [ ]",
        };

        public static string SettingsSectionNotation => Lang switch
        {
            "fr" => "Notation",
            _    => "Notation",
        };

        public static string SettingsIntervalSepLabel => Lang switch
        {
            "fr" => "Séparateur d'intervalle",
            _    => "Interval separator",
        };

        public static string SettingsMatrixLabel => Lang switch
        {
            "fr" => "Visualisation des matrices",
            _    => "Matrix display",
        };

        public static string SettingsMatrixParens => Lang switch
        {
            "fr" => "( … )  parenthèses",
            _    => "( … )  parentheses",
        };

        public static string SettingsMatrixBrackets => Lang switch
        {
            "fr" => "[ … ]  crochets",
            _    => "[ … ]  brackets",
        };

        public static string SettingsInheritedHint => Lang switch
        {
            "fr" => "suit la culture",
            _    => "follows the culture",
        };

        public static string SettingsOverrideHint => Lang switch
        {
            "fr" => "personnalisé",
            _    => "customized",
        };

        public static string SettingsPreviewLabel => Lang switch
        {
            "fr" => "Aperçu",
            _    => "Preview",
        };

        public static string SettingsButtonSave => Lang switch
        {
            "fr" => "Enregistrer",
            _    => "Save",
        };

        public static string SettingsButtonCancel => Lang switch
        {
            "fr" => "Annuler",
            _    => "Cancel",
        };

        public static string SettingsSaveFailed => Lang switch
        {
            "fr" => "Impossible d'écrire le fichier de réglages — les changements s'appliquent pour cette session seulement.",
            _    => "Could not write the settings file — changes apply to this session only.",
        };

        public static string AboutButtonLabel => Lang switch
        {
            "fr" => "À propos",
            _    => "About",
        };

        public static string AboutButtonScreentip => Lang switch
        {
            "fr" => "Version, raccourcis, aide.",
            _    => "Version, shortcuts, help.",
        };

        public static string ReportButtonLabel => Lang switch
        {
            "fr" => "Signaler un souci",
            _    => "Report an issue",
        };

        // Bouton + pane debug Context Inspector (cf. brief 2026-05-07-global-context-multi-zoom-ranking).
        public static string ContextInspectorButtonLabel => Lang switch
        {
            "fr" => "Inspecteur",
            _    => "Inspector",
        };

        public static string ContextInspectorButtonScreentip => Lang switch
        {
            "fr" => "Ouvre/ferme le panneau debug du contexte de résolution (raw source, sidecar, scoring hints, trace)",
            _    => "Open/close the resolution context debug panel (raw source, sidecar, scoring hints, trace)",
        };

        public static string ContextInspectorPaneTitle => Lang switch
        {
            "fr" => "MathCursor — Inspecteur (debug)",
            _    => "MathCursor — Inspector (debug)",
        };

        public static string ReportButtonScreentip => Lang switch
        {
            "fr" => "Prépare un rapport (log + screenshot + contexte) prêt à envoyer",
            _    => "Builds a report (log + screenshot + context) ready to send",
        };

        // ---------- Examples pane (cf. ADR 06-05 ribbon-pane-examples-pivot) ----------

        public static string ExamplesPaneTitle => Lang switch
        {
            "fr" => "MathCursor — Exemples",
            _    => "MathCursor — Examples",
        };

        public static string ExamplesMissingButton => Lang switch
        {
            "fr" => "Cliquez sur le bouton Exemples du ruban pour ouvrir le panneau.",
            _    => "Click the Examples button on the ribbon to open the panel.",
        };

        public static string ExamplesNoMatch => Lang switch
        {
            "fr" => "Aucun exemple ne correspond à votre recherche.",
            _    => "No example matches your search.",
        };

        public static string ExamplesEntryTypeLabel => Lang switch
        {
            "fr" => "Tapez :",
            _    => "Type:",
        };

        public static string ExamplesEntryRenderLabel => Lang switch
        {
            "fr" => "Vous obtenez :",
            _    => "You get:",
        };

        // ---------- Dialog Aide (legacy, à dégager quand le pane Cheatsheet aura absorbé le contenu) ----------

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
                "UN SOUCI ? UNE IDÉE ?\n" +
                "  Bouton \"Signaler un souci\" à gauche → génère un rapport prêt à envoyer\n" +
                "  (WhatsApp ou email). Ton feedback fait avancer le produit !\n\n" +
                "Logs techniques : %AppData%\\MathCursor\\logs\\mathcursor.log\n\n" +
                "MERCI AUX TESTEURS ET CONTRIBUTEURS\n" +
                "  V. de Salaberry — Collège Le Sacré-Cœur, Vannes, France",
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
                "ISSUE? IDEA?\n" +
                "  \"Report an issue\" button on the left → generates a ready-to-send report\n" +
                "  (WhatsApp or email). Your feedback drives the product forward!\n\n" +
                "Technical logs: %AppData%\\MathCursor\\logs\\mathcursor.log\n\n" +
                "THANKS TO OUR TESTERS AND CONTRIBUTORS\n" +
                "  V. de Salaberry — Collège Le Sacré-Cœur, Vannes, France",
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

        // ---------- Feedback dialog (FeedbackDialog.cs) ----------

        public static string FeedbackTitle => Lang switch
        {
            "fr" => "MathCursor — Signaler une erreur",
            _    => "MathCursor — Report an issue",
        };

        public static string FeedbackHeader => Lang switch
        {
            "fr" => "Tu as rencontré un souci ?",
            _    => "Ran into an issue?",
        };

        public static string FeedbackIntro => Lang switch
        {
            "fr" => "Vérifie les infos ci-dessous et explique ce qui ne va pas. On lit tout, ça nous aide.",
            _    => "Check the info below and tell us what's wrong. We read everything, it helps a lot.",
        };

        public static string FeedbackSectionLastAction => Lang switch
        {
            "fr" => "Dernière action",
            _    => "Latest action",
        };

        public static string FeedbackSectionDescribe => Lang switch
        {
            "fr" => "Décris le souci",
            _    => "Describe the issue",
        };

        public static string FeedbackLabelWhatTyped => Lang switch
        {
            "fr" => "Ce que tu as tapé",
            _    => "What you typed",
        };

        public static string FeedbackLabelWhatProposed => Lang switch
        {
            "fr" => "Ce que MathCursor a proposé",
            _    => "What MathCursor proposed",
        };

        public static string FeedbackLabelWhatInserted => Lang switch
        {
            "fr" => "Ce qui (serait) inséré dans Word",
            _    => "What (would be) inserted in Word",
        };

        public static string FeedbackToggleScreenshot => Lang switch
        {
            "fr" => "Joindre une capture d'écran (recommandé)",
            _    => "Include a screenshot (recommended)",
        };

        public static string FeedbackToggleLog => Lang switch
        {
            "fr" => "Joindre les 64 derniers Ko de log technique",
            _    => "Include the last 64 KB of technical log",
        };

        public static string FeedbackDisclaimerPart1 => Lang switch
        {
            "fr" => "Ces données partent vers notre serveur (Cloudflare). Pas de doc entier, pas d'identifiant. ",
            _    => "This data is sent to our server (Cloudflare). No full doc, no identifier. ",
        };

        public static string FeedbackDisclaimerLink => Lang switch
        {
            "fr" => "Détails",
            _    => "Details",
        };

        public static string FeedbackButtonCancel => Lang switch
        {
            "fr" => "Annuler",
            _    => "Cancel",
        };

        public static string FeedbackButtonSend => Lang switch
        {
            "fr" => "Envoyer",
            _    => "Send",
        };

        public static string FeedbackAltActionPrefix => Lang switch
        {
            "fr" => "Pas de réseau ? ",
            _    => "No network? ",
        };

        public static string FeedbackAltActionLink => Lang switch
        {
            "fr" => "Copier dans un mail à la place",
            _    => "Copy to an email instead",
        };

        public static string FeedbackValidationEmpty => Lang switch
        {
            "fr" => "Décris au moins ce qui ne va pas dans le champ commentaire.",
            _    => "Describe at least what's wrong in the comment field.",
        };

        public static string FeedbackStatusSending => Lang switch
        {
            "fr" => "Envoi en cours...",
            _    => "Sending...",
        };

        public static string FeedbackStatusSent => Lang switch
        {
            "fr" => "Merci ! Ton retour a été envoyé.",
            _    => "Thanks! Your feedback was sent.",
        };

        public static string FeedbackStatusSendFailed(string detail) => Lang switch
        {
            "fr" => $"Envoi impossible : {detail}\nBascule sur l'envoi par mail...",
            _    => $"Send failed: {detail}\nFalling back to email send...",
        };

        public static string FeedbackStatusMailCopied => Lang switch
        {
            "fr" => "Texte copié, colle-le (Ctrl+V) dans le mail qui vient de s'ouvrir.",
            _    => "Text copied, paste it (Ctrl+V) in the mail that just opened.",
        };

        public static string FeedbackStatusMailFailed(string detail) => Lang switch
        {
            "fr" => $"Impossible d'ouvrir le client mail. Le rapport est dans le presse-papier (Ctrl+V). {detail}",
            _    => $"Could not open mail client. The report is in the clipboard (Ctrl+V). {detail}",
        };

        public static string FeedbackMailtoSubject(string version) => Lang switch
        {
            "fr" => $"MathCursor — rapport ({version})",
            _    => $"MathCursor — report ({version})",
        };

        /// <summary>Corps texte mis dans le presse-papier, à coller dans
        /// le mail. Markdown léger pour rester lisible côté client mail.</summary>
        public static string FeedbackMailBody(
            string version, DateTimeOffset ts, string wordVersion, string osVersion,
            string sourceText, string proposedLatex, string committedLatex,
            string userComment, string paragraphContext) => Lang switch
        {
            "fr" =>
                "=== MathCursor — Rapport de souci ===\n\n" +
                $"Version : {version}\n" +
                $"Date    : {ts:yyyy-MM-dd HH:mm:ss zzz}\n" +
                $"Word    : {wordVersion}\n" +
                $"OS      : {osVersion}\n\n" +
                "--- Ce que j'ai tapé ---\n" + sourceText + "\n\n" +
                "--- Ce que MathCursor a proposé ---\n" + proposedLatex + "\n\n" +
                "--- Ce qui (serait) inséré dans Word ---\n" +
                (string.IsNullOrEmpty(committedLatex) ? "(rien — pas de commit)" : committedLatex) + "\n\n" +
                "--- Mon explication ---\n" + userComment +
                (string.IsNullOrEmpty(paragraphContext) ? "" :
                    "\n\n--- Paragraphe Word ---\n" + paragraphContext),
            _ =>
                "=== MathCursor — Issue report ===\n\n" +
                $"Version : {version}\n" +
                $"Date    : {ts:yyyy-MM-dd HH:mm:ss zzz}\n" +
                $"Word    : {wordVersion}\n" +
                $"OS      : {osVersion}\n\n" +
                "--- What I typed ---\n" + sourceText + "\n\n" +
                "--- What MathCursor proposed ---\n" + proposedLatex + "\n\n" +
                "--- What (would be) inserted in Word ---\n" +
                (string.IsNullOrEmpty(committedLatex) ? "(nothing — no commit)" : committedLatex) + "\n\n" +
                "--- My explanation ---\n" + userComment +
                (string.IsNullOrEmpty(paragraphContext) ? "" :
                    "\n\n--- Word paragraph ---\n" + paragraphContext),
        };

        // ---------- Helper version ----------

        /// <summary>Format de version standard "Major.Minor.Patch" depuis l'AssemblyVersion.</summary>
        public static string FormatVersion(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";

        // ---------- Ribbon Phase 2 beta-clean (ADR 2026-06-10) ----------

        public static string ConversionGroupLabel => Lang switch
        {
            "fr" => "Conversion",
            _    => "Conversion",
        };

        public static string HelpGroupLabel => Lang switch
        {
            "fr" => "Aide",
            _    => "Help",
        };

        public static string ConvertButtonLabel => Lang switch
        {
            "fr" => "Convertir",
            _    => "Convert",
        };

        public static string ConvertButtonScreentip => Lang switch
        {
            "fr" => "Convertit le texte tapé avant le curseur en équation (Ctrl+Espace). Ré-appuyer étend la zone vers la gauche.",
            _    => "Converts the text typed before the caret into an equation (Ctrl+Space). Press again to extend the zone leftwards.",
        };

        public static string SettingsSectionDetection => Lang switch
        {
            "fr" => "Détection",
            _    => "Detection",
        };

        public static string SettingsAutoDetectLabel => Lang switch
        {
            "fr" => "Proposer automatiquement pendant la frappe (le raccourci Ctrl+Espace reste toujours actif)",
            _    => "Suggest automatically while typing (the Ctrl+Space shortcut always stays active)",
        };

        // ---------- Bouton « Réouvrir le tutoriel » (groupe Aide) ----------

        public static string TutorialButtonLabel => Lang switch
        {
            "fr" => "Réouvrir le tutoriel",
            _    => "Reopen the tutorial",
        };

        public static string TutorialButtonScreentip => Lang switch
        {
            "fr" => "Rouvre le document tutoriel de prise en main (Documents\\MathCursor).",
            _    => "Reopens the getting-started tutorial document (Documents\\MathCursor).",
        };

        public static string TutorialMissingTitle => Lang switch
        {
            "fr" => "MathCursor — Tutoriel",
            _    => "MathCursor — Tutorial",
        };

        public static string TutorialMissingBody(string path) => Lang switch
        {
            "fr" =>
                "Le document tutoriel est introuvable :\n" + path + "\n\n" +
                "Il a peut-être été déplacé ou supprimé. Réinstaller MathCursor le remettra en place.",
            _ =>
                "The tutorial document could not be found:\n" + path + "\n\n" +
                "It may have been moved or deleted. Reinstalling MathCursor will restore it.",
        };

        public static string ConvertNothingRecognized => Lang switch
        {
            "fr" => "MathCursor : aucune notation mathématique reconnue ici.",
            _    => "MathCursor: no mathematical notation recognized here.",
        };
    }
}
