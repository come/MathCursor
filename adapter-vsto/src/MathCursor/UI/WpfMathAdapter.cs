using System.Linq;
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

            // 1) \mathbb{X} : géré désormais par MixedLatexRenderer qui
            //    substitue `\mathbb{R}` → `ℝ` (TextBlock Cambria Math) avant
            //    d'appeler ce Adapt. Si on arrive ici avec un `\mathbb{X}`
            //    persistant, c'est qu'il était nesté dans un sous-groupe
            //    (`\frac{\mathbb{R}}{2}`) — on garde l'ancienne dégradation
            //    `|X` comme fallback. Cf. brief 2026-05-06-wpfmath-fallback-renderer.
            s = MathbbRegex.Replace(s, "|$1");

            // 2) \widehat{X} → \hat{X}. Perd le chapeau étendu pour multi-char,
            //    acceptable visuellement.
            s = WidehatRegex.Replace(s, "\\hat{$1}");

            // 3) \overline{X} → \bar{X}. Pareil, perd la barre étendue.
            s = OverlineRegex.Replace(s, "\\bar{$1}");

            // 4) \begin{cases}…\end{cases} : empile les lignes via \stackrel,
            //    pas de vraie accolade gauche. Visuel dégradé mais lisible.
            //    Format LaTeX d'entrée : "\begin{cases} A \\ B \end{cases}".
            //    Depuis ADR 05-05 cases-multiline (alignement), le renderer
            //    émet `&` pour aligner les colonnes côté Word OMath. WpfMath
            //    ne supporte pas `&` hors environnement matrix → on strip
            //    les `&` (le stacking n'aligne pas de toute façon, c'est
            //    juste un visuel popup).
            s = CasesRegex.Replace(s, m =>
            {
                var body = m.Groups["body"].Value.Trim();
                // Strip les `&` (column separators) → espace simple
                body = Regex.Replace(body, @"\s*&\s*", " ");
                var lines = Regex.Split(body, @"\s*\\\\\s*");
                if (lines.Length == 0) return m.Value;
                // Empilement via \stackrel (au-dessus / en-dessous) — pour 2 lignes
                if (lines.Length == 2)
                    return "\\{\\stackrel{" + lines[0].Trim() + "}{" + lines[1].Trim() + "}";
                // 1 ligne (single-line cases) ou 3+ : stackrel imbriqués
                string acc = lines[lines.Length - 1].Trim();
                for (int i = lines.Length - 2; i >= 0; i--)
                    acc = "\\stackrel{" + lines[i].Trim() + "}{" + acc + "}";
                return "\\{" + acc;
            });

            // 4b) \begin{pmatrix}/bmatrix/vmatrix : WpfMath utilise une syntaxe
            //     DIFFÉRENTE du LaTeX standard. Cf. doc :
            //     https://github.com/sskodje/wpf-math/blob/master/docs/matrices.md
            //     - \matrix{a & b \\ c & d} : matrice SANS delim
            //     - \pmatrix{a & b \\ c & d} : matrice avec delim (square brackets
            //       selon doc, mais ambiguité → on contrôle explicit via \left/\right)
            //     Rows séparées par \\ ou \cr, cells par &.
            //
            //     Approche : on transforme \begin{pmatrix}...\end{pmatrix} en
            //     `\left( \matrix{...} \right)` pour contrôler les délimiteurs
            //     de façon prédictible et indépendante de la sémantique WpfMath
            //     `\pmatrix`. Idem bmatrix → `\left[ \matrix{} \right]`,
            //     vmatrix → `\left| \matrix{} \right|`. Avant P9f+ (2026-05-21),
            //     on stackait via \binom/\genfrac qui ne marchaient que pour
            //     matrices 1-col (= vecteurs colonnes) — désormais matrice 2D
            //     supportée nativement par WpfMath via \matrix{a & b \\ c & d}.
            s = PmatrixRegex.Replace(s, m =>
                "\\left( \\matrix{" + m.Groups["body"].Value.Trim() + "} \\right)");
            s = BmatrixRegex.Replace(s, m =>
                "\\left[ \\matrix{" + m.Groups["body"].Value.Trim() + "} \\right]");
            s = VmatrixRegex.Replace(s, m =>
                "\\left| \\matrix{" + m.Groups["body"].Value.Trim() + "} \\right|");

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

        private static readonly Regex PmatrixRegex =
            new Regex(@"\\begin\{pmatrix\}(?<body>.*?)\\end\{pmatrix\}",
                RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex BmatrixRegex =
            new Regex(@"\\begin\{bmatrix\}(?<body>.*?)\\end\{bmatrix\}",
                RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex VmatrixRegex =
            new Regex(@"\\begin\{vmatrix\}(?<body>.*?)\\end\{vmatrix\}",
                RegexOptions.Compiled | RegexOptions.Singleline);

        // Construit la représentation popup d'un vecteur colonne en utilisant
        // les macros WPF-Math supportées :
        //   - 2 éléments : \genfrac{<L>}{<R>}{0pt}{}{a}{b}  (pile sans barre,
        //     délimiteurs aux extrémités). Cas \genfrac{(}{)} = équivalent à
        //     \binom — déjà utilisé massivement dans l'ancien moteur YAML donc
        //     éprouvé.
        //   - 3+ éléments : \genfrac{<L>}{<R>}{0pt}{}{a}{<inner>} où inner est
        //     un \genfrac{}{}{0pt}{}{b}{c} sans délimiteur ni barre, récursif.
        //     Visuel : un seul jeu de délim externe, valeurs empilées dedans.
        //
        // Note V1 : ces matrices sont des vecteurs colonnes 1-colonne, pas de
        // séparateur `&` à gérer. V2 (vraies matrices 2D) demandera un autre
        // chemin (probablement passer par une représentation plus simple type
        // texte aligné, WPF-Math n'ayant pas de \matrix natif).
        private static string StackrelDelim(string body, string leftDelim, string rightDelim)
        {
            var lines = Regex.Split(body.Trim(), @"\s*\\\\\s*");

            // P9f (2026-05-21) : pour les matrices 2D émises par MatrixTemplate
            // (= contiennent `&` entre colonnes), on substitue `&` par `\quad`
            // (espace large) sur chaque ligne. WpfMath 2.1 ne supporte pas
            // \begin{array}/\matrix donc l'alignement vertical des colonnes
            // est sacrifié au profit d'un rendu lisible : `(a  b ; c  d)`
            // visuellement. Le rendu final Word OMath via LatexToUnicodeMath
            // reste une vraie matrice 2D — ce code n'affecte QUE l'aperçu popup.
            for (int i = 0; i < lines.Length; i++)
                lines[i] = Regex.Replace(lines[i], @"\s*&\s*", " \\quad ");

            if (lines.Length == 0) return leftDelim + rightDelim;
            if (lines.Length == 1) return leftDelim + lines[0].Trim() + rightDelim;

            // Stratégie de rendu popup, validée empiriquement avec WpfMath 2.1
            // (par tâtonnement come 2026-04-29) :
            //   2 éléments parens   → \binom{a}{b}                  (parens, idéal)
            //   3+ éléments parens  → \genfrac{[}{]}{0pt}{}{a}{…}   (brackets, dégradé volontaire
            //                                                       pour éviter l'imbrication moche)
            //   Autres délimiteurs  → inline, fallback texte
            //
            // \genfrac est la généralisation \binom : \binom{a}{b} = \genfrac{(}{)}{0pt}{}{a}{b}.
            // Donc si \binom marche en popup, \genfrac avec d'autres délim doit aussi.
            // L'output Word OMath (LatexToUnicodeMath → BuildUp) reste une vraie
            // matrice avec parens — le fallback ici n'affecte QUE l'aperçu popup.
            if (lines.Length == 2 && leftDelim == "(" && rightDelim == ")")
                return "\\binom{" + lines[0].Trim() + "}{" + lines[1].Trim() + "}";

            // 3+ éléments avec parens demandées : bascule en brackets stacked.
            // \genfrac{[}{]}{0pt}{}{first}{<chaîne genfrac sans délim pour le reste>}.
            // Pour les éléments du milieu/fin, on imbrique \genfrac{}{}{0pt}{}
            // (sans délim, sans barre) : visuellement c'est juste de
            // l'empilement à l'intérieur des brackets externes.
            if (leftDelim == "(" && rightDelim == ")")
            {
                string acc = lines[lines.Length - 1].Trim();
                for (int i = lines.Length - 2; i >= 1; i--)
                    acc = "\\genfrac{}{}{0pt}{}{" + lines[i].Trim() + "}{" + acc + "}";
                return "\\genfrac{[}{]}{0pt}{}{" + lines[0].Trim() + "}{" + acc + "}";
            }

            // Délimiteurs autres que parens (V2+, pas émis en V1) : fallback
            // inline avec le délim demandé. À étoffer si on émet bmatrix/vmatrix.
            return leftDelim + string.Join(", ", lines.Select(l => l.Trim())) + rightDelim;
        }

        // ----- maps -----

        // Substitutions simples (ordre important : remplacements longs d'abord).
        private static readonly (string from, string to)[] LiteralSubs = new[]
        {
            // Différence d'ensembles
            ("\\setminus", "\\backslash"),
            // Flèche fonction : `\mapsto` géré désormais par MixedLatexRenderer
            // (TextBlock Unicode ↦). Si on arrive ici avec un `\mapsto` (cas
            // nesté `\frac{\mapsto}{...}`), on dégrade en `\to` qui est
            // supporté natively par WpfMath.
            ("\\mapsto", "\\to"),
            // Intégrales doubled : `\iint` / `\iiint` gérés désormais par
            // MixedLatexRenderer (TextBlock Unicode ∬ / ∭). Substitutions
            // Unicode retirées : WpfMath ne sait pas rendre les caractères
            // Unicode (cf. brief 2026-05-06-wpfmath-fallback-renderer).
            // `\oint` reste en pass-through : WpfMath le rend natively.
            // Limite sup/inf : composer deux macros supportées
            ("\\limsup", "\\lim\\sup"),
            ("\\liminf", "\\lim\\inf"),
            // Modulo : texte droit espacé
            ("\\bmod", "\\,\\mathrm{mod}\\,"),
        };
    }
}
