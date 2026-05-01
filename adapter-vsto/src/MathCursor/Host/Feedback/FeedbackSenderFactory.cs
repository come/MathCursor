using System;
using System.IO;

namespace MathCursor.Host.Feedback
{
    /// <summary>
    /// Choisit le sender à l'exécution :
    /// 1. Variable d'environnement <c>MATHCURSOR_FEEDBACK_URL</c> (priorité haute, utile en dev).
    /// 2. Fichier <c>%AppData%\MathCursor\feedback.url</c> (1 ligne = URL, déposé par l'installer).
    /// 3. Sinon → URL de production hardcodée (<see cref="DefaultFeedbackUrl"/>).
    ///
    /// Le fallback hardcodé garantit que l'envoi marche même si l'installer
    /// n'a pas créé le fichier (cas de migration depuis une version pré-0.5.5
    /// qui ne le créait pas, ou install partiellement corrompue).
    /// </summary>
    internal static class FeedbackSenderFactory
    {
        private const string UrlFileName = "feedback.url";

        /// <summary>URL endpoint de production (hardcodée comme dernier recours).
        /// À garder synchro avec <c>adapter-vsto/installer/feedback.url</c>.</summary>
        private const string DefaultFeedbackUrl = "https://mathcursor.pages.dev/api/v1/report";

        public static IFeedbackSender Create()
        {
            string url = ReadConfiguredUrl();
            if (string.IsNullOrWhiteSpace(url) || !IsHttpUrl(url))
            {
                url = DefaultFeedbackUrl;
            }
            return new HttpFeedbackSender(url);
        }

        private static string ReadConfiguredUrl()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("MATHCURSOR_FEEDBACK_URL");
                if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            }
            catch { }

            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", UrlFileName);
                if (File.Exists(path))
                {
                    var content = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrWhiteSpace(content)) return content;
                }
            }
            catch { }

            return null;
        }

        private static bool IsHttpUrl(string s)
        {
            return Uri.TryCreate(s, UriKind.Absolute, out var u)
                && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);
        }
    }
}
