using System.Collections.Generic;
using System.Text;
using MathCursor.Engine.Ast;
using MathCursor.Engine.Parsing;
using MathCursor.Engine.Parsing.List;
using MathCursor.Engine.Rules;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Emit
{
    /// <summary>
    /// Rend un <see cref="ShapeMatch"/> selon le template d'emit YAML —
    /// brief v4 §2.1. Supporte interpolation <c>$slot</c> et
    /// <b>fragment-vanish</b> <c>[ ... ]</c> (= rendu seulement si tous les
    /// slots dedans sont remplis).
    ///
    /// <para>Chaque slot est lui-même tokenisé/parsé/rendu via le pipeline
    /// (Tokenizer → StackParser → ListCombinator → <see cref="LatexEmitter"/>)
    /// pour que les sous-expressions s'affichent en LaTeX correct.</para>
    /// </summary>
    public sealed class TemplateEmitter
    {
        private readonly LocaleVocabulary _vocab;
        private readonly LatexEmitter _emitter;

        public TemplateEmitter(LocaleVocabulary vocab)
        {
            _vocab = vocab ?? throw new System.ArgumentNullException(nameof(vocab));
            _emitter = new LatexEmitter();
        }

        public string Emit(ShapeMatch match)
        {
            if (match == null) throw new System.ArgumentNullException(nameof(match));
            var renderedSlots = RenderSlots(match.Slots);
            return InterpolateTemplate(match.Rule.Emit, renderedSlots);
        }

        private IReadOnlyDictionary<string, string> RenderSlots(
            IReadOnlyDictionary<string, IReadOnlyList<Token>> slotsTokens)
        {
            var rendered = new Dictionary<string, string>();
            foreach (var kv in slotsTokens)
            {
                rendered[kv.Key] = RenderSlotValue(kv.Value);
            }
            return rendered;
        }

        private string RenderSlotValue(IReadOnlyList<Token> tokens)
        {
            if (tokens == null || tokens.Count == 0) return string.Empty;

            // Cas court : 1 token simple → texte brut.
            if (tokens.Count == 1)
            {
                var t = tokens[0];
                if (t.Kind == TokenKind.Word || t.Kind == TokenKind.Number)
                    return t.Text;
            }

            // Si le slot est exactement `(expr)` (= groupe parenthésé enveloppant
            // tout le slot), on déballe — les parens du body sont "redondantes"
            // autour d'un argument déjà délimité par l'ancre. Cf. golden brief
            // `sum k 1 n (1/k)` → \frac{1}{k} (pas `(\frac{1}{k})`).
            var stripped = StripOuterParensIfWhole(tokens);

            // P12+P16 : concat brut sauf si le slot contient un opérateur
            // qui demande une transformation LaTeX :
            //   - `/` → \frac{a}{b}
            //   - `^` / `_` → braces autour du droit non-atom (a^{b+c})
            if (NeedsLatexTransform(stripped))
            {
                var parser = new StackParser(_vocab);
                var ast = parser.Parse(stripped);
                ast = ListCombinator.Promote(ast);
                return _emitter.Emit(ast);
            }
            return ConcatRaw(stripped);
        }

        private static bool NeedsLatexTransform(IReadOnlyList<Token> tokens)
        {
            foreach (var t in tokens)
            {
                if (t.Kind != TokenKind.Symbol) continue;
                if (t.Text == "/" || t.Text == "^" || t.Text == "_"
                    || t.Text == "*") return true;
            }
            return false;
        }

        private static string ConcatRaw(IReadOnlyList<Token> tokens)
        {
            // P13 : les Sep internes (= whitespace tokens) sont rendus comme
            // espaces fins. Brief v5 § rendu fidèle au source.
            var sb = new System.Text.StringBuilder();
            foreach (var t in tokens)
            {
                if (t.Kind == TokenKind.Sep && t.Text == " ")
                    sb.Append(' ');
                else
                    sb.Append(t.Text);
            }
            return sb.ToString();
        }

        private static IReadOnlyList<Token> StripOuterParensIfWhole(IReadOnlyList<Token> tokens)
        {
            if (tokens.Count < 2) return tokens;
            if (tokens[0].Kind != TokenKind.OpenDelim || tokens[0].Text != "(") return tokens;
            if (tokens[tokens.Count - 1].Kind != TokenKind.CloseDelim || tokens[tokens.Count - 1].Text != ")")
                return tokens;
            // Vérifie que les parens de bordure sont matched (= depth ne tombe
            // pas à 0 avant la fin).
            int depth = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Kind == TokenKind.OpenDelim && t.Text == "(") depth++;
                else if (t.Kind == TokenKind.CloseDelim && t.Text == ")") depth--;
                if (depth == 0 && i < tokens.Count - 1) return tokens; // parens externes pas wrap complet
            }
            var inner = new List<Token>(tokens.Count - 2);
            for (int i = 1; i < tokens.Count - 1; i++) inner.Add(tokens[i]);
            return inner;
        }

        private static string InterpolateTemplate(
            string template, IReadOnlyDictionary<string, string> slots)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                // P23 (2026-05-22) : fragment-vanish `[...]` désactivé.
                // Brackets traités comme literals LaTeX (= utilisés pour
                // \sqrt[n]{body}, [0,1], etc.). Si besoin futur de
                // fragment-vanish, utiliser une autre syntaxe (= <<...>>).
                if (c == '$')
                {
                    // Lit nom du slot (alphanumérique + _). P12 : si purement
                    // numérique → référence positionnelle $1, $2, … alimentée
                    // par les slots typés (= `$N` ajouté en miroir au stockage
                    // de chaque slot typé). Cf. ShapeMatcher.TryMatchTypedSlot.
                    int s = i + 1;
                    int e = s;
                    while (e < template.Length
                        && (char.IsLetterOrDigit(template[e]) || template[e] == '_')) e++;
                    var slotName = template.Substring(s, e - s);
                    string lookupKey = IsAllDigits(slotName) ? "$" + slotName : slotName;
                    if (slots.TryGetValue(lookupKey, out var val))
                    {
                        sb.Append(val);
                    }
                    else
                    {
                        // Slot vide → placeholder.
                        sb.Append(@"\square");
                    }
                    i = e;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s) if (!char.IsDigit(c)) return false;
            return true;
        }

        private static bool AllSlotsFilled(string fragment, IReadOnlyDictionary<string, string> slots)
        {
            int i = 0;
            while (i < fragment.Length)
            {
                if (fragment[i] == '$')
                {
                    int s = i + 1;
                    int e = s;
                    while (e < fragment.Length
                        && (char.IsLetterOrDigit(fragment[e]) || fragment[e] == '_')) e++;
                    var name = fragment.Substring(s, e - s);
                    var lookupKey = IsAllDigits(name) ? "$" + name : name;
                    if (!slots.TryGetValue(lookupKey, out var v) || string.IsNullOrEmpty(v))
                        return false;
                    i = e;
                    continue;
                }
                i++;
            }
            return true;
        }

        private static int FindMatchingClose(string s, int openIdx, char open, char close)
        {
            int depth = 1;
            for (int i = openIdx + 1; i < s.Length; i++)
            {
                if (s[i] == open) depth++;
                else if (s[i] == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}
