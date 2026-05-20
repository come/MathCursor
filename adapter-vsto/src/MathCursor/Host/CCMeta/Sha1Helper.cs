using System.Security.Cryptography;
using System.Text;

namespace MathCursor.Host.CCMeta
{
    /// <summary>
    /// SHA1 hex utilitaire pour calculer le <c>omml_hash</c> stocké dans
    /// <see cref="MCMeta"/>. SHA1 (et pas crypto-strong) parce qu'on
    /// l'utilise pour détecter une édition utilisateur, pas pour de la
    /// sécurité — collision résistance suffisante pour ce cas d'usage,
    /// court à afficher en debug (40 hex).
    /// </summary>
    internal static class Sha1Helper
    {
        public static string Compute(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            using (var sha = SHA1.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(s);
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
