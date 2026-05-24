using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MathCursor.Engine.Vocabulary
{
    /// <summary>
    /// Vocabulaire d'une locale (FR, EN, …) chargé depuis
    /// <c>data-v2/locale/&lt;code&gt;.yml</c>. Centralise classes synonymes
    /// (to/filler/dir), ancres, fonctions textuelles, relations multi-tier,
    /// glue, séparateurs et décimale.
    ///
    /// <para>Cf. brief v4 §3 + ADR <c>2026-05-22-Feat-engine-poc-isolation</c>.</para>
    /// </summary>
    public sealed class LocaleVocabulary
    {
        public string Code { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Classes { get; }
        public IReadOnlyDictionary<string, string> Anchors { get; }
        public IReadOnlyDictionary<string, string> Functions { get; }
        public IReadOnlyDictionary<string, Relation> Relations { get; }
        public IReadOnlyList<string> Glue { get; }
        public IReadOnlyList<string> ColSep { get; }
        public IReadOnlyList<string> RowSep { get; }
        public string Decimal { get; }

        /// <summary>Set précompilé des valeurs LaTeX renvoyées par
        /// <see cref="Functions"/> (= <c>\sin</c>, <c>\cos</c>, …). Utilisé par
        /// le parser pour reconnaître qu'un token Word est une function known
        /// reclassed (sans devoir reverse-lookup dans le dict à chaque check).</summary>
        public HashSet<string> FunctionLatexValues { get; }

        public LocaleVocabulary(
            string code,
            IReadOnlyDictionary<string, IReadOnlyList<string>> classes,
            IReadOnlyDictionary<string, string> anchors,
            IReadOnlyDictionary<string, string> functions,
            IReadOnlyDictionary<string, Relation> relations,
            IReadOnlyList<string> glue,
            IReadOnlyList<string> colSep,
            IReadOnlyList<string> rowSep,
            string @decimal)
        {
            Code = code;
            Classes = classes;
            Anchors = anchors;
            Functions = functions;
            Relations = relations;
            Glue = glue;
            ColSep = colSep;
            RowSep = rowSep;
            Decimal = @decimal;
            var funcSet = new HashSet<string>();
            foreach (var v in functions.Values) funcSet.Add(v);
            FunctionLatexValues = funcSet;
        }

        /// <summary>
        /// Cherche la classe à laquelle un token appartient (ex. "tend vers"
        /// → "to"). Retourne <c>null</c> si le token n'est dans aucune classe.
        /// </summary>
        public string? FindClass(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            foreach (var kv in Classes)
            {
                foreach (var member in kv.Value)
                    if (string.Equals(member, token, StringComparison.OrdinalIgnoreCase))
                        return kv.Key;
            }
            return null;
        }

        /// <summary>
        /// Cherche le LaTeX canonique d'une ancre (ex. "limite" → "lim").
        /// </summary>
        public string? FindAnchor(string token)
        {
            if (Anchors.TryGetValue(token, out var tex)) return tex;
            // Aussi accepter l'ancre déjà canonique (= "lim" → "lim").
            if (Anchors.Values is ICollection<string> vals)
            {
                foreach (var v in vals)
                    if (string.Equals(v, token, StringComparison.Ordinal)) return v;
            }
            return null;
        }

        public bool IsGlue(string token)
        {
            foreach (var g in Glue)
                if (string.Equals(g, token, StringComparison.Ordinal)) return true;
            return false;
        }

        // ─── Chargement YAML ─────────────────────────────────────────

        /// <summary>
        /// Charge le vocabulaire embedded de la locale demandée. Résout
        /// <c>data-v2/locale/&lt;code&gt;.yml</c> via les embedded resources
        /// de l'assembly <c>MathCursor.Engine</c>.
        /// </summary>
        public static LocaleVocabulary LoadEmbedded(string code)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentNullException(nameof(code));
            var asm = typeof(LocaleVocabulary).Assembly;
            var resName = ResolveResourceName(asm, $"data-v2.locale.{code}.yml");
            if (resName == null)
                throw new FileNotFoundException(
                    $"Locale '{code}' not found in embedded resources of MathCursor.Engine.");
            using var stream = asm.GetManifestResourceStream(resName)!;
            using var reader = new StreamReader(stream);
            return FromYaml(code, reader.ReadToEnd());
        }

        internal static LocaleVocabulary FromYaml(string code, string yaml)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var raw = deserializer.Deserialize<RawDoc>(yaml)
                ?? throw new InvalidDataException($"Vocab YAML '{code}' is empty.");

            var classes = ToReadonlyListDict(raw.Classes);
            var anchors = raw.Anchors ?? new Dictionary<string, string>();
            var functions = raw.Functions ?? new Dictionary<string, string>();
            var relations = BuildRelations(raw.Relations);
            var glue = (IReadOnlyList<string>)(raw.Glue ?? new List<string>());
            var colSep = (IReadOnlyList<string>)(raw.Colsep ?? new List<string>());
            var rowSep = (IReadOnlyList<string>)(raw.Rowsep ?? new List<string>());
            var dec = raw.Decimal ?? ".";

            return new LocaleVocabulary(
                code: code,
                classes: classes,
                anchors: anchors,
                functions: functions,
                relations: relations,
                glue: glue,
                colSep: colSep,
                rowSep: rowSep,
                @decimal: dec);
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> ToReadonlyListDict(
            Dictionary<string, List<string>>? src)
        {
            var dict = new Dictionary<string, IReadOnlyList<string>>();
            if (src != null)
                foreach (var kv in src) dict[kv.Key] = kv.Value;
            return dict;
        }

        private static IReadOnlyDictionary<string, Relation> BuildRelations(
            Dictionary<string, RelationRaw>? src)
        {
            var dict = new Dictionary<string, Relation>();
            if (src == null) return dict;
            foreach (var kv in src)
            {
                if (kv.Value == null) continue;
                if (!TryParseTier(kv.Value.Tier, out var tier))
                    throw new InvalidDataException(
                        $"Unknown precedence tier '{kv.Value.Tier}' for token '{kv.Key}'.");
                if (!TryParseContext(kv.Value.Context, out var context))
                    throw new InvalidDataException(
                        $"Unknown context '{kv.Value.Context}' for token '{kv.Key}'.");
                dict[kv.Key] = new Relation(
                    token: kv.Key,
                    tex: kv.Value.Tex ?? kv.Key,
                    tier: tier,
                    tail: kv.Value.Tail,
                    wrap: kv.Value.Wrap,
                    context: context);
            }
            return dict;
        }

        private static bool TryParseContext(string? raw, out RelationContext context)
        {
            switch ((raw ?? "").ToLowerInvariant())
            {
                case "":
                case "none":
                    context = RelationContext.None; return true;
                case "isolated_between_brackets":
                    context = RelationContext.IsolatedBetweenBrackets; return true;
                default:
                    context = RelationContext.None; return false;
            }
        }

        private static bool TryParseTier(string? raw, out PrecedenceTier tier)
        {
            switch ((raw ?? "").ToLowerInvariant())
            {
                case "funcpow": tier = PrecedenceTier.Funcpow; return true;
                case "muldiv":  tier = PrecedenceTier.Muldiv;  return true;
                case "addsub":  tier = PrecedenceTier.Addsub;  return true;
                case "setop":   tier = PrecedenceTier.Setop;   return true;
                case "comp":    tier = PrecedenceTier.Comp;    return true;
                case "rel":     tier = PrecedenceTier.Rel;     return true;
                case "and":     tier = PrecedenceTier.And;     return true;
                case "or":      tier = PrecedenceTier.Or;      return true;
                case "implies": tier = PrecedenceTier.Implies; return true;
                case "iff":     tier = PrecedenceTier.Iff;     return true;
                default:        tier = PrecedenceTier.Addsub;  return false;
            }
        }

        private static string? ResolveResourceName(Assembly asm, string suffix)
        {
            // Tolère préfixes et différences de séparateur (data-v2/locale/fr.yml
            // vs data_v2.locale.fr.yml selon le rewrite YamlDotNet/MSBuild).
            var normalized = suffix.Replace("/", ".").Replace("\\", ".").Replace("-", "_");
            foreach (var name in asm.GetManifestResourceNames())
            {
                var n = name.Replace("-", "_");
                if (n.EndsWith(normalized, StringComparison.OrdinalIgnoreCase))
                    return name;
            }
            return null;
        }

        // ─── POCO YAML ───────────────────────────────────────────────

        internal sealed class RawDoc
        {
            public Dictionary<string, List<string>>? Classes { get; set; }
            public Dictionary<string, string>? Anchors { get; set; }
            public Dictionary<string, string>? Functions { get; set; }
            public Dictionary<string, RelationRaw>? Relations { get; set; }
            public List<string>? Glue { get; set; }
            public List<string>? Colsep { get; set; }
            public List<string>? Rowsep { get; set; }
            public string? Decimal { get; set; }
        }

        internal sealed class RelationRaw
        {
            public string? Tex { get; set; }
            public string? Tier { get; set; }
            public string? Tail { get; set; }
            public bool Wrap { get; set; }
            public string? Context { get; set; }
        }
    }
}
