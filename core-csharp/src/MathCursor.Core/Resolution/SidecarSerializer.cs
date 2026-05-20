using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Sérialisation JSON minimaliste de <see cref="ResolutionSidecar"/> pour
    /// persistance dans un CustomXMLPart Word ou équivalent Office.js. Pas de
    /// dépendance NuGet — JSON manuel pattern <c>FeedbackJson</c>.
    ///
    /// <para>Format v2 (cf. brief 2026-05-07-rule-pin-span-override-refactor) :</para>
    /// <code>
    /// {"v":2,
    ///  "rule_pins":[{"r":"two-uppercase","a":0}],
    ///  "span_overrides":[{"r":"two-uppercase","d":"AB","p":3,"o":0,"a":0}],
    ///  "pins":[],"votes":{}}
    /// </code>
    /// (les champs <c>pins</c>/<c>votes</c> v1 legacy sont écrits en v2 si
    /// non vides, pour permettre un downgrade éventuel et préserver les
    /// SpanPins span-level qui n'ont pas encore été convertis en
    /// SpanOverrides — la conversion nécessite le rawSource, faite ailleurs.)
    ///
    /// <para>Format v1 (legacy) toléré au load via lazy convert :</para>
    /// <code>
    /// {"v":1,"pins":[{"r":"two-uppercase","o":0,"l":2,"a":0}],"votes":{"two-uppercase":{"0":3}}}
    /// </code>
    /// Au load v1 : ZoneVotes → RulePins via argmax (décision user 2026-05-07 #3).
    /// SpanPins legacy gardés tels quels (conversion en SpanOverrides reportée
    /// au moment où le rawSource est disponible).
    ///
    /// <para>Versionné via le champ <c>v</c>. Au reload d'une version inconnue
    /// (future) → <see cref="ResolutionSidecar.Empty"/>.</para>
    /// </summary>
    public static class SidecarSerializer
    {
        public const int CurrentVersion = 2;

        public static string Serialize(ResolutionSidecar sidecar)
        {
            if (sidecar == null || sidecar.IsEmpty) return "";
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"v\":").Append(CurrentVersion);

            if (sidecar.SpanPins.Count > 0)
            {
                sb.Append(",\"pins\":[");
                bool first = true;
                foreach (var p in sidecar.SpanPins)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('{');
                    sb.Append("\"r\":");
                    AppendString(sb, p.Rule ?? "");
                    sb.Append(",\"o\":").Append(p.Offset);
                    sb.Append(",\"l\":").Append(p.Len);
                    sb.Append(",\"a\":").Append(p.AltIdx);
                    sb.Append('}');
                }
                sb.Append(']');
            }

            if (sidecar.ZoneVotes.Count > 0)
            {
                sb.Append(",\"votes\":{");
                bool firstRule = true;
                foreach (var ruleEntry in sidecar.ZoneVotes)
                {
                    if (!firstRule) sb.Append(',');
                    firstRule = false;
                    AppendString(sb, ruleEntry.Key ?? "");
                    sb.Append(":{");
                    bool firstAlt = true;
                    foreach (var altEntry in ruleEntry.Value)
                    {
                        if (!firstAlt) sb.Append(',');
                        firstAlt = false;
                        AppendString(sb, altEntry.Key.ToString(CultureInfo.InvariantCulture));
                        sb.Append(':').Append(altEntry.Value);
                    }
                    sb.Append('}');
                }
                sb.Append('}');
            }

            // v2 : rule_pins
            if (sidecar.RulePins.Count > 0)
            {
                sb.Append(",\"rule_pins\":[");
                bool first = true;
                foreach (var rp in sidecar.RulePins)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('{');
                    sb.Append("\"r\":");
                    AppendString(sb, rp.RuleId ?? "");
                    sb.Append(",\"a\":").Append(rp.AltIdx);
                    sb.Append('}');
                }
                sb.Append(']');
            }

            // v2 : span_overrides
            if (sidecar.SpanOverrides.Count > 0)
            {
                sb.Append(",\"span_overrides\":[");
                bool first = true;
                foreach (var ov in sidecar.SpanOverrides)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('{');
                    sb.Append("\"r\":");
                    AppendString(sb, ov.Signature.RuleId ?? "");
                    sb.Append(",\"d\":");
                    AppendString(sb, ov.Signature.DefaultLatex ?? "");
                    sb.Append(",\"p\":").Append(ov.Signature.RawSourcePos);
                    sb.Append(",\"o\":").Append(ov.Signature.OccurrenceIdx);
                    sb.Append(",\"a\":").Append(ov.AltIdx); // -1 = revert
                    sb.Append('}');
                }
                sb.Append(']');
            }

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Parse une string JSON produite par <see cref="Serialize"/>. Robuste :
        /// retourne <see cref="ResolutionSidecar.Empty"/> sur entrée vide, null,
        /// JSON malformé, version inconnue. Pas d'exception.
        /// </summary>
        public static ResolutionSidecar Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return ResolutionSidecar.Empty;
            try
            {
                var p = new MiniJsonParser(json!);
                p.SkipWhitespace();
                if (!p.Match('{')) return ResolutionSidecar.Empty;

                int version = 0;
                var pins = new List<SpanPin>();
                var votes = new Dictionary<string, IReadOnlyDictionary<int, int>>();
                var rulePins = new List<RulePin>();
                var spanOverrides = new List<SpanOverride>();

                while (true)
                {
                    p.SkipWhitespace();
                    if (p.Peek() == '}') { p.Index++; break; }
                    string key = p.ReadString();
                    p.SkipWhitespace();
                    if (!p.Match(':')) return ResolutionSidecar.Empty;
                    p.SkipWhitespace();

                    switch (key)
                    {
                        case "v":
                            version = (int)p.ReadNumber();
                            break;
                        case "pins":
                            ReadPins(p, pins);
                            break;
                        case "votes":
                            ReadVotes(p, votes);
                            break;
                        case "rule_pins":
                            ReadRulePins(p, rulePins);
                            break;
                        case "span_overrides":
                            ReadSpanOverrides(p, spanOverrides);
                            break;
                        default:
                            p.SkipValue();
                            break;
                    }

                    p.SkipWhitespace();
                    if (p.Peek() == ',') { p.Index++; continue; }
                }

                if (version > CurrentVersion)
                {
                    // Version future → migration silencieuse (on ne sait pas
                    // parser des champs futurs).
                    return ResolutionSidecar.Empty;
                }

                // Lazy convert v1 → v2 : ZoneVotes → RulePins via argmax
                // (décision user 2026-05-07 #3 « convertir »).
                // SpanPins legacy gardés tels quels — la conversion en
                // SpanOverrides nécessite le rawSource, faite ailleurs.
                if (version <= 1 && votes.Count > 0 && rulePins.Count == 0)
                {
                    foreach (var ruleEntry in votes)
                    {
                        if (ruleEntry.Value == null || ruleEntry.Value.Count == 0) continue;
                        int bestAlt = -1;
                        int bestCount = 0;
                        foreach (var kv in ruleEntry.Value)
                        {
                            if (kv.Value > bestCount
                                || (kv.Value == bestCount && (bestAlt < 0 || kv.Key < bestAlt)))
                            {
                                bestCount = kv.Value;
                                bestAlt = kv.Key;
                            }
                        }
                        if (bestAlt >= 0)
                            rulePins.Add(new RulePin(ruleEntry.Key, bestAlt));
                    }
                }

                return new ResolutionSidecar(pins, votes, rulePins, spanOverrides);
            }
            catch
            {
                return ResolutionSidecar.Empty;
            }
        }

        private static void ReadPins(MiniJsonParser p, List<SpanPin> pins)
        {
            p.SkipWhitespace();
            if (!p.Match('[')) return;
            while (true)
            {
                p.SkipWhitespace();
                if (p.Peek() == ']') { p.Index++; break; }
                if (!p.Match('{')) return;
                string rule = "";
                int offset = 0, len = 0, alt = 0;
                while (true)
                {
                    p.SkipWhitespace();
                    if (p.Peek() == '}') { p.Index++; break; }
                    string k = p.ReadString();
                    p.SkipWhitespace();
                    if (!p.Match(':')) return;
                    p.SkipWhitespace();
                    switch (k)
                    {
                        case "r": rule = p.ReadString(); break;
                        case "o": offset = (int)p.ReadNumber(); break;
                        case "l": len = (int)p.ReadNumber(); break;
                        case "a": alt = (int)p.ReadNumber(); break;
                        default: p.SkipValue(); break;
                    }
                    p.SkipWhitespace();
                    if (p.Peek() == ',') { p.Index++; continue; }
                }
                pins.Add(new SpanPin(rule, offset, len, alt));
                p.SkipWhitespace();
                if (p.Peek() == ',') { p.Index++; continue; }
            }
        }

        private static void ReadRulePins(MiniJsonParser p, List<RulePin> rulePins)
        {
            p.SkipWhitespace();
            if (!p.Match('[')) return;
            while (true)
            {
                p.SkipWhitespace();
                if (p.Peek() == ']') { p.Index++; break; }
                if (!p.Match('{')) return;
                string rule = "";
                int alt = 0;
                while (true)
                {
                    p.SkipWhitespace();
                    if (p.Peek() == '}') { p.Index++; break; }
                    string k = p.ReadString();
                    p.SkipWhitespace();
                    if (!p.Match(':')) return;
                    p.SkipWhitespace();
                    switch (k)
                    {
                        case "r": rule = p.ReadString(); break;
                        case "a": alt = (int)p.ReadNumber(); break;
                        default: p.SkipValue(); break;
                    }
                    p.SkipWhitespace();
                    if (p.Peek() == ',') { p.Index++; continue; }
                }
                if (!string.IsNullOrEmpty(rule) && alt >= 0)
                    rulePins.Add(new RulePin(rule, alt));
                p.SkipWhitespace();
                if (p.Peek() == ',') { p.Index++; continue; }
            }
        }

        private static void ReadSpanOverrides(MiniJsonParser p, List<SpanOverride> spanOverrides)
        {
            p.SkipWhitespace();
            if (!p.Match('[')) return;
            while (true)
            {
                p.SkipWhitespace();
                if (p.Peek() == ']') { p.Index++; break; }
                if (!p.Match('{')) return;
                string rule = "";
                string defaultLatex = "";
                int pos = 0, occ = 0, alt = 0;
                while (true)
                {
                    p.SkipWhitespace();
                    if (p.Peek() == '}') { p.Index++; break; }
                    string k = p.ReadString();
                    p.SkipWhitespace();
                    if (!p.Match(':')) return;
                    p.SkipWhitespace();
                    switch (k)
                    {
                        case "r": rule = p.ReadString(); break;
                        case "d": defaultLatex = p.ReadString(); break;
                        case "p": pos = (int)p.ReadNumber(); break;
                        case "o": occ = (int)p.ReadNumber(); break;
                        case "a": alt = (int)p.ReadNumber(); break;
                        default: p.SkipValue(); break;
                    }
                    p.SkipWhitespace();
                    if (p.Peek() == ',') { p.Index++; continue; }
                }
                if (!string.IsNullOrEmpty(rule) && pos >= 0 && occ >= 0
                    && alt >= SpanOverride.AltIdxRevert)
                {
                    var sig = new MatchSignature(rule, defaultLatex, pos, occ);
                    spanOverrides.Add(new SpanOverride(sig, alt));
                }
                p.SkipWhitespace();
                if (p.Peek() == ',') { p.Index++; continue; }
            }
        }

        private static void ReadVotes(MiniJsonParser p,
            Dictionary<string, IReadOnlyDictionary<int, int>> votes)
        {
            p.SkipWhitespace();
            if (!p.Match('{')) return;
            while (true)
            {
                p.SkipWhitespace();
                if (p.Peek() == '}') { p.Index++; break; }
                string ruleKey = p.ReadString();
                p.SkipWhitespace();
                if (!p.Match(':')) return;
                p.SkipWhitespace();
                if (!p.Match('{')) return;
                var byAlt = new Dictionary<int, int>();
                while (true)
                {
                    p.SkipWhitespace();
                    if (p.Peek() == '}') { p.Index++; break; }
                    string altStr = p.ReadString();
                    p.SkipWhitespace();
                    if (!p.Match(':')) return;
                    p.SkipWhitespace();
                    int count = (int)p.ReadNumber();
                    if (int.TryParse(altStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var altKey))
                        byAlt[altKey] = count;
                    p.SkipWhitespace();
                    if (p.Peek() == ',') { p.Index++; continue; }
                }
                votes[ruleKey] = byAlt;
                p.SkipWhitespace();
                if (p.Peek() == ',') { p.Index++; continue; }
            }
        }

        private static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ─────────────────────────────────────────────────────────────────
        //  Mini-parseur JSON suffisant pour le format ci-dessus. Pas
        //  général-purpose : pas de support des floats, des arrays
        //  imbriqués profonds, des null, etc. Si on a besoin de plus, on
        //  ajoute System.Text.Json en NuGet.
        // ─────────────────────────────────────────────────────────────────

        private sealed class MiniJsonParser
        {
            private readonly string _s;
            public int Index;

            public MiniJsonParser(string s) { _s = s; }

            public char Peek() => Index < _s.Length ? _s[Index] : '\0';

            public bool Match(char c)
            {
                if (Peek() == c) { Index++; return true; }
                return false;
            }

            public void SkipWhitespace()
            {
                while (Index < _s.Length && char.IsWhiteSpace(_s[Index])) Index++;
            }

            public string ReadString()
            {
                if (!Match('"')) return "";
                var sb = new StringBuilder();
                while (Index < _s.Length)
                {
                    char c = _s[Index++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\' && Index < _s.Length)
                    {
                        char esc = _s[Index++];
                        switch (esc)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (Index + 4 <= _s.Length
                                    && int.TryParse(_s.Substring(Index, 4),
                                        NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                {
                                    sb.Append((char)code); Index += 4;
                                }
                                break;
                            default: sb.Append(esc); break;
                        }
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }

            public double ReadNumber()
            {
                int start = Index;
                if (Peek() == '-') Index++;
                while (Index < _s.Length && (char.IsDigit(_s[Index]) || _s[Index] == '.')) Index++;
                string lit = _s.Substring(start, Index - start);
                return double.TryParse(lit, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
            }

            /// <summary>Skip une valeur inconnue (pour forward-compat avec
            /// futurs champs ajoutés au sidecar).</summary>
            public void SkipValue()
            {
                SkipWhitespace();
                char c = Peek();
                if (c == '"') ReadString();
                else if (c == '{') SkipObject();
                else if (c == '[') SkipArray();
                else
                {
                    while (Index < _s.Length && ",}]".IndexOf(_s[Index]) < 0) Index++;
                }
            }

            private void SkipObject()
            {
                if (!Match('{')) return;
                int depth = 1;
                while (Index < _s.Length && depth > 0)
                {
                    char c = _s[Index++];
                    if (c == '"') { Index--; ReadString(); continue; }
                    if (c == '{') depth++;
                    else if (c == '}') depth--;
                }
            }

            private void SkipArray()
            {
                if (!Match('[')) return;
                int depth = 1;
                while (Index < _s.Length && depth > 0)
                {
                    char c = _s[Index++];
                    if (c == '"') { Index--; ReadString(); continue; }
                    if (c == '[') depth++;
                    else if (c == ']') depth--;
                }
            }
        }
    }
}
