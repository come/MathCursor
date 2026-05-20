using System.Collections.Generic;
using MathCursor.Core.Lattice;
using MathCursor.Core.Lattice.Ast;

namespace MathCursor.Core
{
    /// <summary>
    /// Façade du moteur lattice : enchaîne Lex → TopK → Parse → Render et
    /// expose une API conforme à <see cref="ILatexEngine"/> pour que l'adapter
    /// VSTO reste inchangé.
    ///
    /// Phase 5a : retourne plusieurs suggestions quand le top-K Dijkstra
    /// produit des chemins de coût comparable (ratio relatif). Le top-1 est
    /// toujours en première position de la liste — l'adapter VSTO peut
    /// l'utiliser comme suggestion par défaut. Les alternatives sémantiques
    /// (vec(AB), racine V(...), …) viendront en phase 5b via un module
    /// AlternativeGenerator distinct, branché sur l'AST top-1.
    /// </summary>
    public sealed class LatticeEngine : ILatexEngine
    {
        // Delta absolu max entre coût top-1 et coût d'une alternative. 5 =
        // "tolère un écart d'un identifiant 1 lettre". Choix delta absolu
        // (et pas ratio) : sur les longues formules, le coût total des deux
        // paths est proche numériquement même si une portion locale diverge
        // énormément (ex: "sum k=1 n+1 cos2x" — top-1 ~17, alt ~30, ratio
        // 1.76 mais l'écart vient EXCLUSIVEMENT de "cos" vs "c·o·s" qui est
        // un faux positif). Le delta absolu reflète mieux la divergence
        // locale du segment ambigu sans avoir à reconstruire les sous-paths.
        private const int AlternativeCostDelta = 5;

        // Top-1 + jusqu'à 3 alternatives = 4 items max dans la popup. Au-delà
        // c'est illisible dans une fenêtre étroite.
        private const int MaxSuggestions = 4;

        // Largeur de fenêtre top-K Dijkstra : on en prend plus que MaxSuggestions
        // pour tolérer la déduplication (deux paths peuvent rendre le même LaTeX).
        private const int TopKWidth = 6;

        public IReadOnlyList<LatexSuggestion> Convert(string rawSpan)
        {
            if (string.IsNullOrWhiteSpace(rawSpan))
                return System.Array.Empty<LatexSuggestion>();

            var trimmed = AutoBalanceDelimiters(NormalizeAngleCaret(NormalizeUnicodeSubSup(rawSpan.Trim())));
            var edges = Lexer.Lex(trimmed);
            var paths = LatticePathFinder.TopK(edges, trimmed.Length, TopKWidth);
            if (paths.Count == 0)
                return System.Array.Empty<LatexSuggestion>();

            var topCost = paths[0].Cost;
            int maxDelta = AlternativeCostDelta;

            var seenLatex = new HashSet<string>();
            var suggestions = new List<LatexSuggestion>();
            AstNode? topAst = null;
            foreach (var p in paths)
            {
                // Le top-1 entre toujours. Les alternatives doivent être à
                // delta ≤ MaxDelta du coût top-1.
                if (suggestions.Count > 0 && (p.Cost - topCost) > maxDelta) break;

                var ast = new Parser(p.Edges).Parse();
                var latex = LatexRenderer.Render(ast);
                if (!seenLatex.Add(latex)) continue; // dédupe sur LaTeX

                if (topAst == null) topAst = ast; // mémorise pour AlternativeGenerator

                double score = System.Math.Max(0, 100 - p.Cost);
                suggestions.Add(new LatexSuggestion(
                    latex: latex,
                    patternId: "lattice",
                    score: score,
                    consumedTokens: trimmed.Length,
                    totalTokens: trimmed.Length,
                    isPartial: false));
                if (suggestions.Count >= MaxSuggestions) break;
            }

            // Alternatives sémantiques (vec, droite, segment, indice…) à partir
            // de l'AST top-1. Ces alternatives ne sont PAS classées par coût
            // Dijkstra (elles n'en ont pas) — score arbitraire bas pour qu'elles
            // figurent en bas de la liste de candidats sous le top-1, mais
            // au-dessus de potentielles alternatives lattice de coût égal.
            if (topAst != null && suggestions.Count < MaxSuggestions)
            {
                var semanticAlts = AlternativeGenerator.Generate(topAst);
                foreach (var altLatex in semanticAlts)
                {
                    if (!seenLatex.Add(altLatex)) continue;
                    suggestions.Add(new LatexSuggestion(
                        latex: altLatex,
                        patternId: "alt-semantic",
                        score: System.Math.Max(0, 100 - topCost - 1),
                        consumedTokens: trimmed.Length,
                        totalTokens: trimmed.Length,
                        isPartial: false));
                    if (suggestions.Count >= MaxSuggestions) break;
                }
            }

            return suggestions;
        }

