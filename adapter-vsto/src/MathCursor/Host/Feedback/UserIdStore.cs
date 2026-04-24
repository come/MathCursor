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
