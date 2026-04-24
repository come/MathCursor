using System;
using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Core.PatternEngine
{
    /// <summary>
    /// Parse la chaîne <c>match:</c> d'un pattern en séquence d'éléments.
    ///
    /// Syntaxe :
    /// - <c>TOKEN</c> — token canonique nommé (ex. <c>LIMIT</c>)
    /// - <c>lowercase</c> — littéral (match contre Raw, case-insensitive)
    /// - <c>SLOT:name</c> — slot typé (IDENT, EXPR, NUMBER, EXPR_LIST)
    /// - <c>(A|B|C):name</c> — alternative capturée
    /// - suffixe <c>?</c> — optionnel
    /// </summary>
    public sealed class MatchElement
    {
        public ElementKind Kind { get; set; }
        /// <summary>Pour LiteralToken/LiteralWord : le texte à matcher. Pour Slot/Alt : vide.</summary>
        public string Literal { get; set; } = "";
        /// <summary>Pour Slot/Alt : nom de capture (ex. "f", "x", "target").</summary>
        public string CaptureName { get; set; } = "";
        /// <summary>Pour Slot : type (IDENT, EXPR, NUMBER, EXPR_LIST).</summary>
        public SlotType Slot { get; set; }
        /// <summary>Pour Alternative : liste des token names possibles.</summary>
        public IReadOnlyList<string> Alternatives { get; set; } = Array.Empty<string>();
        public bool Optional { get; set; }

        public override string ToString() => Kind switch
        {
            ElementKind.LiteralToken => Literal + (Optional ? "?" : ""),
            ElementKind.LiteralWord => Literal + (Optional ? "?" : ""),
            ElementKind.Slot => $"{Slot}:{CaptureName}" + (Optional ? "?" : ""),
            ElementKind.Alternative => $"({string.Join("|", Alternatives)}):{CaptureName}" + (Optional ? "?" : ""),
            _ => "?"
        };
    }

    public enum ElementKind { LiteralToken, LiteralWord, Slot, Alternative, Space }
    public enum SlotType { Ident, Expr, Number, ExprList, IdentSeq, EquationList, Atom, IdentUpperPair, IdentUpperTriple, VxShort, CfShort, SetAtom, ListParams, Interval, IdentBar, DfShort, CoordPoint }

    public static class MatchDslParser
    {
        public static List<MatchElement> Parse(string dsl)
        {
            var elements = new List<MatchElement>();
            int i = 0;
            while (i < dsl.Length)
            {
                if (char.IsWhiteSpace(dsl[i])) { i++; continue; }

                // Alternative : (A|B|C):name
                if (dsl[i] == '(')
                {
                    int close = dsl.IndexOf(')', i);
                    if (close < 0) throw new FormatException($"Parenthèse non fermée dans match: '{dsl}'");
                    var inner = dsl.Substring(i + 1, close - i - 1);
                    var alts = inner.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    i = close + 1;
                    string name = "";
                    if (i < dsl.Length && dsl[i] == ':')
                    {
                        i++;
                        int start = i;
                        while (i < dsl.Length && (char.IsLetterOrDigit(dsl[i]) || dsl[i] == '_')) i++;
                        name = dsl.Substring(start, i - start);
                    }
                    bool opt = i < dsl.Length && dsl[i] == '?';
                    if (opt) i++;
                    elements.Add(new MatchElement
                    {
                        Kind = ElementKind.Alternative,
                        Alternatives = alts,
                        CaptureName = name,
                        Optional = opt,
                    });
                    continue;
                }

                // Mot (token name, literal, ou slot)
                int wordStart = i;
                while (i < dsl.Length && !char.IsWhiteSpace(dsl[i]) && dsl[i] != '?' && dsl[i] != ':' && dsl[i] != '(') i++;
                string word = dsl.Substring(wordStart, i - wordStart);

                string? capture = null;
                if (i < dsl.Length && dsl[i] == ':')
                {
                    i++;
                    int nameStart = i;
                    while (i < dsl.Length && (char.IsLetterOrDigit(dsl[i]) || dsl[i] == '_')) i++;
                    capture = dsl.Substring(nameStart, i - nameStart);
                }

                bool optional = i < dsl.Length && dsl[i] == '?';
                if (optional) i++;

                if (capture != null)
                {
                    // Slot : word = type
                    var slotType = ParseSlotType(word);
                    elements.Add(new MatchElement
                    {
                        Kind = ElementKind.Slot,
                        Slot = slotType,
                        CaptureName = capture,
                        Optional = optional,
                    });
                }
                else if (word == "SPACE")
                {
                    // Élément spécial : matche une frontière de whitespace (token avec
                    // HadSpaceBefore=true). SPACE? = optionnel, SPACE = strict.
                    elements.Add(new MatchElement
                    {
                        Kind = ElementKind.Space,
                        Literal = "",
                        Optional = optional,
                    });
                }
                else
                {
                    bool isUpper = word.Length > 0 && word.All(ch => !char.IsLetter(ch) || char.IsUpper(ch));
                    elements.Add(new MatchElement
                    {
                        Kind = isUpper ? ElementKind.LiteralToken : ElementKind.LiteralWord,
                        Literal = word,
                        Optional = optional,
                    });
                }
            }
            return elements;
        }

        private static SlotType ParseSlotType(string word)
        {
            return word switch
            {
                "IDENT" => SlotType.Ident,
                "EXPR" => SlotType.Expr,
                "NUMBER" => SlotType.Number,
                "EXPR_LIST" => SlotType.ExprList,
                "IDENT_SEQ" => SlotType.IdentSeq,
                "EQUATION_LIST" => SlotType.EquationList,
                "ATOM" => SlotType.Atom,
                "IDENT_UPPER_PAIR" => SlotType.IdentUpperPair,
                "IDENT_UPPER_TRIPLE" => SlotType.IdentUpperTriple,
                "VX_SHORT" => SlotType.VxShort,
                "CF_SHORT" => SlotType.CfShort,
                "SET_ATOM" => SlotType.SetAtom,
                "LIST_PARAMS" => SlotType.ListParams,
                "INTERVAL" => SlotType.Interval,
                "IDENT_BAR" => SlotType.IdentBar,
                "DF_SHORT" => SlotType.DfShort,
                "COORD_POINT" => SlotType.CoordPoint,
                _ => SlotType.Ident,
            };
        }
    }
}
