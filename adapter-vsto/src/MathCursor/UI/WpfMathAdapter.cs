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

            // 1) \mathbb{X} → |X pour la popup. Préfixe `|` qui simule la
            //    double barre du blackboard : `|R`, `|N`, `|Z`. Distinctif
            //    visuellement de la variable italique R/N/Z, et clair pour
            //    l'utilisateur que c'est l'ensemble. Compromis temporaire en
            //    attendant une vraie font math compatible blackboard.
            //
            //    Conversion finale Word OMath garde \mathbb{X} (vrai ℝ avec
            //    double barre) — le path SuggestionService.InsertOMathAt ne
            //    passe pas par Adapt.
            s = MathbbRegex.Replace(s, "|$1");

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

            // 4b) \begin{pmatrix}/bmatrix/vmatrix : matrices à 1 colonne (vecteurs
            //     colonnes émis par notre LatexRenderer pour `u (1 2)` etc.).
            //     WpfMath 2.1 ne supporte pas \begin{pmatrix} → on stack via
            //     \stackrel imbriqués entourés du bon délimiteur :
            //       pmatrix → ( … )
            //       bmatrix → [ … ]
            //       vmatrix → | … |
            //     Visuel dégradé (espacement \stackrel < pmatrix natif) mais
            //     identifiable comme vecteur colonne. La conversion vers Word OMath
            //     ne passe pas ici (cf. LatexToUnicodeMath), donc le rendu final
            //     dans le doc reste une vraie matrice.
            s = PmatrixRegex.Replace(s, m => StackrelDelim(m.Groups["body"].Value, "(", ")"));
            s = BmatrixRegex.Replace(s, m => StackrelDelim(m.Groups["body"].Value, "[", "]"));
            s = VmatrixRegex.Replace(s, m => StackrelDelim(m.Groups["body"].Value, "|", "|"));

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
            // Flèche fonction : \mapsto (↦ U+21A6) n'est pas dans la font math
            // de WpfMath (rend un point placeholder, idem \mathbb). On dégrade
            // en \to (flèche simple →) qui est supporté natively. La conversion
            // finale Word OMath garde \mapsto (vrai ↦ avec barre verticale).
            ("\\mapsto", "\\to"),
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
