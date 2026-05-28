using System.Collections.Generic;
using System.IO;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MathCursor.Engine.Rules
{
    /// <summary>
    /// Charge les fichiers YAML <c>data/concepts/*.yml</c> en
    /// <see cref="ConceptFile"/> structurés.
    /// </summary>
    public static class RuleLoader
    {
        public static ConceptFile LoadEmbedded(string conceptName)
        {
            var asm = typeof(RuleLoader).Assembly;
            var resName = ResolveResourceName(asm, $"data.concepts.{conceptName}.yml");
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
                if (!resName.Contains("data.concepts.")) continue;
                if (!resName.EndsWith(".yml", System.StringComparison.OrdinalIgnoreCase)) continue;
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
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var doc = deserializer.Deserialize<ConceptFile>(yaml)
                ?? throw new InvalidDataException($"Concept YAML '{concept}' is empty.");
            if (string.IsNullOrEmpty(doc.Concept)) doc.Concept = concept;
            for (int i = 0; i < doc.Rules.Count; i++)
            {
                var r = doc.Rules[i];
                if (string.IsNullOrEmpty(r.Id)) r.Id = $"{concept}-{i}";
            }
            return doc;
        }

        private static string? ResolveResourceName(Assembly asm, string suffix)
        {
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                    return name;
            }
            return null;
        }
    }
}
