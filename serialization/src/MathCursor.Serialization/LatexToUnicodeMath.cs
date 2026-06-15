using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MathCursor.Serialization
{
    /// <summary>
    /// Convertit un LaTeX simple en UnicodeMath — le format que Word interprète
    /// nativement via <c>OMaths.BuildUp()</c>.
    ///
    /// Couvre les structures émises par notre <c>LatexRenderer</c> :
    /// - <c>\frac{A}{B}</c> → <c>A/B</c> (single char) ou <c>(A)/(B)</c> (multi)
    /// - <c>\sqrt{X}</c> → <c>√(X)</c>
    /// - <c>x^{2}</c> → <c>x^2</c>, <c>x^{abc}</c> → <c>x^(abc)</c>
    /// - <c>x_{n}</c> → <c>x_n</c>, <c>x_{ij}</c> → <c>x_(ij)</c>
    /// - Lettres grecques, ensembles, opérateurs : remplacements littéraux
    /// - Accents : <c>\vec{u}</c> → <c>u⃗</c> (combining unicode)
    /// - Environnements : <c>\begin{pmatrix}</c>, <c>\begin{cases}</c>
    ///
    /// Stratégie (cf. ADR 2026-04-30-Fix-latex-to-unicodemath-refactor) :
    /// approche en passe unique récursive avec contexte voisin droit. Émet
    /// les arguments en mode <see cref="EmitArg"/> qui décide single-char
    /// shortcut (pas de parens) vs multi-char (parens). Après une fraction
    /// multi-char, insère un espace si un token tight suit (anti-absorption).
    /// </summary>
    public static class LatexToUnicodeMath
    {
        public static string Convert(string latex)
        {
            if (string.IsNullOrEmpty(latex)) return latex ?? "";

            // 0) Environnements LaTeX \begin{...}...\end{...}.
            //    Pré-passe regex (matrices, cases) avant le scan structurel.
            var s = ConvertEnvironments(latex);
            // 1) Scan récursif des commandes structurelles + remplacements
            //    littéraux appliqués à la sortie de chaque appel.
            return ConvertSequence(s);
        }

        // ============================================================
        // Étape 0 : environnements LaTeX \begin{...}…\end{...}
        // ============================================================

        // \begin{cases} A \\ B \end{cases} → "{█(A@B)┤"
        // █ (U+2588) démarre une pile d'équations, @ sépare les lignes,
        // ┤ (U+2524) ferme sans accolade droite visible — c'est la syntaxe
        // UnicodeMath que Word transforme en {: (système avec accolade gauche).
        private static readonly Regex CasesEnvironmentRegex = new Regex(
            @"\\begin\{cases\}(?<body>.*?)\\end\{cases\}",
            RegexOptions.Singleline | RegexOptions.Compiled);

        // Matrices : Word's UnicodeMath utilise ■ (U+25A0 BLACK SQUARE) comme
        // marqueur de matrice. `&` sépare les colonnes (déjà même char en LaTeX),
        // `\\` (LaTeX) sépare les lignes → `@`. Les délimiteurs sont posés autour :
        //   pmatrix → `(■(…))` (parens)
        //   bmatrix → `[■(…)]` (crochets)
        //   vmatrix → `|■(…)|` (déterminant)
        //   matrix  → `■(…)` (sans délimiteur)
        // BuildUp parse ça nativement et produit l'OMath structure matricielle.
        private static readonly Regex PmatrixEnvironmentRegex = new Regex(
            @"\\begin\{pmatrix\}(?<body>.*?)\\end\{pmatrix\}",
            RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex BmatrixEnvironmentRegex = new Regex(
            @"\\begin\{bmatrix\}(?<body>.*?)\\end\{bmatrix\}",
            RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex VmatrixEnvironmentRegex = new Regex(
            @"\\begin\{vmatrix\}(?<body>.*?)\\end\{vmatrix\}",
            RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex MatrixEnvironmentRegex = new Regex(
            @"\\begin\{matrix\}(?<body>.*?)\\end\{matrix\}",
            RegexOptions.Singleline | RegexOptions.Compiled);

        // align* (chaînes d'équivalences/égalités, brief 30-04 multiline-systems).
        // Word UnicodeMath : `█(...)` (U+2588 BLACK SQUARE) est le shorthand
        // matrix Word qui marche en BuildUp. `\eqarray(...)` testé le 02-05
        // → rendu littéralement comme texte (non reconnu par BuildUp dans
        // certaines versions Word). On reste donc sur `█(...)`.
        // `&` = column separator, `@` = row separator.
        private static readonly Regex AlignStarEnvironmentRegex = new Regex(
            @"\\begin\{align\*\}(?<body>.*?)\\end\{align\*\}",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static string NormalizeMatrixBody(string body)
        {
            body = body.Trim();
            body = Regex.Replace(body, @"\\\\", "@");
            body = Regex.Replace(body, @"\s*@\s*", "@");
            body = Regex.Replace(body, @"\s*&\s*", "&");
            return body;
        }

        private static string ConvertEnvironments(string src)
        {
            src = CasesEnvironmentRegex.Replace(src, m =>
            {
                // Espace après `{` pour séparation visuelle entre l'accolade
                // et les lignes (cf. user feedback 05-05 « plus un petit espace
                // entre l'accolade et les lignes »).
                // NormalizeMatrixBody collapse les espaces autour de `&` et `@`
                // pour le rendu Word OMath, supportant ainsi l'alignement
                // intra-cases via `&` (Phase 2 ADR 05-05 cases-multiline).
                return "{ █(" + NormalizeMatrixBody(m.Groups["body"].Value) + ")┤";
            });
            src = PmatrixEnvironmentRegex.Replace(src, m =>
                "(■(" + NormalizeMatrixBody(m.Groups["body"].Value) + "))");
            src = BmatrixEnvironmentRegex.Replace(src, m =>
                "[■(" + NormalizeMatrixBody(m.Groups["body"].Value) + ")]");
            src = VmatrixEnvironmentRegex.Replace(src, m =>
                "|■(" + NormalizeMatrixBody(m.Groups["body"].Value) + ")|");
            src = MatrixEnvironmentRegex.Replace(src, m =>
                "■(" + NormalizeMatrixBody(m.Groups["body"].Value) + ")");
            src = AlignStarEnvironmentRegex.Replace(src, m =>
                "█(" + NormalizeMatrixBody(m.Groups["body"].Value) + ")");
            return src;
        }

        // ============================================================
        // Étape 1 : scan structurel + littéraux
        // ============================================================

        /// <summary>
        /// Convertit un fragment complet (sequence de tokens et structures).
        /// Applique les remplacements littéraux APRÈS le scan structurel sur
        /// la chaîne reconstruite. Récursif via les arguments <c>{...}</c>
        /// des commandes (<c>ConvertArg</c> applique aussi les littéraux).
        /// </summary>
        private static string ConvertSequence(string src)
        {
            var s = ScanStructural(src);
            return ApplyLiteralReplacements(s);
        }

        /// <summary>
        /// Convertit un argument <c>{...}</c> (contenu déjà extrait).
        /// Applique structurel + littéraux pour que le résultat soit dans
        /// son état final — utile pour <see cref="EmitArg"/> qui décide
        /// single-char vs multi-char d'après la longueur post-conversion.
        /// </summary>
        private static string ConvertArg(string arg) => ConvertSequence(arg);

        /// <summary>
        /// Scan principal : reconnaît les commandes structurelles
        /// (<c>\frac</c>, <c>\sqrt</c>, <c>\binom</c>, <c>\mathbb</c>,
        /// accents) et les exposant/indice avec accolades. Le reste est
        /// recopié tel quel (les littéraux seront résolus en post-pass).
        ///
        /// Stratégie d'émission :
        /// - <c>\frac{a}{b}</c> : émet <c>EmitArg(a)/EmitArg(b)</c> avec
        ///   single-char shortcut. Si un token tight suit dans la source
        ///   (lettre/chiffre/paren ouvrante), insère un espace
        ///   d'anti-absorption pour empêcher Word d'absorber au dénom.
        /// - <c>x^{...}</c> / <c>x_{...}</c> : émet <c>^EmitArg(...)</c>
        ///   ou <c>_EmitArg(...)</c>. Single-char shortcut s'applique.
        /// - <c>\sqrt{x}</c> : toujours <c>√(x)</c>, Word ne reconnaît pas
        ///   <c>√x</c> sans parens comme racine englobante.
        /// </summary>
        private static string ScanStructural(string src)
        {
            var sb = new StringBuilder(src.Length);
            int i = 0;
            while (i < src.Length)
            {
                if (src[i] == '\\')
                {
                    int nameStart = i + 1;
                    int nameEnd = nameStart;
                    while (nameEnd < src.Length && char.IsLetter(src[nameEnd])) nameEnd++;
                    string cmd = src.Substring(nameStart, nameEnd - nameStart);
                    int after = nameEnd;

                    if (cmd == "frac" || cmd == "dfrac" || cmd == "tfrac")
                    {
                        if (TryReadBracedArg(src, after, out string a, out int afterA)
                            && TryReadBracedArg(src, afterA, out string b, out int afterB))
                        {
                            string ca = ConvertArg(a);
                            string cb = ConvertArg(b);
                            sb.Append(EmitArg(ca)).Append('/').Append(EmitArg(cb));
                            // Anti-absorption : si le caractère suivant est un
                            // token tight (lettre/chiffre/paren ouvrante), Word
                            // l'avale au dénom. On insère un espace pour borner
                            // visuellement la fraction. Cf. ADR 30-04 + bug image.
                            if (afterB < src.Length && IsTightSuccessor(src[afterB]))
                                sb.Append(' ');
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
                            string carg = ConvertArg(arg);
                            // sqrt garde TOUJOURS les parens : Word UnicodeMath
                            // n'englobe que ce qui est explicitement délimité
                            // sous le radical. `√x` rendrait juste √ + x à côté.
                            if (nArg != null)
                                sb.Append("√(").Append(ConvertArg(nArg)).Append("&").Append(carg).Append(")");
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
                              .Append(ConvertArg(a))
                              .Append("¦")
                              .Append(ConvertArg(b))
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
                            if (cmd == "mathbb" && arg.Length == 1 && SetLetterMap.TryGetValue(arg, out var setChar))
                            {
                                sb.Append(setChar);
                            }
                            else
                            {
                                sb.Append(ConvertArg(arg));
                            }
                            i = afterArg;
                            continue;
                        }
                    }
                    else if (CombiningAccents.TryGetValue(cmd, out var combiningChar))
                    {
                        if (TryReadBracedArg(src, after, out string arg, out int afterArg))
                        {
                            // ACCENTS : caractère Unicode combinant que Word
                            // reconnaît dans BuildUp (menu accent du ribbon).
                            //   \vec{AB}  → (AB)⃗
                            //   \hat{x}   → x̂
                            string inner = ConvertArg(arg);
                            if (inner.Length == 1)
                                sb.Append(inner).Append(combiningChar);
                            else
                                sb.Append("(").Append(inner).Append(")").Append(combiningChar);
                            i = afterArg;
                            continue;
                        }
                    }
                    // Commande non dispatchée : on recopie telle quelle, les
                    // remplacements littéraux feront le travail (lettres
                    // grecques, ensembles, relations, fonctions trig…).
                    sb.Append('\\').Append(cmd);
                    i = after;
                    continue;
                }

                // Exposant / indice avec accolades : x^{abc} → x^(abc) ou x^a
                // (single-char shortcut). Cf. ADR 30-04 : l'argument 1 char
                // ASCII alphanum (ou lettre unicode après convert) est émis
                // nu pour éviter les `(2)` visibles dans Word BuildUp.
                if ((src[i] == '^' || src[i] == '_') && i + 1 < src.Length && src[i + 1] == '{')
                {
                    if (TryReadBracedArg(src, i + 1, out string arg, out int afterArg))
                    {
                        sb.Append(src[i]);
                        string carg = ConvertArg(arg);
                        sb.Append(EmitArg(carg));
                        i = afterArg;
                        continue;
                    }
                }

                sb.Append(src[i]);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Décide si <paramref name="converted"/> peut être émis nu (single-
        /// char shortcut) ou doit être enrobé de parenthèses. Une lettre
        /// alphanum ou greek (post-conversion) seule est nue ; tout le reste
        /// est entre parens.
        ///
        /// Cas frontière : un signe seul (`+`, `-`) en exposant (limite
        /// `0^+`) doit aussi passer en single-char (Word reconnaît `^+`).
        /// </summary>
        private static string EmitArg(string converted)
        {
            if (IsSingleAtomicChar(converted)) return converted;
            return "(" + converted + ")";
        }

        private static bool IsSingleAtomicChar(string s)
        {
            if (s.Length != 1) return false;
            char c = s[0];
            // Letter or digit (ASCII + greek/maths après LiteralReplacements)
            // OU signe arithmétique simple (cas limite 0^+ / 0^-).
            return char.IsLetterOrDigit(c) || c == '+' || c == '-';
        }

        /// <summary>
        /// True si <paramref name="c"/> est un caractère qui, accolé à une
        /// fraction émise <c>(num)/(denom)</c> ou <c>num/denom</c>, serait
        /// absorbé par Word BuildUp dans le dénominateur. On insère alors
        /// un espace de séparation. Cas typiques : lettre, chiffre, paren
        /// ouvrante. Pas d'absorption pour les opérateurs arithmétiques
        /// (Word coupe la fraction sur `+`, `-`, `*`, `=`, etc.) ni pour
        /// l'espace.
        /// </summary>
        private static bool IsTightSuccessor(char c)
        {
            return char.IsLetterOrDigit(c) || c == '(' || c == '[';
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

        // ============================================================
        // Étape 2 : remplacements littéraux (commande → caractère unicode)
        // ============================================================

        private static string ApplyLiteralReplacements(string s)
        {
            foreach (var kv in LiteralReplacements)
                s = s.Replace(kv.Key, kv.Value);
            return s;
        }

        // Lettres grecques, relations, ensembles. Tout passe par Replace — donc
        // l'ordre compte : versions longues d'abord pour éviter que "\in" matche
        // "\int" etc.
        private static readonly List<KeyValuePair<string, string>> LiteralReplacements =
            new List<KeyValuePair<string, string>>
            {
                // Relations multi-char (prioritaires)
                new KeyValuePair<string, string>("\\leqslant", "≤"),
                new KeyValuePair<string, string>("\\geqslant", "≥"),
                // Délimiteurs auto-tailles \left \right (le renderer lattice
                // les émet autour des Group). Word OMath n'a pas besoin de la
                // commande LaTeX pour auto-redimensionner, donc on déballe.
                // Ordre : versions à 2 chars d'abord pour matcher avant \left.
                new KeyValuePair<string, string>("\\left\\{", "{"),
                new KeyValuePair<string, string>("\\right\\}", "}"),
                new KeyValuePair<string, string>("\\left(", "("),
                new KeyValuePair<string, string>("\\right)", ")"),
                new KeyValuePair<string, string>("\\left[", "["),
                new KeyValuePair<string, string>("\\right]", "]"),
                new KeyValuePair<string, string>("\\left|", "|"),
                new KeyValuePair<string, string>("\\right|", "|"),
                new KeyValuePair<string, string>("\\left.", ""),
                new KeyValuePair<string, string>("\\right.", ""),
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
                // Multiplications : on consomme tous les espaces parasites
                // autour (séparateur LaTeX trailing et leading). Sinon
                // `2\cdot 4` → `2⋅ 4` (bug 2026-05-09 trailing) ou
                // `\vec{a} \cdot \vec{b}` → `a⃗ ⋅b⃗` (leading) avec
                // espaces visibles dans Word OMath. Ordre : variantes les
                // plus spécifiques d'abord.
                new KeyValuePair<string, string>(" \\times ", "×"),
                new KeyValuePair<string, string>("\\times ", "×"),
                new KeyValuePair<string, string>(" \\times", "×"),
                new KeyValuePair<string, string>("\\times", "×"),
                new KeyValuePair<string, string>(" \\cdot ", "⋅"),
                new KeyValuePair<string, string>("\\cdot ", "⋅"),
                new KeyValuePair<string, string>(" \\cdot", "⋅"),
                new KeyValuePair<string, string>("\\cdot", "⋅"),
                new KeyValuePair<string, string>("\\circ", "∘"),
                new KeyValuePair<string, string>("\\otimes", "⊗"),
                new KeyValuePair<string, string>("\\oplus", "⊕"),
                new KeyValuePair<string, string>("\\approx", "≈"),
                new KeyValuePair<string, string>("\\propto", "∝"),
                new KeyValuePair<string, string>("\\equiv", "≡"),
                new KeyValuePair<string, string>("\\triangle", "△"),
                // \square : carré vide. Émis par le renderer lattice pour les
                // Holes (slots manquants). Word OMath BuildUp ne reconnaît pas
                // \square en commande, mais affiche □ (U+25A1) en glyphe direct.
                new KeyValuePair<string, string>("\\square", "□"),
                // // (parallèle oblique en notation française) : Word interprète
                // deux slashes successifs comme deux divisions consécutives →
                // fractions imbriquées vides. On bascule sur le caractère
                // Unicode ⫽ (U+2AFD DOUBLE SOLIDUS OPERATOR) qui est un opérateur
                // math reconnu et rendu en barres obliques.
                new KeyValuePair<string, string>("//", "⫽"),
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
                new KeyValuePair<string, string>("\\Upsilon", "Υ"),
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
                new KeyValuePair<string, string>("\\varphi", "ϕ"),
                new KeyValuePair<string, string>("\\chi", "χ"),
                new KeyValuePair<string, string>("\\psi", "ψ"),
                new KeyValuePair<string, string>("\\omega", "ω"),
                // Relations 2-char
                new KeyValuePair<string, string>("\\leq", "≤"),
                new KeyValuePair<string, string>("\\geq", "≥"),
                new KeyValuePair<string, string>("\\neq", "≠"),
                new KeyValuePair<string, string>("\\sim", "∼"),
                new KeyValuePair<string, string>("\\notin", "∉"),
                new KeyValuePair<string, string>("\\cup", "∪"),
                new KeyValuePair<string, string>("\\cap", "∩"),
                new KeyValuePair<string, string>("\\pm", "±"),
                new KeyValuePair<string, string>("\\mp", "∓"),
                new KeyValuePair<string, string>("\\to", "→"),
                new KeyValuePair<string, string>("\\mapsto", "↦"),
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
                // \in EN DERNIER : "\in" est préfixe de "\int", "\infty",
                // "\intersection"... La boucle Replace ci-dessous applique
                // chaque entrée séquentiellement, donc tout préfixe traité
                // avant mange ses extensions ("\int" → "∈t"). Régression
                // critique 29-04. Garder \in en queue pour que tous les
                // \in* aient déjà été remplacés.
                new KeyValuePair<string, string>("\\in", "∈"),
            };

        private static readonly Dictionary<string, string> SetLetterMap =
            new Dictionary<string, string>
            {
                { "R", "ℝ" }, { "N", "ℕ" }, { "Z", "ℤ" }, { "Q", "ℚ" }, { "C", "ℂ" },
                { "K", "𝕂" }, { "P", "ℙ" }, { "F", "𝔽" },
            };

        // Table des accents LaTeX → caractère Unicode combinant que Word
        // interprète en BuildUp comme l'accent correspondant du menu
        // "Accent" (Design tab des équations).
        private static readonly Dictionary<string, string> CombiningAccents =
            new Dictionary<string, string>
            {
                { "vec",            "⃗" }, // ⃗ Combining Right Arrow Above (\vec)
                { "overrightarrow", "⃗" }, // même accent, nom alternatif
                { "hat",            "̂" }, // ̂ Combining Circumflex Accent
                { "widehat",        "̂" },
                { "bar",            "̅" }, // ̅ Combining Overline
                { "overline",       "̅" },
                { "underline",      "̲" }, // ̲ Combining Low Line
                { "tilde",          "̃" }, // ̃ Combining Tilde
                { "widetilde",      "̃" },
                { "dot",            "̇" }, // ̇ Combining Dot Above
                { "ddot",           "̈" }, // ̈ Combining Diaeresis
            };
    }
}
