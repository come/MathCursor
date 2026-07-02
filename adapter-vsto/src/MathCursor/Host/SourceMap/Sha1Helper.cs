// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme de Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
