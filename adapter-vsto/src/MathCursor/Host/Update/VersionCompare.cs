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

using System;

namespace MathCursor.Host.Update
{
    /// <summary>
    /// Comparaison de versions PURE (sans dépendance réseau/Word), testable en
    /// unitaire. Extrait de <see cref="UpdateChecker"/> (cf. ADR
    /// 2026-06-18-Feat-ribbon-update-badge).
    /// </summary>
    internal static class VersionCompare
    {
        /// <summary>
        /// Vrai si <paramref name="latest"/> est STRICTEMENT plus récent que
        /// <paramref name="current"/>, comparés en Major.Minor.Build (la révision
        /// est ignorée). Parsing tolérant : si l'un des deux ne parse pas, renvoie
        /// <c>false</c> — jamais de faux positif « MAJ dispo ».
        /// </summary>
        public static bool IsNewer(string latest, string current)
        {
            var l = ParseTriplet(latest);
            var c = ParseTriplet(current);
            if (l == null || c == null) return false;
            return l > c;
        }

        /// <summary>"x.y.z" / "x.y.z.w" / "vx.y.z" → Version(x.y.z.0), ou null.</summary>
        public static Version ParseTriplet(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            if (s[0] == 'v' || s[0] == 'V') s = s.Substring(1);
            var parts = s.Split('.');
            if (parts.Length < 3) return null;
            if (!int.TryParse(parts[0], out int maj)
                || !int.TryParse(parts[1], out int min)
                || !int.TryParse(parts[2], out int build)) return null;
            if (maj < 0 || min < 0 || build < 0) return null;
            return new Version(maj, min, build); // révision = 0, normalisée
        }

        /// <summary>
        /// Extrait la valeur de <c>"latest"</c> d'un JSON plat
        /// <c>{ "latest": "x.y.z" }</c> sans dépendance JSON. Null si absent.
        /// </summary>
        public static string ExtractLatest(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            const string key = "\"latest\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            i = json.IndexOf(':', i + key.Length);
            if (i < 0) return null;
            i++;
            while (i < json.Length && (json[i] == ' ' || json[i] == '"')) i++;
            int start = i;
            while (i < json.Length && json[i] != '"' && json[i] != ',' && json[i] != '}') i++;
            if (i < start) return null;
            return json.Substring(start, i - start).Trim();
        }
    }
}
