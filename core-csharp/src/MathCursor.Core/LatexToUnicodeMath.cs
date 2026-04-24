using System.Collections.Generic;
using System.Text;

namespace MathCursor.Core
{
    /// <summary>
    /// Convertit un LaTeX simple en UnicodeMath — le format que Word interprète
    /// nativement via <c>OMaths.BuildUp()</c>.
    ///
    /// Couvre les cas produits par notre moteur de patterns :
    /// - <c>\frac{A}{B}</c> → <c>(A)/(B)</c>
    /// - <c>\sqrt{X}</c> → <c>√(X)</c>
    /// - <c>x^{abc}</c> → <c>x^(abc)</c>, <c>x_{abc}</c> → <c>x_(abc)</c>
    /// - Lettres grecques : <c>\alpha</c> → α, etc.
    /// - Ensembles : <c>\mathbb{R}</c> → ℝ, <c>\forall</c> → ∀, <c>\in</c> → ∈
    /// - Opérateurs : <c>\to</c> → →, <c>\leq</c> → ≤, <c>\cdot</c> → ⋅, <c>\circ</c> → ∘
    /// - Noms de fonctions : <c>\sin</c>, <c>\lim</c>, <c>\ln</c>… → déballés (Word
    ///   reconnaît "lim", "sin", etc. et les passe en droit automatiquement).
    /// - <c>\mathrm{txt}</c>, <c>\operatorname{txt}</c> → <c>txt</c> (wrapper supprimé).
    ///
    /// Pas un convertisseur LaTeX complet — juste suffisant pour le vocabulaire
    /// que notre moteur produit. Les macros non reconnues sont laissées telles quelles.
    /// </summary>
    public static class LatexToUnicodeMath
    {
        public static string Convert(string latex)
        {
            if (string.IsNullOrEmpty(latex)) return latex ?? "";

            // 1) Remplacements de commandes structurelles à argument entre accolades.
            //    On scanne manuellement pour respecter la profondeur des accolades.
            var s = ConvertStructural(latex);

            // 2) Remplacements littéraux : lettres grecques, symboles, relations.
            foreach (var kv in LiteralReplacements)
                s = s.Replace(kv.Key, kv.Value);

            // 3) Auto-wrap pour x^2 / x_n : UnicodeMath ne wrap pas tout seul,
            //    mais Word gère ^ et _ sur un seul caractère — on ne touche donc
            //    pas aux formes simples. Les formes "x^{abc}" sont déjà traitées
            //    par ConvertStructural qui transforme {abc} en (abc).

            return s;
        }

        // ------------------------------------------------------------
        // Étape 1 : commandes à argument(s) entre accolades
        // ------------------------------------------------------------

