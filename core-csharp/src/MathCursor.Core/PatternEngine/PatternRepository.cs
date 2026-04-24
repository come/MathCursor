using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using YamlDotNet.RepresentationModel;

namespace MathCursor.Core.PatternEngine
{
    /// <summary>
    /// Charge les fichiers YAML (core + shared + {lang}) et fusionne les tokens
    /// et patterns en respectant la priorité (collision d'alias → priorité la
    /// plus haute gagne). Charge depuis les ressources embarquées de l'assembly.
    /// </summary>
    public sealed class PatternRepository
    {
        public IReadOnlyList<TokenDef> Tokens { get; }
        public IReadOnlyList<PatternDef> Patterns { get; }
        public ISet<string> Connectors { get; }
        public ISet<string> StopWords { get; }
        /// <summary>
        /// Phrases multi-mots : (tokens lowercase séparés par ' ') → token name à émettre
        /// (ex. "for all" → "FORALL"). Une entrée avec tokenName null = stop phrase à supprimer.
        /// </summary>
        public IReadOnlyDictionary<string, string?> Phrases { get; }

        private PatternRepository(
            IReadOnlyList<TokenDef> tokens,
            IReadOnlyList<PatternDef> patterns,
            ISet<string> connectors,
            ISet<string> stopWords,
            IReadOnlyDictionary<string, string?> phrases)
        {
            Tokens = tokens;
            Patterns = patterns;
            Connectors = connectors;
            StopWords = stopWords;
            Phrases = phrases;
        }

        /// <summary>Charge les YAML embarqués pour la langue donnée (fr, en).</summary>
        public static PatternRepository LoadEmbedded(string language = "fr")
        {
            var asm = typeof(PatternRepository).Assembly;
            var prefix = asm.GetName().Name + ".Data.yaml_domains.";

            var sources = new List<(string name, int priority)>();
            foreach (var resName in asm.GetManifestResourceNames())
            {
                if (!resName.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!resName.EndsWith(".yaml", StringComparison.Ordinal)) continue;

                // resName ressemble à "MathCursor.Core.Data.yaml_domains._core.core.yaml"
                string rel = resName.Substring(prefix.Length);
                int priority = PriorityForPath(rel, language);
                if (priority < 0) continue; // autre langue → skip
                sources.Add((resName, priority));
            }

            // Ordre de chargement : priority croissante (les priorités hautes écrasent les basses)
            sources.Sort((a, b) => a.priority.CompareTo(b.priority));

            var tokensByAlias = new Dictionary<string, TokenDef>(StringComparer.Ordinal);
            var tokensByName = new Dictionary<string, TokenDef>(StringComparer.Ordinal);
            var patterns = new List<PatternDef>();
            // Ordinal case-sensitive : "A" (variable) ne doit PAS matcher "a" (connector FR).
            var connectors = new HashSet<string>(StringComparer.Ordinal);
            var stopWords = new HashSet<string>(StringComparer.Ordinal);
            var phrases = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var (resName, priority) in sources)
            {
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream == null) continue;
                using var reader = new StreamReader(stream);
                var yaml = new YamlStream();
                yaml.Load(reader);
                if (yaml.Documents.Count == 0) continue;
                var root = yaml.Documents[0].RootNode as YamlMappingNode;
                if (root == null) continue;

                ParseTokens(root, priority, tokensByAlias, tokensByName, connectors);
                ParsePatterns(root, priority, patterns, tokensByAlias, tokensByName, connectors);
                ParseStopWords(root, stopWords, phrases);
                ParseConnectors(root, connectors);
            }

