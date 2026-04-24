using System;
using System.IO;

namespace MathCursor.Host.Feedback
{
    /// <summary>
    /// Choisit le sender à l'exécution :
    /// 1. Variable d'environnement <c>MATHCURSOR_FEEDBACK_URL</c> (priorité haute, utile en dev).
    /// 2. Fichier <c>%AppData%\MathCursor\feedback.url</c> (1 ligne = URL, géré par l'installer).
    /// 3. Sinon → ClipboardFeedbackSender.
    ///
    /// Pas de config valide = pas d'appels réseau silencieux. Le jour où tu
    /// déploies une API, tu remplis le fichier via l'installer ou la variable
    /// d'env, et ça bascule automatiquement.
    /// </summary>
    internal static class FeedbackSenderFactory
    {
        private const string UrlFileName = "feedback.url";

        public static IFeedbackSender Create()
        {
            string url = ReadConfiguredUrl();
            if (!string.IsNullOrWhiteSpace(url) && IsHttpUrl(url))
            {
                return new HttpFeedbackSender(url);
            }
            return new ClipboardFeedbackSender();
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
