using System.Threading.Tasks;

namespace MathCursor.Host.Feedback
{
    /// <summary>
    /// Abstraction de l'envoi d'un rapport. Deux implémentations prévues :
    /// - ClipboardFeedbackSender : met le JSON dans le presse-papier (marche jour 1)
    /// - HttpFeedbackSender : POST vers une API configurable (activé quand l'URL est
    ///   renseignée via config ou variable d'environnement)
    /// Le choix est fait au runtime par FeedbackSenderFactory.
    /// </summary>
    public interface IFeedbackSender
    {
        /// <summary>Nom affiché à l'utilisateur après envoi ("envoyé via ..." / logs).</summary>
        string Name { get; }
        Task<FeedbackResult> SendAsync(FeedbackReport report);
    }
}
