// MathCursor: capturing mathematical intent from linear keyboard input.
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
using System.IO;

namespace MathCursor.Host.Feedback
{
    /// <summary>
    /// Identifiant utilisateur anonyme et stable : GUID persisté dans
    /// <c>%AppData%\MathCursor\user.id</c>. Sert à corréler les feedbacks d'un
    /// même testeur sans demander d'info personnelle. Généré au premier accès,
    /// gardé en mémoire ensuite. Zéro info identifiante dedans.
    /// </summary>
    internal static class UserIdStore
    {
        private static string _cached;
        private static readonly object _lock = new object();

        public static string GetOrCreate()
        {
            if (_cached != null) return _cached;
            lock (_lock)
            {
                if (_cached != null) return _cached;
                try
                {
                    var dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "MathCursor");
                    Directory.CreateDirectory(dir);
                    var path = Path.Combine(dir, "user.id");
                    if (File.Exists(path))
                    {
                        var content = File.ReadAllText(path).Trim();
                        if (Guid.TryParse(content, out _)) return _cached = content;
                    }
                    _cached = Guid.NewGuid().ToString("D");
                    File.WriteAllText(path, _cached);
                    return _cached;
                }
                catch
                {
                    // Si le filesystem refuse (permissions, quota), on retombe sur
                    // un ID en mémoire qui durera le temps de la session Word.
                    return _cached = Guid.NewGuid().ToString("D");
                }
            }
        }
    }
}
