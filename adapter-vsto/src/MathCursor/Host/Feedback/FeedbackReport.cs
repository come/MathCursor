using System;

namespace MathCursor.Host.Feedback
{
    /// <summary>
    /// Rapport de feedback envoyé depuis le lien "Signaler une erreur" de la popup.
    /// Contexte technique pré-rempli au moment du clic + message utilisateur saisi
    /// dans le dialog. Structure flat volontairement (sérialisable en JSON simple,
    /// pas de dépendance Newtonsoft/etc).
    /// </summary>
    public sealed class FeedbackReport
    {
        public string Version { get; set; } = "";
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string UserId { get; set; } = "";
        public string SessionId { get; set; } = "";

        /// <summary>Texte détecté par le NER ou span issue du trigger manuel.</summary>
        public string NerText { get; set; } = "";
        /// <summary>Formule LaTeX sélectionnée dans la popup au moment du signalement.</summary>
        public string RecognizedFormula { get; set; } = "";

        /// <summary>Message libre de l'utilisateur (ce qu'il voulait faire, ce qui s'est passé).</summary>
        public string UserMessage { get; set; } = "";
        /// <summary>Email facultatif pour recontact.</summary>
        public string UserEmail { get; set; } = "";

        /// <summary>Dernière portion du log local (diagnostic).</summary>
        public string LogTail { get; set; } = "";

        public string WordVersion { get; set; } = "";
        public string OsVersion { get; set; } = "";
    }

    /// <summary>Retour d'un envoi de feedback.</summary>
    public sealed class FeedbackResult
    {
        public bool Success { get; set; }
        /// <summary>Message affichable à l'utilisateur (succès OU échec).</summary>
        public string DisplayMessage { get; set; } = "";
        /// <summary>Détail technique (erreur réseau, etc.), pour logs uniquement.</summary>
        public string ErrorDetail { get; set; } = "";
    }
}
