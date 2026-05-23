using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MathCursor.Engine.Rules
{
    /// <summary>
    /// Charge les fichiers YAML <c>data-v2/concepts/*.yml</c> en
    /// <see cref="ConceptFile"/> structurés.
    /// </summary>
    public static class RuleLoader
    {
        public static ConceptFile LoadEmbedded(string conceptName)
        {
            var asm = typeof(RuleLoader).Assembly;
            var resName = ResolveResourceName(asm, $"data-v2.concepts.{conceptName}.yml");
            if (resName == null)
                throw new FileNotFoundException(
                    $"Concept '{conceptName}' not found in embedded resources.");
            using var stream = asm.GetManifestResourceStream(resName)!;
            using var reader = new StreamReader(stream);
            return FromYaml(conceptName, reader.ReadToEnd());
        }

        public static IReadOnlyList<ConceptFile> LoadAllEmbedded()
        {
            var asm = typeof(RuleLoader).Assembly;
            var concepts = new List<ConceptFile>();
            foreach (var resName in asm.GetManifestResourceNames())
            {
                var normalized = resName.Replace("-", "_");
                if (!normalized.Contains("data_v2.concepts.")) continue;
                if (!resName.EndsWith(".yml", System.StringComparison.OrdinalIgnoreCase)) continue;
                // Extrait le nom du concept à partir de la fin "...concepts.<name>.yml".
                var fileName = resName.Substring(resName.LastIndexOf('.', resName.Length - 5) + 1);
                fileName = fileName.Substring(0, fileName.Length - 4); // strip .yml
                using var stream = asm.GetManifestResourceStream(resName)!;
                using var reader = new StreamReader(stream);
                concepts.Add(FromYaml(fileName, reader.ReadToEnd()));
            }
            return concepts;
        }

        internal static ConceptFile FromYaml(string concept, string yaml)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var doc = deserializer.Deserialize<ConceptFile>(yaml)
                ?? throw new InvalidDataException($"Concept YAML '{concept}' is empty.");
            if (string.IsNullOrEmpty(doc.Concept)) doc.Concept = concept;
            // Renseigne Id et Anchor si absents (= dérivés du shape).
            for (int i = 0; i < doc.Rules.Count; i++)
            {
                var r = doc.Rules[i];
                if (string.IsNullOrEmpty(r.Id)) r.Id = $"{concept}-{i}";
                if (string.IsNullOrEmpty(r.Anchor) && !string.IsNullOrEmpty(r.Shape))
                    r.Anchor = ExtractAnchor(r.Shape);
            }
            return doc;
        }

        private static string ExtractAnchor(string shape)
        {
            // 1er token "mot nu" (= pas un $slot, pas une classe, pas une option).
            var trimmed = shape.TrimStart();
            int sp = trimmed.IndexOf(' ');
            var head = sp < 0 ? trimmed : trimmed.Substring(0, sp);
            // Si head commence par $, <, ( → on prend le prochain mot.
            if (head.Length == 0 || head[0] == '$' || head[0] == '<' || head[0] == '(')
                return "";
            return head;
        }

        private static string? ResolveResourceName(Assembly asm, string suffix)
        {
            var normalized = suffix.Replace("-", "_");
            foreach (var name in asm.GetManifestResourceNames())
            {
                var n = name.Replace("-", "_");
                if (n.EndsWith(normalized, System.StringComparison.OrdinalIgnoreCase))
                    return name;
            }
            return null;
        }
    }
}
