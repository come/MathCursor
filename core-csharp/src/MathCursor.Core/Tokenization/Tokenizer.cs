using System.Collections.Generic;
using System.Text;

namespace MathCursor.Core.Tokenization;

/// <summary>
/// Texte → tokens catégorisés. Porté depuis
/// archive/officejs-prototype/src/taskpane/conversion/tokenizer.ts.
/// Les positions Start/End sont en code units UTF-16 (cohérent avec .NET string indexing).
/// </summary>
public static class Tokenizer
{
    // Grec minuscules / majuscules
    private static readonly HashSet<int> GreekLower = StringToCodepointSet(
        "αβγδεζηθικλμνξπρσςτυφχψω");
    private static readonly HashSet<int> GreekUpper = StringToCodepointSet(
        "ΑΒΓΔΕΖΗΘΙΚΛΜΝΞΠΡΣΤΥΦΧΨΩ");

    // Symboles math Unicode
    private static readonly HashSet<int> MathSymbols = StringToCodepointSet(
        "∫∑∏∂∇∞≤≥≠≈∈∉⊂⊃⊆⊇∪∩±∓×÷∧∨¬→⟹⟺∀∃∄∅ℕℤℚℝℂ");

    private static readonly HashSet<int> OperatorChars =
        new() { '+', '-', '*', '/', '^', '=', '<', '>', '!' };

    private static readonly HashSet<int> ParenChars =
        new() { '(', ')', '[', ']', '{', '}' };

    private static HashSet<int> StringToCodepointSet(string s)
    {
        var set = new HashSet<int>();
        int i = 0;
        while (i < s.Length)
        {
            int cp = char.ConvertToUtf32(s, i);
            set.Add(cp);
            i += cp > 0xFFFF ? 2 : 1;
        }
        return set;
    }

    // ============================================================
    // NORMALISATION (math italic Unicode → ASCII)
    // ============================================================

    public static string NormalizeCodepoint(int cp)
    {
        // Math Italic uppercase A-Z : U+1D434..U+1D44D
        if (cp >= 0x1D434 && cp <= 0x1D44D) return ((char)(65 + cp - 0x1D434)).ToString();
        // Math Italic lowercase a-g : U+1D44E..U+1D454
        if (cp >= 0x1D44E && cp <= 0x1D454) return ((char)(97 + cp - 0x1D44E)).ToString();
        // Math Italic h (Planck) : U+210E
        if (cp == 0x210E) return "h";
        // Math Italic lowercase i-z : U+1D456..U+1D467
        if (cp >= 0x1D456 && cp <= 0x1D467) return ((char)(105 + cp - 0x1D456)).ToString();
        // Math Bold uppercase : U+1D400..U+1D419
        if (cp >= 0x1D400 && cp <= 0x1D419) return ((char)(65 + cp - 0x1D400)).ToString();
        // Math Bold lowercase : U+1D41A..U+1D433
        if (cp >= 0x1D41A && cp <= 0x1D433) return ((char)(97 + cp - 0x1D41A)).ToString();
        // Math Bold Italic uppercase : U+1D468..U+1D481
        if (cp >= 0x1D468 && cp <= 0x1D481) return ((char)(65 + cp - 0x1D468)).ToString();
        // Math Bold Italic lowercase : U+1D482..U+1D49B
        if (cp >= 0x1D482 && cp <= 0x1D49B) return ((char)(97 + cp - 0x1D482)).ToString();
        // × (multiplication sign) → *
        if (cp == 0x00D7) return "*";
        // − (minus sign) → -
        if (cp == 0x2212) return "-";
        // en-dash / em-dash → -
        if (cp == 0x2013 || cp == 0x2014) return "-";

        return char.ConvertFromUtf32(cp);
    }

    // ============================================================
    // CATÉGORISATION
    // ============================================================

    private static HashSet<UnicodeCategory> ClassifyCodepoint(int cp)
    {
        var cats = new HashSet<UnicodeCategory>();

        // Math italic (renvoyé en "letter" pour que les variables OMath s'agrègent)
        if ((cp >= 0x1D400 && cp <= 0x1D7FF) || cp == 0x210E)
        {
            cats.Add(UnicodeCategory.Letter);
            return cats;
        }

        if (GreekLower.Contains(cp) || GreekUpper.Contains(cp)) { cats.Add(UnicodeCategory.GreekLetter); return cats; }
        if (MathSymbols.Contains(cp)) { cats.Add(UnicodeCategory.MathSymbol); return cats; }
        if (OperatorChars.Contains(cp)) { cats.Add(UnicodeCategory.Operator); return cats; }
        if (ParenChars.Contains(cp)) { cats.Add(UnicodeCategory.Paren); return cats; }
        if (cp == ',') { cats.Add(UnicodeCategory.Comma); return cats; }
        if (cp == '.') { cats.Add(UnicodeCategory.Dot); return cats; }

        // Char BMP : utiliser les helpers char.*
        if (cp <= 0xFFFF)
        {
            char c = (char)cp;
            if (char.IsDigit(c)) { cats.Add(UnicodeCategory.Digit); return cats; }
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) { cats.Add(UnicodeCategory.Letter); return cats; }
            if (char.IsLetter(c)) { cats.Add(UnicodeCategory.Letter); return cats; } // lettres accentuées
            if (char.IsWhiteSpace(c)) { cats.Add(UnicodeCategory.Whitespace); return cats; }
        }

