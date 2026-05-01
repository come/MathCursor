using System;
using System.Net;
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
        // 15 s = marge confortable pour 1ère connexion via proxy d'entreprise
        // (DNS + TLS handshake + POST + R2 write).
        private static readonly HttpClient SharedClient = CreateClient();

        private static HttpClient CreateClient()
        {
            // Force TLS 1.2 (et 1.3 si dispo). .NET Framework 4.8 peut
            // négocier TLS 1.0/1.1 par défaut selon les paramètres registry,
            // ce que Cloudflare refuse (TLS 1.2+ obligatoire). Sans ce
            // override, le POST échoue avec "Could not create SSL/TLS secure
            // channel" et l'utilisateur croit à un problème réseau/proxy.
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                // TLS 1.3 = enum value 12288 (ajouté en .NET 4.8 mais pas
                // toujours dans l'enum compilé). On l'ajoute via le numérique.
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; } catch { }
            }
            catch { }
            return new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

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
