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

        /// <summary>Label de l'onglet quand une mise à jour est dispo (indicateur
        /// passif). Cf. ADR 2026-06-18-Feat-ribbon-update-badge.</summary>
        public static string MathCursorTabLabelUpdate => Lang switch
        {
            "fr" => "MathCursor ● MAJ",
            _    => "MathCursor ● Update",
        };

        public static string UpdateGroupLabel => Lang switch
        {
            "fr" => "Mise à jour",
            _    => "Update",
        };

        public static string UpdateButtonLabel => Lang switch
        {
            "fr" => "Mise à jour disponible",
            _    => "Update available",
        };

        public static string UpdateButtonScreentip => Lang switch
        {
            "fr" => "Une nouvelle version de MathCursor est disponible — ouvrir la page de téléchargement.",
            _    => "A new version of MathCursor is available — open the download page.",
        };

        public static string UpdateAvailableTitle => Lang switch
        {
            "fr" => "MathCursor — Mise à jour disponible",
            _    => "MathCursor — Update available",
        };

        public static string UpdateAvailableBody(string current, string latest) => Lang switch
        {
            "fr" =>
                $"Une nouvelle version de MathCursor est disponible : {latest}\n" +
                $"(tu utilises la {current}).\n\n" +
                "Ouvrir la page de téléchargement ?",
            _ =>
                $"A new version of MathCursor is available: {latest}\n" +
                $"(you're on {current}).\n\n" +
                "Open the download page?",
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
                "LICENCE\n" +
                "  MathCursor  Copyright (C) 2026  Côme Percin\n" +
                "  Ce programme est fourni SANS AUCUNE GARANTIE.\n" +
                "  C'est un logiciel libre, que vous pouvez redistribuer sous les\n" +
                "  conditions de la GNU GPL v3 (voir le fichier LICENSE dans le dossier\n" +
                "  d'installation) ou toute version ultérieure.\n" +
                "  Code source : https://github.com/come/MathCursor\n" +
                "  Polices math fournies : Latin Modern Math (GUST Font License) et\n" +
                "  STIX Two Math (SIL Open Font License) — textes des licences dans le\n" +
                "  sous-dossier fonts-licenses du dossier d'installation.\n\n" +
                "MERCI AUX TESTEURS ET CONTRIBUTEURS\n" +
                "  V. de Salaberry — Collège Le Sacré-Cœur, Vannes, France\n" +
                "  E. Velay — Lycée Français de Varsovie, Pologne",
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
                "LICENSE\n" +
                "  MathCursor  Copyright (C) 2026  Côme Percin\n" +
                "  This program comes with ABSOLUTELY NO WARRANTY.\n" +
                "  This is free software, and you are welcome to redistribute it under\n" +
                "  the terms of the GNU GPL v3 (see the LICENSE file in the install\n" +
                "  folder) or any later version.\n" +
                "  Source code: https://github.com/come/MathCursor\n" +
                "  Bundled math fonts: Latin Modern Math (GUST Font License) and STIX Two\n" +
                "  Math (SIL Open Font License) — license texts in the fonts-licenses\n" +
                "  subfolder of the install folder.\n\n" +
                "THANKS TO OUR TESTERS AND CONTRIBUTORS\n" +
                "  V. de Salaberry — Collège Le Sacré-Cœur, Vannes, France\n" +
                "  E. Velay — Lycée Français de Varsovie, Pologne",
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

        public static string BoxResultButtonLabel => Lang switch
        {
            "fr" => "Encadrer",
            _    => "Box result",
        };

        public static string BoxResultButtonScreentip => Lang switch
        {
            "fr" => "Encadre l'équation sous le curseur (cadre autour du résultat). Annulable en un Ctrl+Z. Sans effet si le curseur n'est pas dans une équation MathCursor.",
            _    => "Draws a frame around the equation under the caret (boxed result). Undo with a single Ctrl+Z. No effect if the caret is not inside a MathCursor equation.",
        };

        // Entrées de la popup d'édition (au-dessus d'une OMath à nous).
        public static string EditBoxFormulaLabel => Lang switch
        {
            "fr" => "Encadrer cette formule",
            _    => "Box this formula",
        };

        // Variante BLOC (chaîne / système) : on n'encadre que la dernière ligne.
        public static string EditBoxLastLineLabel => Lang switch
        {
            "fr" => "Encadrer la dernière ligne",
            _    => "Box last line",
        };

        public static string EditRevertLabel => Lang switch
        {
            "fr" => "Revenir à la saisie initiale",
            _    => "Revert to original input",
        };

        public static string CalloutMenuLabel => Lang switch
        {
            "fr" => "Encadré",
            _    => "Callout",
        };

        public static string CalloutMenuScreentip => Lang switch
        {
            "fr" => "Insère un encadré coloré (Théorème, Définition, Exemple, Propriété) autour de la sélection ou du paragraphe courant : barre d'accent, fond teinté et titre.",
            _    => "Inserts a coloured callout (Theorem, Definition, Example, Property) around the selection or current paragraph: accent bar, tinted background and title.",
        };

        // ---------- Sélecteur de police math (ADR 2026-06-22) ----------

        public static string MathFontLabel => Lang switch
        {
            "fr" => "Police math",
            _    => "Math font",
        };

        public static string MathFontScreentip => Lang switch
        {
            "fr" => "Choisit la police des équations (Cambria, Latin Modern, STIX). Applique la fonte à toutes les équations du document et la garde pour les prochaines. Latin Modern et STIX doivent être installées sur le poste.",
            _    => "Picks the font for equations (Cambria, Latin Modern, STIX). Applies it to every equation in the document and keeps it for the next ones. Latin Modern and STIX must be installed on this PC.",
        };

        public static string MathFontDefaultSuffix => Lang switch
        {
            "fr" => "(défaut)",
            _    => "(default)",
        };

        public static string MathFontNotInstalledSuffix => Lang switch
        {
            "fr" => "(à installer)",
            _    => "(not installed)",
        };

        public static string MathFontNotInstalledTitle => Lang switch
        {
            "fr" => "Police non installée",
            _    => "Font not installed",
        };

        public static string MathFontNotInstalledBody(string font) => Lang switch
        {
            "fr" => $"La police « {font} » n'est pas installée sur cet ordinateur : Word affichera les équations en Cambria Math en attendant.\n\nElle est gratuite — ouvrir la page de téléchargement maintenant ? (installe la police, puis re-sélectionne-la dans le menu « Police math »)",
            _    => $"The font \"{font}\" is not installed on this computer: Word will show the equations in Cambria Math for now.\n\nIt is free — open the download page now? (install the font, then pick it again from the \"Math font\" menu)",
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

        // ---------- Section « Confidentialité » (compteur d'usage) ----------

        public static string SettingsSectionPrivacy => Lang switch
        {
            "fr" => "Confidentialité",
            _    => "Privacy",
        };

        public static string SettingsUsageStatsLabel => Lang switch
        {
            "fr" => "Envoyer les statistiques d'usage anonymes",
            _    => "Send anonymous usage statistics",
        };

        public static string SettingsUsageStatsHint => Lang switch
        {
            "fr" => "Un simple compteur du nombre de formules converties. Aucun contenu, aucune donnée personnelle, aucun identifiant — juste un nombre, pour savoir si l'outil sert.",
            _    => "Just a count of how many formulas you convert. No content, no personal data, no identifier — only a number, to know the tool is used.",
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

        // Échecs visibles (ADR 2026-06-22-Fix-surface-silent-failures) — StatusBar.
        public static string ConvertCommitFailed => Lang switch
        {
            "fr" => "MathCursor : l'insertion a échoué — réessayez (détail au journal).",
            _    => "MathCursor: insertion failed — try again (see log for details).",
        };

        public static string RevertFailed => Lang switch
        {
            "fr" => "MathCursor : impossible de revenir à la saisie initiale (détail au journal).",
            _    => "MathCursor: could not revert to the original input (see log for details).",
        };

        public static string SourceNotRecorded => Lang switch
        {
            "fr" => "MathCursor : formule insérée, mais sa source n'a pas été enregistrée — ré-édition indisponible.",
            _    => "MathCursor: equation inserted, but its source wasn't saved — re-editing unavailable.",
        };

        // Démarrage / dialogues d'erreur ruban (ADR 2026-06-23 — i18n).
        public static string StatusReady => Lang switch
        {
            "fr" => "MathCursor prêt",
            _    => "MathCursor ready",
        };

        public static string StartupFailed => Lang switch
        {
            "fr" => "Échec du démarrage MathCursor :\n",
            _    => "MathCursor failed to start:\n",
        };

        public static string CalloutInsertFailed => Lang switch
        {
            "fr" => "Impossible d'insérer l'encadré :\n",
            _    => "Couldn't insert the callout:\n",
        };

        public static string ColumnsInsertFailed => Lang switch
        {
            "fr" => "Impossible d'insérer les colonnes :\n",
            _    => "Couldn't insert the columns:\n",
        };
    }
}
