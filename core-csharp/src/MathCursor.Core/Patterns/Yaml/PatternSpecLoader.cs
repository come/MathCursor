using System.Collections.Generic;
using System.IO;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MathCursor.Core.Patterns.Yaml
{
    /// <summary>
    /// Loader pour les <see cref="PatternSpec"/> stockés en YAML embedded
    /// dans l'assembly Core (<c>data/patterns/*.yaml</c>). Utilise YamlDotNet
    /// pour deserializer.
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-yaml-pattern-specs</c>.</para>
    /// </summary>
    public static class PatternSpecLoader
    {
        private static readonly IDeserializer _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        /// <summary>
        /// Charge une <see cref="PatternSpec"/> depuis une ressource embedded
        /// de l'assembly Core. <paramref name="resourceName"/> doit être le
        /// nom de fichier (ex. <c>"lim.yaml"</c>) — le loader cherche
        /// <c>MathCursor.Core.data.patterns.lim.yaml</c> en interne.
        /// </summary>
        public static PatternSpec LoadEmbedded(string resourceName)
        {
            var assembly = typeof(PatternSpecLoader).Assembly;
            // Le namespace de la ressource embedded reflète la hiérarchie
            // dossier : data/patterns/foo.yaml → MathCursor.Core.data.patterns.foo.yaml
            string fullName = $"MathCursor.Core.data.patterns.{resourceName}";
            using var stream = assembly.GetManifestResourceStream(fullName)
                ?? throw new System.InvalidOperationException(
                    $"Embedded resource '{fullName}' not found. " +
                    $"Vérifier que le fichier est marqué `EmbeddedResource` dans le .csproj.");
            using var reader = new StreamReader(stream);
            string yaml = reader.ReadToEnd();
            return LoadFromString(yaml);
        }

        /// <summary>
        /// Charge depuis une chaîne YAML brute. Utile pour les tests
        /// (= éviter le packaging embedded).
        /// </summary>
        public static PatternSpec LoadFromString(string yaml)
        {
            if (string.IsNullOrWhiteSpace(yaml))
                throw new System.ArgumentException("YAML vide", nameof(yaml));
            return _deserializer.Deserialize<PatternSpec>(yaml);
        }

        /// <summary>
        /// Liste tous les noms de fichiers <c>.yaml</c> sous
        /// <c>data/patterns/</c> dans l'assembly embedded.
        /// </summary>
        public static IReadOnlyList<string> ListEmbeddedPatternFiles()
        {
            var assembly = typeof(PatternSpecLoader).Assembly;
            const string prefix = "MathCursor.Core.data.patterns.";
            var result = new List<string>();
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
                if (!name.EndsWith(".yaml", System.StringComparison.Ordinal)) continue;
                // Strip le prefix pour ne garder que "lim.yaml" etc.
                result.Add(name.Substring(prefix.Length));
            }
            return result;
        }
    }
}