        cats.Add(UnicodeCategory.Unknown);
        return cats;
    }

    // ============================================================
    // TOKENIZER
    // ============================================================

    public static IReadOnlyList<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < text.Length)
        {
            int cp = char.ConvertToUtf32(text, i);
            int cpLen = cp > 0xFFFF ? 2 : 1;
            var cats = ClassifyCodepoint(cp);

            // Whitespace : runs
            if (cats.Contains(UnicodeCategory.Whitespace))
            {
                int start = i;
                var sb = new StringBuilder();
                while (i < text.Length)
                {
                    int cp2 = char.ConvertToUtf32(text, i);
                    int len2 = cp2 > 0xFFFF ? 2 : 1;
                    if (!ClassifyCodepoint(cp2).Contains(UnicodeCategory.Whitespace)) break;
                    sb.Append(text, i, len2);
                    i += len2;
                }
                tokens.Add(new Token
                {
                    Text = sb.ToString(),
                    Normalized = " ",
                    Start = start,
                    End = i,
                    Categories = new HashSet<UnicodeCategory> { UnicodeCategory.Whitespace },
                });
                continue;
            }

            // Nombre : digits + dot décimal
            if (cats.Contains(UnicodeCategory.Digit))
            {
                int start = i;
                var sb = new StringBuilder();
                var accCats = new HashSet<UnicodeCategory> { UnicodeCategory.Digit };
                while (i < text.Length)
                {
                    int cp2 = char.ConvertToUtf32(text, i);
                    int len2 = cp2 > 0xFFFF ? 2 : 1;
                    var cc = ClassifyCodepoint(cp2);
                    if (!cc.Contains(UnicodeCategory.Digit) && !cc.Contains(UnicodeCategory.Dot)) break;
                    sb.Append(text, i, len2);
                    accCats.UnionWith(cc);
                    i += len2;
                }
                tokens.Add(new Token
                {
                    Text = sb.ToString(),
                    Normalized = sb.ToString(),
                    Start = start,
                    End = i,
                    Categories = accCats,
                });
                continue;
            }

            // Lettres (mot entier, inclut math italic + grec)
            if (cats.Contains(UnicodeCategory.Letter) || cats.Contains(UnicodeCategory.GreekLetter))
            {
                int start = i;
                var raw = new StringBuilder();
                var norm = new StringBuilder();
                var accCats = new HashSet<UnicodeCategory>();
                while (i < text.Length)
                {
                    int cp2 = char.ConvertToUtf32(text, i);
                    int len2 = cp2 > 0xFFFF ? 2 : 1;
                    var cc = ClassifyCodepoint(cp2);
                    if (!cc.Contains(UnicodeCategory.Letter) && !cc.Contains(UnicodeCategory.GreekLetter)) break;
                    raw.Append(text, i, len2);
                    norm.Append(NormalizeCodepoint(cp2));
                    accCats.UnionWith(cc);
                    i += len2;
                }
                tokens.Add(new Token
                {
                    Text = raw.ToString(),
                    Normalized = norm.ToString(),
                    Start = start,
                    End = i,
                    Categories = accCats,
                });
                continue;
            }

            // Symbole math Unicode
            if (cats.Contains(UnicodeCategory.MathSymbol))
            {
                tokens.Add(new Token
                {
                    Text = text.Substring(i, cpLen),
                    Normalized = NormalizeCodepoint(cp),
                    Start = i,
                    End = i + cpLen,
                    Categories = cats,
                });
                i += cpLen;
                continue;
            }

            // Opérateur (avec groupement multi-char : <=, >=, !=, =>, <=>, ->)
            if (cats.Contains(UnicodeCategory.Operator))
            {
                int start = i;
                string op = text.Substring(i, cpLen);
                i += cpLen;
                if (i < text.Length)
                {
                    char next = text[i];
                    string two = op + next;
                    if (two == "<=" || two == ">=" || two == "!=" || two == "=>")
                    {
                        op = two; i++;
                        if (op == "<=" && i < text.Length && text[i] == '>')
                        {
                            op = "<=>"; i++;
                        }
                    }
                    else if (op == "-" && next == '>')
                    {
                        op = "->"; i++;
                    }
                }
                tokens.Add(new Token
                {
                    Text = op,
                    Normalized = op,
                    Start = start,
                    End = i,
                    Categories = new HashSet<UnicodeCategory> { UnicodeCategory.Operator },
                });
                continue;
            }

            // Paren / bracket / comma / dot / fallback : 1 codepoint
            tokens.Add(new Token
            {
                Text = text.Substring(i, cpLen),
                Normalized = NormalizeCodepoint(cp),
                Start = i,
                End = i + cpLen,
                Categories = cats,
            });
            i += cpLen;
        }

        return tokens;
    }
}
