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

            // 1) \mathbb{X} → \mathrm{X} pour la popup. WpfMath ne supporte
            //    NI \mathbb NI \mathbf NI le caractère Unicode `ℝ` (testés :
            //    tous rendent un point placeholder). \mathrm est natively
            //    supporté et donne un X en romain droit, distinctif visuellement
            //    de la variable italique.
            //
            //    Compromis assumé : on perd la double barre dans la popup,
            //    mais la conversion finale Word OMath reste \mathbb{X} (path
            //    SuggestionService.InsertOMathAt qui ne passe pas par Adapt,
            //    Word OMath gère `\mathbb` natively → vrai ℝ avec barre).
            s = MathbbRegex.Replace(s, "\\mathrm{$1}");

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
