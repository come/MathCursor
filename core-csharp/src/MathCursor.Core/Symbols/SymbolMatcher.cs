using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MathCursor.Core.Symbols;

/// <summary>
/// Reconnaît les patterns symboliques rapides (vec AB, Vx(R, alpha, ≥, ...).
/// Porté depuis archive/officejs-prototype/src/taskpane/symbols/patterns.ts.
///
/// Les patterns sont définis en table — pas de logique inline. Pour ajouter
/// un nouveau symbole, ajouter une entrée à <see cref="Patterns"/>.
/// </summary>
public static class SymbolMatcher
{
    // Ensembles : R N Z Q C → ℝ ℕ ℤ ℚ ℂ
    private static readonly Dictionary<string, string> Sets = new()
    {
        ["R"] = "\u211D",
        ["N"] = "\u2115",
        ["Z"] = "\u2124",
        ["Q"] = "\u211A",
        ["C"] = "\u2102",
    };

    private sealed class Pattern
    {
        public Regex EndAnchored { get; }   // pour FindSymbol (popup, end-anchored)
        public Regex Anywhere { get; }      // pour ReplaceAllInText (scan dans toute la zone)
        public Func<Match, IReadOnlyList<SymbolChoice>> Resolve { get; }
        // Par défaut case-SENSITIVE : nécessaire pour distinguer Delta/delta,
        // Sigma/sigma, et éviter que "inf" matche le pattern AnB (intersection).
        public Pattern(string regex, Func<Match, IReadOnlyList<SymbolChoice>> resolve, bool ignoreCase = false)
        {
            var opts = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            EndAnchored = new Regex(regex + "$", opts);
            Anywhere = new Regex(regex, opts);
            Resolve = resolve;
        }
    }

    private static SymbolChoice Simple(string label, string display, string? replacement = null) =>
        new SymbolChoice { Label = label, Display = display, Replacement = replacement ?? display };

    private static IReadOnlyList<SymbolChoice> One(string label, string display, string? replacement = null) =>
        new[] { Simple(label, display, replacement) };

    // ============================================================
    // TABLE DES PATTERNS — ordre = priorité (1er match gagne)
    // ============================================================
    private static readonly List<Pattern> Patterns = new()
    {
        // Implications / équivalences
        new Pattern(@"<=>", _ => One("équivalent", "\u27FA")),
        new Pattern(@"(?<![<>])=>", _ => One("implique", "\u27F9")),

        // Quantificateurs : Vx(R, Vx,y dans R, qq x c R, pt x app N, ...
        new Pattern(@"(?:V|pt|qq)\s*([a-zA-Z](?:\s*,\s*[a-zA-Z])*)\s*(?:\(|c|dans |in |app |de )\s*([RNZQC])",
            m => {
                var vars = m.Groups[1].Value.Replace(" ", "");
                var s = Sets.TryGetValue(m.Groups[2].Value.ToUpperInvariant(), out var sv) ? sv : m.Groups[2].Value;
                var disp = $"\u2200{vars} \u2208 {s}";
                return One("pour tout", disp);
            }),
        new Pattern(@"(?:E|ie)\s*([a-zA-Z](?:\s*,\s*[a-zA-Z])*)\s*(?:\(|c|dans |in |app |de )\s*([RNZQC])",
            m => {
                var vars = m.Groups[1].Value.Replace(" ", "");
                var s = Sets.TryGetValue(m.Groups[2].Value.ToUpperInvariant(), out var sv) ? sv : m.Groups[2].Value;
                var disp = $"\u2203{vars} \u2208 {s}";
                return One("il existe", disp);
            }),
        new Pattern(@"(?:E|ie)!\s*([a-zA-Z](?:\s*,\s*[a-zA-Z])*)",
            m => {
                var vars = m.Groups[1].Value.Replace(" ", "");
                return One("il existe un unique", $"\u2203!{vars}");
            }),

        // Appartenance / inclusion
        new Pattern(@"!(?:\(|c)\s*([RNZQC])",
            m => {
                var s = Sets.TryGetValue(m.Groups[1].Value.ToUpperInvariant(), out var sv) ? sv : m.Groups[1].Value;
                return One("n'appartient pas", $"\u2209{s}");
            }),
        new Pattern(@"\b(?:sub|inc)\s+([RNZQC])",
            m => {
                var s = Sets.TryGetValue(m.Groups[1].Value.ToUpperInvariant(), out var sv) ? sv : m.Groups[1].Value;
                return One("inclus dans", $"\u2282{s}");
            }),
        new Pattern(@"(?:\(|(?<=[^a-zA-Z])c)\s*([RNZQC])",
            m => {
                var s = Sets.TryGetValue(m.Groups[1].Value.ToUpperInvariant(), out var sv) ? sv : m.Groups[1].Value;
                return One("appartient à", $"\u2208{s}");
            }),

        // Union / intersection : AuB, AnB
        new Pattern(@"([A-Z])u([A-Z])",
            m => One("union", $"{m.Groups[1].Value}\u222A{m.Groups[2].Value}")),
        new Pattern(@"([A-Z])n([A-Z])",
            m => One("intersection", $"{m.Groups[1].Value}\u2229{m.Groups[2].Value}")),

        // Comparateurs
        new Pattern(@">=", _ => One("supérieur ou égal", "\u2265")),
        new Pattern(@"(?<!<)<=", _ => One("inférieur ou égal", "\u2264")),
        new Pattern(@"!=", _ => One("différent", "\u2260")),

        // Limites
        new Pattern(@"\blim\s*->\s*([a-zA-Z0-9]+)(\+|-)?",
            m => {
                var t = m.Groups[1].Value == "inf" ? "+\u221E" : m.Groups[1].Value;
                var sg = m.Groups[2].Value == "+" ? "\u207A" : m.Groups[2].Value == "-" ? "\u207B" : "";
                var disp = $"lim \u2192 {t}{sg}";
                return One("limite", disp);
            }),

        // Dérivées
        new Pattern(@"([a-zA-Z])''",
            m => One("dérivée seconde", $"{m.Groups[1].Value}\u2033")),
        new Pattern(@"([a-zA-Z])'",
            m => One("dérivée", $"{m.Groups[1].Value}\u2032")),

        // Géométrie : vec / seg / ang
        new Pattern(@"\bvec\s+([A-Za-z]+)",
            m => {
                var t = m.Groups[1].Value.ToUpperInvariant();
                // U+20D7 COMBINING RIGHT ARROW ABOVE — appliqué à chaque char (rendu approximatif).
                // Phase ultérieure : générer un OMath m:groupChr pour vrai vecteur.
                var withArrow = new StringBuilder();
                foreach (var c in t) { withArrow.Append(c).Append('\u20D7'); }
                return One("vecteur", withArrow.ToString());
            }),
        new Pattern(@"\bseg\s+([A-Za-z]+)",
            m => {
                var t = m.Groups[1].Value.ToUpperInvariant();
                return One("segment", $"[{t}]");
            }),
        new Pattern(@"\bang\s+([A-Za-z]+)",
            m => One("angle", $"\u2220{m.Groups[1].Value.ToUpperInvariant()}")),

        // Constantes / infinis
        new Pattern(@"-inf", _ => One("-∞", "-\u221E")),
        new Pattern(@"\+inf", _ => One("+∞", "+\u221E")),
        new Pattern(@"\binf", _ => One("∞", "\u221E")),
        new Pattern(@"\bvide", _ => One("ensemble vide", "\u2205")),

        // Lettres grecques
        new Pattern(@"\bepsilon", _ => One("ε", "\u03B5")),
        new Pattern(@"\blambda", _ => One("λ", "\u03BB")),
        new Pattern(@"\balpha", _ => One("α", "\u03B1")),
        new Pattern(@"\bdelta", _ => One("δ", "\u03B4")),
        new Pattern(@"\bDelta", _ => One("Δ", "\u0394")),
        new Pattern(@"\btheta", _ => One("θ", "\u03B8")),
        new Pattern(@"\bsigma", _ => One("σ", "\u03C3")),
        new Pattern(@"\bSigma", _ => One("Σ", "\u03A3")),
        new Pattern(@"\bomega", _ => One("ω", "\u03C9")),
        new Pattern(@"\bOmega", _ => One("Ω", "\u03A9")),
        new Pattern(@"\bgamma", _ => One("γ", "\u03B3")),
        new Pattern(@"\bbeta", _ => One("β", "\u03B2")),
        new Pattern(@"\bphi", _ => One("φ", "\u03C6")),
        new Pattern(@"\bmu", _ => One("μ", "\u03BC")),
        new Pattern(@"\bpi", _ => One("π", "\u03C0")),

        // Négation
        new Pattern(@"~", _ => One("négation", "\u00AC")),
    };

