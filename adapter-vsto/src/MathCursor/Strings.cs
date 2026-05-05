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

        /// <summary>Label de l'onglet ruban MathCursor (top-level tab).</summary>
        public static string TabLabel => Lang switch
        {
            "fr" => "MathCursor",
            _    => "MathCursor",
        };

        /// <summary>Label du group "Outils" dans l'onglet MathCursor.</summary>
        public static string ToolsGroupLabel(string version) => Lang switch
        {
            "fr" => $"Outils — v{version}",
            _    => $"Tools — v{version}",
        };

        public static string CheatsheetButtonLabel => Lang switch
        {
            "fr" => "Cheatsheet",
            _    => "Cheatsheet",
        };

        public static string CheatsheetButtonScreentip => Lang switch
        {
            "fr" => "Ouvre/ferme le panneau des raccourcis MathCursor (steno math + raccourcis clavier)",
            _    => "Open/close the MathCursor shortcuts panel (math steno + keyboard shortcuts)",
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

        // ---------- Cheatsheet (stub Coming Soon en attendant le pane WPF) ----------

        public static string CheatsheetComingSoonTitle => Lang switch
        {
            "fr" => "MathCursor — Cheatsheet",
            _    => "MathCursor — Cheatsheet",
        };

        public static string CheatsheetComingSoonBody => Lang switch
        {
            "fr" => "Le panneau Cheatsheet arrive dans une prochaine version. " +
                    "Tu pourras y consulter tous les raccourcis MathCursor pendant " +
                    "que tu tapes ton cours, sans quitter Word.",
            _    => "The Cheatsheet panel is coming in an upcoming release. " +
                    "It will let you check all MathCursor shortcuts while you " +
                    "type your lesson, without leaving Word.",
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
    }
}