        /// <summary>
        /// API phase 5b2 : retourne le top-1 LaTeX et l'ambiguïté la plus à
        /// droite (s'il y en a une) avec sa position pour permettre la
        /// recompose dans la popup. À utiliser à la place de <see cref="Convert"/>
        /// quand on veut le modèle « formule finale + ambiguïté courante ».
        /// </summary>
        public AmbiguityResult ConvertWithAmbiguity(string rawSpan)
        {
            if (string.IsNullOrWhiteSpace(rawSpan))
                return new AmbiguityResult(string.Empty, null, null, null);

            var trimmed = AutoBalanceDelimiters(NormalizeAngleCaret(NormalizeUnicodeSubSup(rawSpan.Trim())));
            var edges = Lexer.Lex(trimmed);
            var paths = LatticePathFinder.TopK(edges, trimmed.Length, TopKWidth);
            if (paths.Count == 0)
                return new AmbiguityResult(string.Empty, null, null, null);

            var topAst = new Parser(paths[0].Edges).Parse();
            var topLatex = LatexRenderer.Render(topAst);
            // On passe `trimmed` (la source post-NormalizeUnicode + balance) pour
            // que les règles source-mutation (V→forall, etc.) scannent la chaîne
            // qui correspond à l'AST top-1.
            return AlternativeGenerator.FindRightmost(topAst, topLatex, trimmed);
        }

        /// <summary>
        /// Factory pour rester aligné avec le contrat de
        /// <see cref="NotImplementedEngine.LoadEmbedded"/> (que l'adapter
        /// VSTO appelle au démarrage). Le paramètre langue est ignoré : la
        /// langue est gérée par le vocabulaire interne du lexer.
        /// </summary>
        public static LatticeEngine LoadEmbedded(string language = "fr")
            => new LatticeEngine();

        /// <summary>
        /// Auto-balance les délimiteurs ouverts : si l'utilisateur tape
        /// <c>u (1 3</c> sans fermer la paren, on append le <c>)</c> manquant
        /// pour que le pattern soit reconnu en cours de frappe.
        ///
        /// UX : la conversion live (popup en cours de saisie / démo web) doit
        /// reconnaître l'intention avant que l'utilisateur ait fini de fermer.
        /// On ferme silencieusement les parens / brackets / braces non fermés
        /// dans l'ordre où ils manquent.
        ///
        /// Comportement :
        /// <list type="bullet">
        /// <item>Compteurs séparés pour <c>()</c>, <c>[]</c>, <c>{}</c>.</item>
        /// <item>Une fermeture en trop (closer sans opener) ne crée pas un
        /// compteur négatif — l'utilisateur peut fermer une paren qu'il a
        /// pas ouverte (collage) sans qu'on injecte de chars devant.</item>
        /// <item>Append seulement à la FIN. On ne réordonne pas, on ne ferme
        /// pas dans l'ordre LIFO strict — c'est une approximation qui suffit
        /// pour les cas typiques (un seul niveau d'imbrication ouvert).</item>
        /// </list>
        /// </summary>
        private static string AutoBalanceDelimiters(string input)
        {
            if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
            // On ne balance QUE les parens `()`. Les `[]` ne sont PAS balançés
            // parce que la convention française des intervalles utilise `[`
            // et `]` dans les deux sens (`[0,1]` fermé, `[0,1[` semi-ouvert,
            // `]0,1]` semi-ouvert inverse). Compter `[` comme open pure
            // ferait casser `[0,+inf[`. Idem `{}` non balancés (rare en
            // saisie partielle, et risque sur les commandes LaTeX `\frac{`).
            int parens = 0;
            foreach (var c in input)
            {
                if (c == '(') parens++;
                else if (c == ')' && parens > 0) parens--;
            }
            if (parens == 0) return input;
            var sb = new System.Text.StringBuilder(input);
            while (parens-- > 0) sb.Append(')');
            return sb.ToString();
        }

