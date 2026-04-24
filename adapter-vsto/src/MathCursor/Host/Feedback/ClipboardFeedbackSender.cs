using System;
using System.Threading.Tasks;
using System.Windows;

namespace MathCursor.Host.Feedback
{
    /// <summary>
    /// Implémentation de secours : sérialise le rapport en JSON et le met dans
    /// le presse-papier. L'utilisateur le colle dans WhatsApp/email/issue GitHub.
    /// Sert tant que l'endpoint HTTP n'est pas configuré — aucun réseau requis.
    /// </summary>
    internal sealed class ClipboardFeedbackSender : IFeedbackSender
    {
        public string Name => "presse-papier";

        public Task<FeedbackResult> SendAsync(FeedbackReport report)
        {
            try
            {
                string json = FeedbackJson.Serialize(report);
                Clipboard.SetText(json);
                return Task.FromResult(new FeedbackResult
                {
                    Success = true,
                    DisplayMessage = "Rapport copié dans le presse-papier — colle-le dans le groupe WhatsApp ou par email.",
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new FeedbackResult
                {
                    Success = false,
                    DisplayMessage = "Impossible de copier le rapport. Réessaie, ou envoie-nous un message texte.",
                    ErrorDetail = ex.Message,
                });
            }
        }
    }
}
