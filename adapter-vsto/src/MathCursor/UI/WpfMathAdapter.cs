using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MathCursor.UI
{
    /// <summary>
    /// Substitutions LaTeX → LaTeX appliquées avant de passer au
    /// <see cref="WpfMath.Controls.FormulaControl"/>. WpfMath 2.1 ne couvre
    /// pas tout le vocabulaire émis par notre core (cf. audit
    /// <c>tools/audit-latex-macros.md</c>) — on traduit ici les ~10 macros
    /// manquantes vers leurs équivalents Unicode ou macros supportées.
    ///
    /// Ces substitutions sont COSMETIQUES et n'affectent QUE l'affichage
    /// popup. Le LaTeX qui part vers Word OMath (côté SuggestionService.
    /// InsertOMathAt) reste inchangé — Word's BuildUp gère nativement
    /// <c>\mathbb</c>, <c>\begin{cases}</c>, etc.
    ///
    /// Cf. ADR 2026-04-24-Feat-popup-revert-wpfmath.md.
    /// </summary>
    internal static class WpfMathAdapter
    {
        public static string Adapt(string latex)
        {
            if (string.IsNullOrEmpty(latex)) return latex ?? "";

            string s = latex;

            // 1) \mathbb{X} → caractère Unicode (WpfMath rend les chars du font math).
            //    Pour les lettres rares non-mappées, on déballe en \mathrm{X}.
            s = MathbbRegex.Replace(s, m =>
            {
                var letter = m.Groups[1].Value;
                return MathbbMap.TryGetValue(letter, out var unicode)
                    ? unicode
                    : "\\mathrm{" + letter + "}";
            });

            // 2) \widehat{X} → \hat{X}. Perd le chapeau étendu pour multi-char,
            //    acceptable visuellement.
            s = WidehatRegex.Replace(s, "\\hat{$1}");

            // 3) \overline{X} → \bar{X}. Pareil, perd la barre étendue.
            s = OverlineRegex.Replace(s, "\\bar{$1}");

            // 4) \begin{cases}…\end{cases} : empile les lignes via \stackrel,
            //    pas de vraie accolade gauche. Visuel dégradé mais lisible.
            //    Format LaTeX d'entrée : "\begin{cases} A \\ B \end{cases}".
            s = CasesRegex.Replace(s, m =>
            {
                var body = m.Groups["body"].Value.Trim();
                // \\ → @ pour découper, espaces nettoyés
                var lines = Regex.Split(body, @"\s*\\\\\s*");
                if (lines.Length == 0) return m.Value;
                // Empilement via \stackrel (au-dessus / en-dessous) — pour 2 lignes
                if (lines.Length == 2)
                    return "\\{\\stackrel{" + lines[0].Trim() + "}{" + lines[1].Trim() + "}";
                // Plus de 2 : on fait des \stackrel imbriqués
                string acc = lines[lines.Length - 1].Trim();
                for (int i = lines.Length - 2; i >= 0; i--)
                    acc = "\\stackrel{" + lines[i].Trim() + "}{" + acc + "}";
                return "\\{" + acc;
            });

            // 5) Substitutions littérales mot-à-mot (ordre préservé).
            foreach (var (from, to) in LiteralSubs)
                s = s.Replace(from, to);

            // 6) Holes (① ② ③ …) émis par le renderer lattice. WpfMath fail
            //    silencieusement à les rendre — même via \text{} car la font
            //    par défaut (Arial) ne contient pas U+2460–U+2468 et ça plante
            //    le rendu COMPLET de la formule (pas juste le hole), résultat
            //    boîte 2×2 invisible. \text[font]{...} pas supporté non plus.
            //    On bascule sur \square (carré vide standard, universellement
            //    rendu). Perd la numérotation visuelle ① vs ② mais l'élève voit
            //    bien qu'un slot manque. Côté Word OMath, le LaTeX original
            //    conserve les ronds (substitution popup-only).
            foreach (var hole in HoleGlyphs)
                s = s.Replace(hole, "\\square ");

            return s;
        }

        // ----- regex compilées -----

        private static readonly Regex MathbbRegex =
            new Regex(@"\\mathbb\{(\w)\}", RegexOptions.Compiled);

        private static readonly Regex WidehatRegex =
            new Regex(@"\\widehat\{([^{}]*)\}", RegexOptions.Compiled);

        private static readonly Regex OverlineRegex =
            new Regex(@"\\overline\{([^{}]*)\}", RegexOptions.Compiled);

        private static readonly Regex CasesRegex =
            new Regex(@"\\begin\{cases\}(?<body>.*?)\\end\{cases\}",
                RegexOptions.Compiled | RegexOptions.Singleline);

        // ----- maps -----

        // Caractères Unicode "letterlike" couvrant les ensembles classiques
        // de maths lycée/sup. Lettres rares (K/F/T/...) fallback en \mathrm.
        private static readonly Dictionary<string, string> MathbbMap =
            new Dictionary<string, string>
            {
                { "R", "ℝ" }, // ℝ
                { "N", "ℕ" }, // ℕ
                { "Z", "ℤ" }, // ℤ
                { "Q", "ℚ" }, // ℚ
                { "C", "ℂ" }, // ℂ
                { "P", "ℙ" }, // ℙ
                { "H", "ℍ" }, // ℍ
            };

        // Glyphes Hole émis par le renderer lattice (cf. LatexRenderer).
        // ⓪ pas utilisé (les holes sont indexés à partir de 1).
        private static readonly string[] HoleGlyphs =
        {
            "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧", "⑨",
        };

        // Substitutions simples (ordre important : remplacements longs d'abord).
        private static readonly (string from, string to)[] LiteralSubs = new[]
        {
            // Différence d'ensembles
            ("\\setminus", "\\backslash"),
            // Flèche fonction
            ("\\mapsto", "↦"), // ↦
            // Intégrales doubled / contour
            ("\\iint",  "∬"), // ∬
            ("\\iiint", "∭"), // ∭
            ("\\oint",  "∮"), // ∮
            // Limite sup/inf : composer deux macros supportées
            ("\\limsup", "\\lim\\sup"),
            ("\\liminf", "\\lim\\inf"),
            // Modulo : texte droit espacé
            ("\\bmod", "\\,\\mathrm{mod}\\,"),
        };
    }
}