        /// <summary>
        /// Normalise les caractères Unicode de superscript/subscript en notation
        /// caret/underscore avant l'analyse : `x²` → `x^2`, `n₃` → `n_3`. Permet
        /// au lexer (qui ne connaît que ^ et _) de reconnaître les conventions
        /// typographiques tapées via le clavier français étendu.
        /// </summary>
        /// <summary>
        /// Substitue <c>^[a-zA-Z]+</c> en position "fresh" (début de zone
        /// OU précédé d'espace OU précédé d'un opérateur math) par
        /// <c>angle X</c> — le mot-clé que le parser reconnaît comme un
        /// nœud <see cref="MathCursor.Core.Lattice.Ast.Angle"/>.
        ///
        /// <para>Position "fresh" : on considère que <c>^</c> est un
        /// marqueur d'angle si rien à gauche (sauf espace/op math) ne
        /// peut servir de base à un exposant. Si à gauche on a une lettre/
        /// chiffre/fermant, <c>^</c> reste l'opérateur exposant historique
        /// (<c>x^2</c> = puissance, inchangé).</para>
        ///
        /// <para>Cf. ADR <c>2026-05-11-Feat-angle-notation-caret-and-keyword</c>.</para>
        /// </summary>
        internal static string NormalizeAngleCaret(string input)
        {
            if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
            // Test rapide : pas de `^` → return tel quel.
            if (input.IndexOf('^') < 0) return input;

            var sb = new System.Text.StringBuilder(input.Length + 8);
            int i = 0;
            while (i < input.Length)
            {
                if (input[i] != '^') { sb.Append(input[i]); i++; continue; }
                // Test position fresh : on regarde le char à gauche (ou
                // début de chaîne). Fresh = whitespace, opérateur math, ou
                // début. Tout autre (lettre, chiffre, `}`, `)`) = exposant
                // classique → on laisse le `^` tel quel.
                bool fresh;
                if (i == 0) { fresh = true; }
                else
                {
                    char prev = input[i - 1];
                    fresh = char.IsWhiteSpace(prev)
                        || prev == '+' || prev == '-' || prev == '*' || prev == '/'
                        || prev == '=' || prev == '<' || prev == '>'
                        || prev == ',' || prev == ';' || prev == '(' || prev == '[';
                }
                if (!fresh) { sb.Append('^'); i++; continue; }
                // Lookhead : doit être suivi d'au moins une lettre.
                int j = i + 1;
                while (j < input.Length && IsAsciiLetter(input[j])) j++;
                int letterCount = j - (i + 1);
                if (letterCount == 0) { sb.Append('^'); i++; continue; }
                // Substitue `^XYZ` → `angle XYZ` (espace pour que le lexer
                // sépare le keyword du nom).
                sb.Append("angle ");
                sb.Append(input, i + 1, letterCount);
                i = j;
            }
            return sb.ToString();
        }

        private static bool IsAsciiLetter(char c)
            => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static string NormalizeUnicodeSubSup(string input)
        {
            if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
            // Test rapide avant allocation : pas de char concerné → return tel quel
            bool needs = false;
            foreach (var c in input)
            {
                if ((c >= '⁰' && c <= '⁹') || c == '¹' || c == '²' || c == '³'
                    || (c >= '₀' && c <= '₉'))
                { needs = true; break; }
            }
            if (!needs) return input;

            var sb = new System.Text.StringBuilder(input.Length + 4);
            foreach (var c in input)
            {
                switch (c)
                {
                    // Superscripts Unicode → ^N (EXPLICITE). L'utilisateur a
                    // tapé un caractère superscript dédié, c'est un choix
                    // typographique fort : pas d'ambiguïté avec un subscript.
                    // Le Sup créé par le parser sera IsImplicit=false (^ explicit),
                    // donc AlternativeGenerator ne proposera pas x_N comme alt.
                    case '⁰': sb.Append("^0"); break;
                    case '¹': sb.Append("^1"); break;
                    case '²': sb.Append("^2"); break;
                    case '³': sb.Append("^3"); break;
                    case '⁴': sb.Append("^4"); break;
                    case '⁵': sb.Append("^5"); break;
                    case '⁶': sb.Append("^6"); break;
                    case '⁷': sb.Append("^7"); break;
                    case '⁸': sb.Append("^8"); break;
                    case '⁹': sb.Append("^9"); break;
                    case '₀': sb.Append("_0"); break;
                    case '₁': sb.Append("_1"); break;
                    case '₂': sb.Append("_2"); break;
                    case '₃': sb.Append("_3"); break;
                    case '₄': sb.Append("_4"); break;
                    case '₅': sb.Append("_5"); break;
                    case '₆': sb.Append("_6"); break;
                    case '₇': sb.Append("_7"); break;
                    case '₈': sb.Append("_8"); break;
                    case '₉': sb.Append("_9"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