            var tokens = tokensByName.Values
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToList();
            return new PatternRepository(tokens, patterns, connectors, stopWords, phrases);
        }

        private static int PriorityForPath(string rel, string language)
        {
            // Convertir "_core.core.yaml" → folder = "_core"
            // (les séparateurs de dossier deviennent des '.' dans le nom de ressource)
            var parts = rel.Split('.');
            if (parts.Length < 2) return -1;
            string folder = parts[0];
            if (folder == "_core") return 0;
            if (folder == "shared") return 10;
            if (folder == language)
            {
                // _language.yaml → priority 50, sinon 100
                if (parts.Length >= 2 && parts[1].StartsWith("_language", StringComparison.Ordinal)) return 50;
                return 100;
            }
            return -1; // autre langue
        }

        private static void ParseTokens(
            YamlMappingNode root, int priority,
            Dictionary<string, TokenDef> tokensByAlias,
            Dictionary<string, TokenDef> tokensByName,
            HashSet<string> connectors)
        {
            if (!root.Children.TryGetValue(new YamlScalarNode("tokens"), out var tokensNode)) return;
            if (tokensNode is not YamlMappingNode tokensMap) return;

            foreach (var kv in tokensMap.Children)
            {
                if (kv.Key is not YamlScalarNode nameNode) continue;
                if (kv.Value is not YamlMappingNode def) continue;
                string name = nameNode.Value ?? "";
                var td = new TokenDef { Name = name, Priority = priority };

                if (def.Children.TryGetValue(new YamlScalarNode("canonical"), out var canNode) && canNode is YamlScalarNode cs)
                    td.Canonical = cs.Value ?? "";
                if (def.Children.TryGetValue(new YamlScalarNode("aliases"), out var aliasNode) && aliasNode is YamlSequenceNode aliasSeq)
                    td.Aliases = aliasSeq.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? "").Where(s => s.Length > 0).ToList();
                if (def.Children.TryGetValue(new YamlScalarNode("fuzzy_max_distance"), out var fuzzNode) && fuzzNode is YamlScalarNode fs && int.TryParse(fs.Value, out var fd))
                    td.FuzzyMaxDistance = fd;
                if (def.Children.TryGetValue(new YamlScalarNode("role"), out var roleNode) && roleNode is YamlScalarNode rs)
                    td.Role = rs.Value ?? "";

                // Priorité : ne remplace que si on a une priorité >= existante
                if (tokensByName.TryGetValue(name, out var existing) && existing.Priority > priority)
                    continue;
                tokensByName[name] = td;

                if (td.Role == "connector")
                {
                    foreach (var alias in td.Aliases)
                        connectors.Add(alias);
                }

                foreach (var alias in td.Aliases)
                {
                    if (tokensByAlias.TryGetValue(alias, out var prev) && prev.Priority > priority)
                        continue;
                    tokensByAlias[alias] = td;
                }
            }
        }

        private static void ParsePatterns(
            YamlMappingNode root, int priority,
            List<PatternDef> patterns,
            Dictionary<string, TokenDef> tokensByAlias,
            Dictionary<string, TokenDef> tokensByName,
            HashSet<string> connectors)
        {
            if (!root.Children.TryGetValue(new YamlScalarNode("patterns"), out var pNode)) return;
            if (pNode is not YamlSequenceNode pSeq) return;

            foreach (var item in pSeq.Children.OfType<YamlMappingNode>())
            {
                var pd = new PatternDef { Priority = priority };
                if (item.Children.TryGetValue(new YamlScalarNode("id"), out var idN) && idN is YamlScalarNode ids)
                    pd.Id = ids.Value ?? "";
                if (item.Children.TryGetValue(new YamlScalarNode("description"), out var descN) && descN is YamlScalarNode descs)
                    pd.Description = descs.Value ?? "";
                if (item.Children.TryGetValue(new YamlScalarNode("match"), out var matchN) && matchN is YamlScalarNode ms)
                    pd.Match = ms.Value ?? "";
                if (item.Children.TryGetValue(new YamlScalarNode("template"), out var tmplN) && tmplN is YamlScalarNode ts)
                    pd.Template = ts.Value ?? "";
                // Priorité per-pattern optionnelle (sinon on utilise celle du fichier)
                if (item.Children.TryGetValue(new YamlScalarNode("priority"), out var priN) && priN is YamlScalarNode pris
                    && int.TryParse(pris.Value, out var pri))
                    pd.Priority = pri;

                // tokens_inline : tokens définis localement dans le pattern.
                // Si le pattern est simple (match = juste le nom du token, template = constante),
                // on utilise le template comme Canonical pour ce token.
                if (item.Children.TryGetValue(new YamlScalarNode("tokens_inline"), out var inlineN) && inlineN is YamlMappingNode inlineMap)
                {
                    string inferredCanonical = "";
                    if (!string.IsNullOrEmpty(pd.Template) && !pd.Template.Contains("{{"))
                    {
                        // Template sans slots → peut servir de canonical pour le token matché
                        inferredCanonical = pd.Template;
                    }

                    foreach (var kv in inlineMap.Children)
                    {
                        if (kv.Key is not YamlScalarNode nameNode) continue;
                        if (kv.Value is not YamlMappingNode def) continue;
                        string name = nameNode.Value ?? "";
                        var td = new TokenDef { Name = name, Priority = priority };
                        if (def.Children.TryGetValue(new YamlScalarNode("aliases"), out var aliasNode) && aliasNode is YamlSequenceNode aliasSeq)
                            td.Aliases = aliasSeq.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? "").Where(s => s.Length > 0).ToList();
                        if (def.Children.TryGetValue(new YamlScalarNode("canonical"), out var canNode) && canNode is YamlScalarNode cs)
                            td.Canonical = cs.Value ?? "";
                        else if (pd.Match.Trim() == name && inferredCanonical.Length > 0)
                            td.Canonical = inferredCanonical;

                        if (tokensByName.TryGetValue(name, out var existing) && existing.Priority > priority)
                            continue;
                        tokensByName[name] = td;
                        foreach (var alias in td.Aliases)
                        {
                            if (tokensByAlias.TryGetValue(alias, out var prev) && prev.Priority > priority)
                                continue;
                            tokensByAlias[alias] = td;
                        }
                    }
                }

                if (item.Children.TryGetValue(new YamlScalarNode("examples"), out var exN) && exN is YamlSequenceNode exSeq)
                {
                    var list = new List<PatternExample>();
                    foreach (var ex in exSeq.Children.OfType<YamlMappingNode>())
                    {
                        var pe = new PatternExample();
                        if (ex.Children.TryGetValue(new YamlScalarNode("input"), out var inN) && inN is YamlScalarNode ins)
                            pe.Input = ins.Value ?? "";
                        if (ex.Children.TryGetValue(new YamlScalarNode("output"), out var outN) && outN is YamlScalarNode outs)
                            pe.Output = outs.Value ?? "";
                        if (ex.Children.TryGetValue(new YamlScalarNode("skip"), out var skN) && skN is YamlScalarNode sks)
                            pe.Skip = sks.Value ?? "";
                        list.Add(pe);
                    }
                    pd.Examples = list;
                }

                if (!string.IsNullOrEmpty(pd.Id) && !string.IsNullOrEmpty(pd.Match))
                    patterns.Add(pd);
            }
        }

        private static void ParseStopWords(YamlMappingNode root, HashSet<string> stopWords, Dictionary<string, string?> phrases)
        {
            if (root.Children.TryGetValue(new YamlScalarNode("stop_words"), out var swN) && swN is YamlSequenceNode swSeq)
                foreach (var s in swSeq.Children.OfType<YamlScalarNode>())
                    if (!string.IsNullOrEmpty(s.Value)) stopWords.Add(s.Value!);

            if (root.Children.TryGetValue(new YamlScalarNode("stop_phrases"), out var spN) && spN is YamlSequenceNode spSeq)
            {
                foreach (var item in spSeq.Children.OfType<YamlMappingNode>())
                {
                    if (!item.Children.TryGetValue(new YamlScalarNode("phrase"), out var pN) || pN is not YamlScalarNode ps) continue;
                    if (string.IsNullOrEmpty(ps.Value)) continue;
                    string phrase = ps.Value!;
                    string? mapsTo = null;
                    if (item.Children.TryGetValue(new YamlScalarNode("maps_to"), out var mN) && mN is YamlScalarNode ms)
                        mapsTo = string.IsNullOrEmpty(ms.Value) ? null : ms.Value;
                    phrases[phrase] = mapsTo;
                    if (mapsTo == null) stopWords.Add(phrase);
                }
            }
        }

        private static void ParseConnectors(YamlMappingNode root, HashSet<string> connectors)
        {
            if (!root.Children.TryGetValue(new YamlScalarNode("connectors"), out var cN)) return;
            if (cN is not YamlSequenceNode cSeq) return;
            foreach (var s in cSeq.Children.OfType<YamlScalarNode>())
                if (!string.IsNullOrEmpty(s.Value)) connectors.Add(s.Value!);
        }
    }
}