    /// <summary>
    /// Cherche un pattern symbolique en fin de <paramref name="text"/>.
    /// Retourne null si aucun pattern ne matche.
    /// Utilisé par la popup et par le matcher rapide.
    /// </summary>
    public static SymbolMatch? FindSymbol(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var trimmed = text.TrimEnd(' ', '\t', '\r', '\n');
        foreach (var p in Patterns)
        {
            var m = p.EndAnchored.Match(trimmed);
            if (m.Success)
            {
                var choices = p.Resolve(m);
                if (choices.Count > 0)
                {
                    return new SymbolMatch { Raw = m.Value, Choices = choices };
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Scan tout <paramref name="text"/> et applique TOUS les remplacements
    /// symboliques trouvés. Utilisé pour pré-traiter une zone math avant le
    /// pipeline parser/render : "alpha+beta" devient "α+β" puis traité comme
    /// une expression unique.
    /// Les overlaps sont résolus en gardant le 1er match trouvé (par priorité
    /// dans la table Patterns) à chaque position.
    /// </summary>
    public static string ReplaceAllInText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // 1. Collecter tous les matches (start, end, replacement)
        var found = new List<(int start, int end, string replacement)>();
        foreach (var p in Patterns)
        {
            foreach (Match m in p.Anywhere.Matches(text))
            {
                var choices = p.Resolve(m);
                if (choices.Count > 0)
                {
                    found.Add((m.Index, m.Index + m.Length, choices[0].Replacement));
                }
            }
        }
        if (found.Count == 0) return text;

        // 2. Trier par start, garder le 1er match à chaque position (pas d'overlap)
        found.Sort((a, b) =>
        {
            int c = a.start.CompareTo(b.start);
            return c != 0 ? c : (b.end - b.start).CompareTo(a.end - a.start); // longest at same start wins
        });
        var kept = new List<(int, int, string)>();
        int lastEnd = -1;
        foreach (var f in found)
        {
            if (f.start >= lastEnd)
            {
                kept.Add(f);
                lastEnd = f.end;
            }
        }

        // 3. Remplacer de droite à gauche pour ne pas décaler les indices
        var sb = new System.Text.StringBuilder(text);
        for (int i = kept.Count - 1; i >= 0; i--)
        {
            var (s, e, r) = kept[i];
            sb.Remove(s, e - s);
            sb.Insert(s, r);
        }
        return sb.ToString();
    }
}
