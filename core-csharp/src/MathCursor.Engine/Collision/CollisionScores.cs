using System.Collections.Generic;
using System.IO;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MathCursor.Engine.Collision
{
    /// <summary>
    /// P31 (2026-05-22) : scores de collision déclarés en YAML.
    /// Charge <c>data-v2/collision-scores.yml</c>. Les détecteurs consultent
    /// cette table via <see cref="ScoreFor"/> au lieu de hardcoder leurs
    /// scores dans le code C#. Permet de re-équilibrer l'ordre popup
    /// sans recompiler.
    /// </summary>
    public sealed class CollisionScores
    {
        private readonly IReadOnlyDictionary<string, int> _scores;

        public CollisionScores(IReadOnlyDictionary<string, int> scores)
        {
            _scores = scores;
        }

        /// <summary>
        /// Retourne le score déclaré pour <paramref name="ruleId"/>, ou
        /// <paramref name="fallback"/> si non défini en YAML.
        /// </summary>
        public int ScoreFor(string ruleId, int fallback = 50)
            => _scores.TryGetValue(ruleId, out var s) ? s : fallback;

        public static CollisionScores LoadEmbedded()
        {
            var asm = typeof(CollisionScores).Assembly;
            string? resName = null;
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith("collision-scores.yml", System.StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("collision_scores.yml", System.StringComparison.OrdinalIgnoreCase))
                {
                    resName = name;
                    break;
                }
            }
            if (resName == null)
                return new CollisionScores(new Dictionary<string, int>());
            using var stream = asm.GetManifestResourceStream(resName)!;
            using var reader = new StreamReader(stream);
            return FromYaml(reader.ReadToEnd());
        }

        internal static CollisionScores FromYaml(string yaml)
        {
            var de = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var raw = de.Deserialize<RawDoc>(yaml);
            var dict = new Dictionary<string, int>();
            if (raw?.Scores != null)
                foreach (var kv in raw.Scores) dict[kv.Key] = kv.Value;
            return new CollisionScores(dict);
        }

        internal sealed class RawDoc
        {
            public Dictionary<string, int>? Scores { get; set; }
        }
    }
}
