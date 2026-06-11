using System.Security.Cryptography;
using System.Text;

namespace MathCursor.Host.SourceMap
{
    /// <summary>
    /// SHA1 hex utilitaire pour les clés K1 (Range.Text) et K2 (OMML
    /// canonique) de la source map. SHA1 (et pas crypto-strong) parce que
    /// le besoin est l'identité de contenu, pas la sécurité — collision
    /// résistance suffisante, court à afficher en debug (40 hex).
    /// (Déménagé depuis Host/CCMeta — le dossier CCMeta disparaît avec le
    /// pattern anchor, ADR 2026-06-11-Feat-hash-source-map-no-cc.)
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
