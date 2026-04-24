using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace MathCursor.Host.Feedback
{
    /// <summary>
    /// Envoie le rapport via HTTP POST à une URL configurable. Scaffold prêt :
    /// l'URL est passée au constructeur (par FeedbackSenderFactory qui la lit
    /// depuis config/env). Si l'endpoint n'est pas encore opérationnel côté
    /// serveur, le sender renvoie un Fail propre sans bloquer.
    ///
    /// CONTRAT API (à implémenter côté backend) :
    ///   POST {endpoint}
    ///   Content-Type: application/json
    ///   Body : FeedbackJson.Serialize(report) — flat JSON, clés snake_case
    ///   Response 2xx = succès, autre = échec
    /// </summary>
    internal sealed class HttpFeedbackSender : IFeedbackSender
    {
        // HttpClient static : recommandé par MS pour éviter l'épuisement des
        // sockets en cas d'utilisation répétée. Timeout défini à l'instance.
        private static readonly HttpClient SharedClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        private readonly string _endpoint;

        public HttpFeedbackSender(string endpoint)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        public string Name => "serveur";

        public async Task<FeedbackResult> SendAsync(FeedbackReport report)
        {
            try
            {
                string json = FeedbackJson.Serialize(report);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    var response = await SharedClient.PostAsync(_endpoint, content).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        return new FeedbackResult
                        {
                            Success = true,
                            DisplayMessage = "Merci ! Ton retour a été envoyé.",
                        };
                    }
                    return new FeedbackResult
                    {
                        Success = false,
                        DisplayMessage = "L'envoi a échoué. Réessaie dans un instant, ou utilise WhatsApp.",
                        ErrorDetail = $"HTTP {(int)response.StatusCode}",
                    };
                }
            }
            catch (Exception ex)
            {
                return new FeedbackResult
                {
                    Success = false,
                    DisplayMessage = "Pas de réseau ou serveur injoignable. Réessaie plus tard, ou utilise WhatsApp.",
                    ErrorDetail = ex.Message,
                };
            }
        }
    }
}