        private static string ConvertStructural(string src)
        {
            var sb = new StringBuilder(src.Length);
            int i = 0;
            while (i < src.Length)
            {
                if (src[i] == '\\')
                {
                    // Lit le nom de la commande
                    int nameStart = i + 1;
                    int nameEnd = nameStart;
                    while (nameEnd < src.Length && char.IsLetter(src[nameEnd])) nameEnd++;
                    string cmd = src.Substring(nameStart, nameEnd - nameStart);
                    int after = nameEnd;

                    // Dispatch par nom
                    if (cmd == "frac" || cmd == "dfrac" || cmd == "tfrac")
                    {
                        if (TryReadBracedArg(src, after, out string a, out int afterA)
                            && TryReadBracedArg(src, afterA, out string b, out int afterB))
                        {
                            string ca = ConvertStructural(a);
                            string cb = ConvertStructural(b);
                            sb.Append("(").Append(ca).Append(")/(").Append(cb).Append(")");
                            i = afterB;
                            continue;
                        }
                    }
                    else if (cmd == "sqrt")
                    {
                        // \sqrt[N]{X} optionnel : indice N
                        int j = after;
                        string? nArg = null;
                        if (j < src.Length && src[j] == '[')
                        {
                            int close = src.IndexOf(']', j + 1);
                            if (close > j)
                            {
                                nArg = src.Substring(j + 1, close - j - 1);
                                j = close + 1;
                            }
                        }
                        if (TryReadBracedArg(src, j, out string arg, out int afterArg))
                        {
                            string carg = ConvertStructural(arg);
                            if (nArg != null)
                                sb.Append("√(").Append(ConvertStructural(nArg)).Append("&").Append(carg).Append(")");
                            else
                                sb.Append("√(").Append(carg).Append(")");
                            i = afterArg;
                            continue;
                        }
                    }
                    else if (cmd == "binom")
                    {
                        if (TryReadBracedArg(src, after, out string a, out int afterA)
                            && TryReadBracedArg(src, afterA, out string b, out int afterB))
                        {
                            // UnicodeMath pour coefficient binomial : (a¦b) avec ¦ = U+00A6
                            sb.Append("(")
                              .Append(ConvertStructural(a))
                              .Append("¦")
                              .Append(ConvertStructural(b))
                              .Append(")");
                            i = afterB;
                            continue;
                        }
                    }
                    else if (cmd == "mathbb" || cmd == "mathcal" || cmd == "mathrm"
                             || cmd == "mathbf" || cmd == "mathsf" || cmd == "operatorname"
                             || cmd == "text")
                    {
                        if (TryReadBracedArg(src, after, out string arg, out int afterArg))
                        {
                            // mathbb est géré plus bas via LiteralReplacements (R→ℝ).
                            // Pour les autres, on déballe simplement : le LaTeX des
                            // opérateurs nommés ("Vect", "Ker", "tr"…) devient du texte
                            // droit dans Word (qui reconnaît les séquences alpha comme
                            // des opérateurs automatiquement).
                            if (cmd == "mathbb" && arg.Length == 1 && SetLetterMap.TryGetValue(arg, out var setChar))
                            {
                                sb.Append(setChar);
                            }
                            else
                            {
                                sb.Append(ConvertStructural(arg));
                            }
                            i = afterArg;
                            continue;
                        }
                    }
                    else if (cmd == "vec" || cmd == "bar" || cmd == "tilde"
                             || cmd == "hat" || cmd == "dot" || cmd == "ddot"
                             || cmd == "overline" || cmd == "underline")
                    {
                        if (TryReadBracedArg(src, after, out string arg, out int afterArg))
                        {
                            // UnicodeMath : \vec(x) ou x + combining — on garde la
                            // commande LaTeX, Word reconnaît \vec et quelques autres.
                            // Fallback : on écrit "cmd(arg)".
                            string unicodeMap = AccentMap.TryGetValue(cmd, out var m) ? m : "\\" + cmd;
                            sb.Append(unicodeMap).Append("(").Append(ConvertStructural(arg)).Append(")");
                            i = afterArg;
                            continue;
                        }
                    }
                    // Si on n'a pas dispatché : on recopie la commande telle quelle,
                    // les remplacements littéraux se chargeront des lettres grecques etc.
                    sb.Append('\\').Append(cmd);
                    i = after;
                    continue;
                }

                // Exposant / indice avec accolades : x^{abc} → x^(abc), x_{ij} → x_(ij)
                if ((src[i] == '^' || src[i] == '_') && i + 1 < src.Length && src[i + 1] == '{')
                {
                    sb.Append(src[i]);
                    if (TryReadBracedArg(src, i + 1, out string arg, out int afterArg))
                    {
                        sb.Append("(").Append(ConvertStructural(arg)).Append(")");
                        i = afterArg;
                        continue;
                    }
                }

                sb.Append(src[i]);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>Lit <c>{ ... }</c> à la position donnée, gère la profondeur.</summary>
        private static bool TryReadBracedArg(string src, int pos, out string arg, out int afterArg)
        {
            arg = "";
            afterArg = pos;
            if (pos >= src.Length || src[pos] != '{') return false;
            int depth = 1;
            int start = pos + 1;
            int i = start;
            while (i < src.Length && depth > 0)
            {
                if (src[i] == '\\' && i + 1 < src.Length) { i += 2; continue; }
                if (src[i] == '{') depth++;
                else if (src[i] == '}') { depth--; if (depth == 0) break; }
                i++;
            }
            if (depth != 0) return false;
            arg = src.Substring(start, i - start);
            afterArg = i + 1;
            return true;
        }

        // ------------------------------------------------------------
        // Étape 2 : remplacements littéraux (commande → caractère unicode)
        // ------------------------------------------------------------

        // Lettres grecques, relations, ensembles. Tout passe par Replace — donc
        // l'ordre compte : versions longues d'abord pour éviter que "\in" matche
        // "\int" etc.
        private static readonly List<KeyValuePair<string, string>> LiteralReplacements =
            new List<KeyValuePair<string, string>>
            {
                // Relations multi-char (prioritaires)
                new KeyValuePair<string, string>("\\leqslant", "≤"),
                new KeyValuePair<string, string>("\\geqslant", "≥"),
                new KeyValuePair<string, string>("\\Leftrightarrow", "⇔"),
                new KeyValuePair<string, string>("\\Rightarrow", "⇒"),
                new KeyValuePair<string, string>("\\leftarrow", "←"),
                new KeyValuePair<string, string>("\\rightarrow", "→"),
                new KeyValuePair<string, string>("\\longrightarrow", "⟶"),
                new KeyValuePair<string, string>("\\infty", "∞"),
                new KeyValuePair<string, string>("\\forall", "∀"),
                new KeyValuePair<string, string>("\\exists", "∃"),
                new KeyValuePair<string, string>("\\setminus", "∖"),
                new KeyValuePair<string, string>("\\emptyset", "∅"),
                new KeyValuePair<string, string>("\\partial", "∂"),
                new KeyValuePair<string, string>("\\nabla", "∇"),
                new KeyValuePair<string, string>("\\times", "×"),
                new KeyValuePair<string, string>("\\cdot", "⋅"),
                new KeyValuePair<string, string>("\\otimes", "⊗"),
                new KeyValuePair<string, string>("\\oplus", "⊕"),
                new KeyValuePair<string, string>("\\approx", "≈"),
                new KeyValuePair<string, string>("\\propto", "∝"),
                new KeyValuePair<string, string>("\\equiv", "≡"),
                new KeyValuePair<string, string>("\\triangle", "△"),
                new KeyValuePair<string, string>("\\parallel", "∥"),
                new KeyValuePair<string, string>("\\coloneqq", "≔"),
                new KeyValuePair<string, string>("\\subseteq", "⊆"),
                new KeyValuePair<string, string>("\\supseteq", "⊇"),
                new KeyValuePair<string, string>("\\subset", "⊂"),
                new KeyValuePair<string, string>("\\supset", "⊃"),
                new KeyValuePair<string, string>("\\limsup", "limsup"),
                new KeyValuePair<string, string>("\\liminf", "liminf"),
                new KeyValuePair<string, string>("\\iint", "∬"),
                new KeyValuePair<string, string>("\\iiint", "∭"),
                new KeyValuePair<string, string>("\\oint", "∮"),
                new KeyValuePair<string, string>("\\bmod", "mod"),
                new KeyValuePair<string, string>("\\pmod", "mod"),
                new KeyValuePair<string, string>("\\mid", "∣"),
                new KeyValuePair<string, string>("\\nmid", "∤"),
                new KeyValuePair<string, string>("\\iff", "⇔"),
                new KeyValuePair<string, string>("\\wedge", "∧"),
                new KeyValuePair<string, string>("\\lor", "∨"),
                new KeyValuePair<string, string>("\\land", "∧"),
                new KeyValuePair<string, string>("\\neg", "¬"),
                new KeyValuePair<string, string>("\\Gamma", "Γ"),
                new KeyValuePair<string, string>("\\Delta", "Δ"),
                new KeyValuePair<string, string>("\\Theta", "Θ"),
                new KeyValuePair<string, string>("\\Lambda", "Λ"),
                new KeyValuePair<string, string>("\\Xi", "Ξ"),
                new KeyValuePair<string, string>("\\Pi", "Π"),
                new KeyValuePair<string, string>("\\Sigma", "Σ"),
                new KeyValuePair<string, string>("\\Phi", "Φ"),
                new KeyValuePair<string, string>("\\Psi", "Ψ"),
                new KeyValuePair<string, string>("\\Omega", "Ω"),
                new KeyValuePair<string, string>("\\alpha", "α"),
                new KeyValuePair<string, string>("\\beta", "β"),
                new KeyValuePair<string, string>("\\gamma", "γ"),
                new KeyValuePair<string, string>("\\delta", "δ"),
                new KeyValuePair<string, string>("\\epsilon", "ε"),
                new KeyValuePair<string, string>("\\varepsilon", "ε"),
                new KeyValuePair<string, string>("\\zeta", "ζ"),
                new KeyValuePair<string, string>("\\eta", "η"),
                new KeyValuePair<string, string>("\\theta", "θ"),
                new KeyValuePair<string, string>("\\iota", "ι"),
                new KeyValuePair<string, string>("\\kappa", "κ"),
                new KeyValuePair<string, string>("\\lambda", "λ"),
                new KeyValuePair<string, string>("\\mu", "μ"),
                new KeyValuePair<string, string>("\\nu", "ν"),
                new KeyValuePair<string, string>("\\xi", "ξ"),
                new KeyValuePair<string, string>("\\pi", "π"),
                new KeyValuePair<string, string>("\\rho", "ρ"),
                new KeyValuePair<string, string>("\\sigma", "σ"),
                new KeyValuePair<string, string>("\\tau", "τ"),
                new KeyValuePair<string, string>("\\upsilon", "υ"),
                new KeyValuePair<string, string>("\\phi", "φ"),
                new KeyValuePair<string, string>("\\varphi", "φ"),
                new KeyValuePair<string, string>("\\chi", "χ"),
                new KeyValuePair<string, string>("\\psi", "ψ"),
                new KeyValuePair<string, string>("\\omega", "ω"),
                // Relations 2-char
                new KeyValuePair<string, string>("\\leq", "≤"),
                new KeyValuePair<string, string>("\\geq", "≥"),
                new KeyValuePair<string, string>("\\neq", "≠"),
                new KeyValuePair<string, string>("\\sim", "∼"),
                new KeyValuePair<string, string>("\\in", "∈"),
                new KeyValuePair<string, string>("\\notin", "∉"),
                new KeyValuePair<string, string>("\\cup", "∪"),
                new KeyValuePair<string, string>("\\cap", "∩"),
                new KeyValuePair<string, string>("\\pm", "±"),
                new KeyValuePair<string, string>("\\mp", "∓"),
                new KeyValuePair<string, string>("\\to", "→"),
                new KeyValuePair<string, string>("\\perp", "⊥"),
                new KeyValuePair<string, string>("\\ker", "ker"),
                new KeyValuePair<string, string>("\\det", "det"),
                new KeyValuePair<string, string>("\\gcd", "gcd"),
                new KeyValuePair<string, string>("\\dim", "dim"),
                new KeyValuePair<string, string>("\\arg", "arg"),
                new KeyValuePair<string, string>("\\exp", "exp"),
                new KeyValuePair<string, string>("\\ln", "ln"),
                new KeyValuePair<string, string>("\\log", "log"),
                new KeyValuePair<string, string>("\\sin", "sin"),
                new KeyValuePair<string, string>("\\cos", "cos"),
                new KeyValuePair<string, string>("\\tan", "tan"),
                new KeyValuePair<string, string>("\\cot", "cot"),
                new KeyValuePair<string, string>("\\sec", "sec"),
                new KeyValuePair<string, string>("\\csc", "csc"),
                new KeyValuePair<string, string>("\\sinh", "sinh"),
                new KeyValuePair<string, string>("\\cosh", "cosh"),
                new KeyValuePair<string, string>("\\tanh", "tanh"),
                new KeyValuePair<string, string>("\\arcsin", "arcsin"),
                new KeyValuePair<string, string>("\\arccos", "arccos"),
                new KeyValuePair<string, string>("\\arctan", "arctan"),
                new KeyValuePair<string, string>("\\lim", "lim"),
                new KeyValuePair<string, string>("\\sum", "∑"),
                new KeyValuePair<string, string>("\\prod", "∏"),
                new KeyValuePair<string, string>("\\int", "∫"),
                new KeyValuePair<string, string>("\\,", " "),
                new KeyValuePair<string, string>("\\;", " "),
                new KeyValuePair<string, string>("\\:", " "),
                new KeyValuePair<string, string>("\\ ", " "),
                new KeyValuePair<string, string>("\\{", "{"),
                new KeyValuePair<string, string>("\\}", "}"),
            };

        private static readonly Dictionary<string, string> SetLetterMap =
            new Dictionary<string, string>
            {
                { "R", "ℝ" }, { "N", "ℕ" }, { "Z", "ℤ" }, { "Q", "ℚ" }, { "C", "ℂ" },
                { "K", "𝕂" }, { "P", "ℙ" }, { "F", "𝔽" },
            };

        private static readonly Dictionary<string, string> AccentMap =
            new Dictionary<string, string>
            {
                { "vec", "\\vec" },
                { "hat", "\\hat" },
                { "bar", "\\bar" },
                { "tilde", "\\tilde" },
                { "dot", "\\dot" },
                { "ddot", "\\ddot" },
                { "overline", "\\overline" },
                { "underline", "\\underline" },
            };
    }
}
