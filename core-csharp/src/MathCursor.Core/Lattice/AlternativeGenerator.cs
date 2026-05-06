using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.Lattice.Ast;

namespace MathCursor.Core.Lattice
{
    /// <summary>
    /// Mutation à appliquer sur la SOURCE d'entrée (la chaîne tapée par
    /// l'utilisateur, pas le LaTeX rendu) pour résoudre une ambiguïté. Quand
    /// <see cref="AmbiguitySpot.Mutation"/> est non null, l'adapter doit
    /// remplacer source[Offset..Offset+Length] par <see cref="Replacement"/>
    /// puis re-déclencher le pipeline complet (Lex → TopK → Parse → Render)
    /// au lieu de faire une sub-string sur le LaTeX rendu.
    ///
    /// Modèle plus robuste que la sub LaTeX : la source reste la vérité, le
    /// rendu est une projection. Permet aux règles comme V→forall de
    /// déclencher un re-parsing en mode scope où les espaces deviennent
    /// séparateurs d'arguments.
    /// </summary>
    public sealed class SourceMutation
    {
        public int Offset { get; }
        public int Length { get; }
        public string Replacement { get; }
        public SourceMutation(int offset, int length, string replacement)
        {
            Offset = offset;
            Length = length;
            Replacement = replacement;
        }
    }

    /// <summary>
    /// Une alternative dans une ambiguïté : son aperçu LaTeX et sa mutation
    /// source optionnelle. Si <see cref="Mutation"/> est null, l'alternative
    /// est appliquée par sub LaTeX (mode legacy AB/ABC/x2). Si non null,
    /// l'adapter doit muter la source brute et relancer le pipeline.
    /// Permet d'avoir des alts hétérogènes dans le même Spot (ex: V → [V
    /// identity sans mutation, ∀ avec mutation forall, √ avec mutation racine]).
    /// </summary>
    public sealed class AmbiguityAlternative
    {
        public string Latex { get; }
        public SourceMutation? Mutation { get; }
        public AmbiguityAlternative(string latex, SourceMutation? mutation = null)
        {
            Latex = latex;
            Mutation = mutation;
        }
    }

    /// <summary>
    /// Une ambiguïté détectée localement dans l'AST top-1 : un sous-AST qui a
    /// une lecture par défaut (le LaTeX intégré dans top-1) et N alternatives.
    /// <see cref="RuleId"/> identifie la RÈGLE qui a généré cette ambiguïté
    /// (ex: "two-uppercase") — utilisé par la popup pour appliquer une
    /// préférence apprise (l'utilisateur a déjà choisi vec pour AB → on
    /// applique vec automatiquement aux CD, EF, … de la session).
    ///
    /// Chaque alternative porte sa propre mutation source via
    /// <see cref="AmbiguityAlternative.Mutation"/>. Permet à un même Spot de
    /// proposer des choix hétérogènes (V → V identity / ∀ scope / √ scope).
    /// </summary>
    public sealed class AmbiguitySpot
    {
        public string RuleId { get; }
        public string DefaultLatex { get; }
        public IReadOnlyList<AmbiguityAlternative> Alternatives { get; }
        public AmbiguitySpot(string ruleId, string defaultLatex,
            IReadOnlyList<AmbiguityAlternative> alternatives)
        {
            RuleId = ruleId;
            DefaultLatex = defaultLatex;
            Alternatives = alternatives;
        }
    }

    /// <summary>
    /// Une occurrence d'ambiguïté dans l'AST top-1 avec sa position dans le
    /// LaTeX rendu. Permet à la popup d'appliquer en cascade une préférence
    /// validée : « si l'utilisateur a choisi vec pour BC, on applique vec à
    /// tous les autres matches two-uppercase de la formule (AB, AC…) ».
    /// </summary>
    public sealed class AmbiguityMatch
    {
        public AmbiguitySpot Spot { get; }
        public int Start { get; }
        public int End { get; }
        public AmbiguityMatch(AmbiguitySpot spot, int start, int end)
        {
            Spot = spot;
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// Résultat de <see cref="AlternativeGenerator.FindRightmost"/> : le top-1
    /// LaTeX complet, le segment ambigu le plus à droite (pour la popup), et
    /// la liste de TOUS les matches dans la formule (pour la résolution en
    /// cascade des autres patterns du même RuleId).
    /// </summary>
    public sealed class AmbiguityResult
    {
        public string TopLatex { get; }
        public AmbiguitySpot? Spot { get; }
        public int? SpotStart { get; }
        public int? SpotEnd { get; }
        public IReadOnlyList<AmbiguityMatch> AllMatches { get; }

        public AmbiguityResult(string topLatex, AmbiguitySpot? spot, int? spotStart, int? spotEnd,
            IReadOnlyList<AmbiguityMatch>? allMatches = null)
        {
            TopLatex = topLatex;
            Spot = spot;
            SpotStart = spotStart;
            SpotEnd = spotEnd;
            AllMatches = allMatches ?? System.Array.Empty<AmbiguityMatch>();
        }
    }

    /// <summary>
    /// Génère des alternatives sémantiques sur l'AST top-1 (LaTeX top-1 du
    /// pipeline lattice). Ces alternatives ne viennent PAS du lattice naturel
    /// (qui ne génère qu'une lecture par chaîne d'opérateurs) — elles encodent
    /// des conventions typographiques où une même saisie peut vouloir dire
    /// plusieurs choses dans les maths du lycée français.
    ///
    /// Exemples :
    /// <list type="bullet">
    /// <item><c>AB</c> (deux majuscules adjacentes) → vecteur, droite, segment</item>
    /// <item><c>x2</c> (lettre + chiffre) → exposant (top-1) ou indice (alt)</item>
    /// </list>
    ///
    /// Cf. <see cref="FindRightmost"/> pour la stratégie « ambiguïté la plus à
    /// droite = la plus proche du caret en cours de saisie » : tout ce qui est
    /// avant a été validé mou par le fait que l'élève continue à taper.
    /// </summary>
    public static class AlternativeGenerator
    {
        /// <summary>
        /// Liste plate des alternatives à TOUS les niveaux (legacy, utilisé par
        /// <see cref="MathCursor.Core.LatticeEngine.Convert"/> pour exposer
        /// chaque variante comme une suggestion complète). Usage UI 5b2 :
        /// préférer <see cref="FindRightmost"/> qui rend une seule ambiguïté.
        /// </summary>
        public static IReadOnlyList<string> Generate(AstNode? topAst)
        {
            if (topAst == null) return System.Array.Empty<string>();
            var spot = MatchAmbiguity(topAst);
            if (spot == null) return System.Array.Empty<string>();
            var list = new string[spot.Alternatives.Count];
            for (int i = 0; i < list.Length; i++) list[i] = spot.Alternatives[i].Latex;
            return list;
        }

        /// <summary>
        /// Trouve l'ambiguïté la plus à droite dans l'AST (post-ordre right-first).
        /// Renvoie le LaTeX du segment ambigu (default + alternatives) et sa
        /// position dans <paramref name="topLatex"/> via LastIndexOf.
        ///
        /// <paramref name="source"/> = chaîne d'entrée brute (avant rendu LaTeX).
        /// Sert aux règles « source-mutation » (V→forall, futurs intervalles)
        /// qui scannent le source plutôt que le LaTeX rendu et émettent une
        /// <see cref="SourceMutation"/> au lieu d'une sub LaTeX.
        ///
        /// Si plusieurs ambiguïtés existent dans la formule, seule la plus à
        /// droite est exposée — convention « validation molle » : l'élève a
        /// implicitement validé les ambiguïtés gauches en tapant le reste.
        /// </summary>
        public static AmbiguityResult FindRightmost(AstNode? topAst, string topLatex, string source = "")
        {
            if (topAst == null) return new AmbiguityResult(topLatex, null, null, null);

            // Fallback historique : pour le code legacy qui n'a pas de source à
            // passer (tests, anciens tests d'engine), on prend topLatex comme
            // source. Approximation acceptable pour les règles AB/ABC/x2 dont
            // le rendu LaTeX coïncide avec la source.
            if (source.Length == 0) source = topLatex;

            // Walk pré-ordre right-first qui collecte TOUS les matches valides
            // (= entourés de word boundaries dans topLatex). Le rightmost est
            // le PREMIER ajouté (parcours right-first), retourné comme Spot
            // principal. Les autres servent à la cascade côté popup.
            var allMatches = CollectAllMatches(topAst, topLatex, source);
            if (allMatches.Count == 0)
                return new AmbiguityResult(topLatex, null, null, null, allMatches);
            var rightmost = allMatches[0];
            return new AmbiguityResult(topLatex, rightmost.Spot, rightmost.Start, rightmost.End, allMatches);
        }

        /// <summary>
        /// Parcourt l'AST top-1 et retourne TOUS les sous-AST qui matchent une
        /// règle d'ambiguïté, avec leur position dans <paramref name="topLatex"/>.
        /// Stoppe la descente dès qu'un node match (= cohérent avec
        /// TraverseRightmost qui favorise le pattern le plus large).
        /// </summary>
        private static IReadOnlyList<AmbiguityMatch> CollectAllMatches(AstNode topAst, string topLatex, string source)
        {
            var matches = new List<AmbiguityMatch>();
            var consumed = new bool[topLatex.Length];
            // 1) Patterns AST-based (Sup d'une lettre par un nombre, etc.)
            CollectAllMatchesRec(topAst, topLatex, matches, consumed);
            // 2) Patterns STRING-based sur le LaTeX rendu : séquences de
            //    majuscules adjacentes. On scanne en passant par-dessus l'AST
            //    parce que l'arbre gauche-associatif ne regroupe pas toujours
            //    C et D ensemble (ex: AB*CD donne ((A*B)*C)*D, donc CD n'est
            //    jamais un sous-Bin direct). Le scan string capture toutes
            //    les séquences majuscules quelle que soit leur structure AST.
            ScanUppercaseSequences(topLatex, matches, consumed);
            // 3) Patterns SOURCE-mutation : règles qui ré-écrivent la source et
            //    relancent le pipeline (V→forall, à venir : intervalles, U, etc.).
            //    Scannent la source brute (pas le LaTeX rendu) pour avoir les
            //    positions exactes à muter.
            ScanVAsForallEAsExists(source, topLatex, matches, consumed);
            // 4) Lettres canoniques R/N/Z/Q/C isolées : popup ensemble vs lettre.
            ScanCanonicalSetLetters(source, topLatex, matches, consumed);
            // 5) Function-typique + parens avec 2/3 args virgule : `f(1, 2)`.
            //    Default = function call (top-1), alt = vec coords ligne via
            //    mutation source `f` → `u` (un ident vec-typique) qui force
            //    le parser à reconnaître le pattern coords.
            //    Cf. brief 2026-04-29-vector-coordinates-shorthand §3.1.
            ScanFunctionTypicalWithCommaCoords(source, topLatex, matches, consumed);
            // 6) Col↔ligne pour VectorCoordinates top-level : si l'AST entier
            //    est une seule VectorCoordinates (cas `u (1 2)`, `u(1, 2)`,
            //    `OM (x y z)`, etc.), proposer le layout opposé en alt. On
            //    limite au top-level pour éviter les conflits avec d'autres
            //    ambig nested (V/E, AB, etc.). V2 étendra aux nested.
            ScanVectorLayoutFlipTopLevel(topAst, topLatex, matches, consumed);
            // 7) Élargissement tight de la chaîne (cf. ADR 30-04
            //    Feat-tight-implicit-mult-grouping). Default : `1/x+1` →
            //    \frac{1}{x}+1 (PEMDAS, chaîne implicite uniquement). Alt :
            //    \frac{1}{x+1} (chaîne tight élargie aux ops). Re-parse avec
            //    TightExtendsToOps=true et compare.
            ScanTightChainExtension(source, topLatex, matches, consumed);
            // 8) Décimal vs multiplication pour `\d+\.\d+` (ADR 30-04
            //    Feat-dot-as-multiplier). Default = mult (`3 \cdot 4`),
            //    alt = décimal (`3{,}4`). Permet aux utilisateurs anglo
            //    qui tapent `3.4` pour "trois virgule quatre" de switcher
            //    rapidement.
            ScanDecimalVsMultiplication(source, topLatex, matches, consumed);
            // Tri : priorité aux règles structurantes (V→∀, E→∃ qui changent
            // la sémantique globale) puis aux règles locales (canonical-set,
            // AB→vec, x²→x_2). En cas d'égalité de priorité, rightmost first
            // (= la plus proche du caret).
            matches.Sort((a, b) =>
            {
                int prioA = GetRulePriority(a.Spot.RuleId);
                int prioB = GetRulePriority(b.Spot.RuleId);
                if (prioA != prioB) return prioA.CompareTo(prioB);
                return b.Start.CompareTo(a.Start);
            });
            return matches;
        }

        // Priorité des règles d'ambig (1 = haute, traité en premier).
        // Rules structurantes (changent la sémantique globale, ex V→∀ vs V
        // variable) sortent avant les locales (modifient juste un atome).
        private static int GetRulePriority(string ruleId) => ruleId switch
        {
            RuleVAsForall => 1,
            RuleEAsExists => 1,
            RuleCanonicalSet => 2,
            RuleTwoUppercase => 3,
            RuleThreeUppercase => 3,
            RuleLetterSupNumber => 3,
            // u*v ambig : priorité 3 comme les autres règles d'ambig de
            // multiplication d'idents — pas plus structurant qu'AB→vec.
            RuleVecDotProduct => 3,
            // f(1, 2) ambig : priorité haute parce que sémantiquement
            // structurante (function call vs vecteur de coords) — l'élève
            // doit trancher avant qu'on touche aux locales.
            RuleVectorCoordsVsCall => 2,
            // Col↔ligne : priorité basse (juste un layout, pas un changement
            // sémantique). Les autres ambig l'emportent en cas de coexistence
            // (= rightmost wins entre ambig de priorité égale ; ici on est en
            // dernier).
            RuleVectorLayoutFlip => 4,
            // Tight chain extension : priorité basse (juste un regroupement
            // alternatif, pas un changement sémantique). Les ambig structurelles
            // (AB, V/E) gardent la main.
            RuleTightChainExtension => 4,
            // Décimal vs mult : priorité 3 (sémantique change, pas un layout).
            // Plus haut que tight-chain-extension (4) parce que la confusion
            // décimal/mult a un impact plus fort sur le rendu visuel.
            RuleDecimalVsMultiplication => 3,
            _ => 99,
        };

        /// <summary>
        /// Scan SOURCE : R/N/Z/Q/C isolées (précédées de non-lettre, suivies
        /// d'espace/EOF/ponctuation/fermeture) → popup ensemble vs lettre.
        /// Critère « isolé » strict pour préserver les formules de géométrie
        /// (`pi*R^2`, `2N+1`, etc.) où R est une variable.
        ///
        /// Émet 2 alternatives :
        /// <list type="number">
        /// <item>Alt 0 (focus défaut) : ensemble `\mathbb{R}` via mutation
        ///   source `R` → `bbR` (le keyword bbR est consommé par ParseScope
        ///   et rend `\mathbb{R}`).</item>
        /// <item>Alt 1 : lettre identity (R reste atom, variable).</item>
        /// </list>
        ///
        /// Délimiteurs droits qui valident l'isolation : espace, EOF,
        /// ponctuation `, ; .`, fermeture `]`/`)`/`}`. Les opérateurs math
        /// (`+`, `-`, `*`, `/`, `^`, `_`) NE sont pas considérés isolants
        /// car ils suggèrent un contexte arithmétique (variable).
        /// </summary>
        private static void ScanCanonicalSetLetters(string source, string topLatex,
            List<AmbiguityMatch> output, bool[] consumed)
        {
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (c != 'R' && c != 'N' && c != 'Z' && c != 'Q' && c != 'C') continue;
                // Word boundary à gauche
                if (i > 0 && char.IsLetter(source[i - 1])) continue;
                // À droite : EOF, espace, ou délimiteur de contexte ensemble
                bool followedByDelim = i + 1 == source.Length
                    || IsCanonicalSetDelimiter(source[i + 1]);
                if (!followedByDelim) continue;

                // Position dans topLatex : pour une lettre seule rendue telle
                // quelle, position identique. Sinon on saute (rare).
                int topPos = i < topLatex.Length && topLatex[i] == c ? i : topLatex.IndexOf(c, 0);
                if (topPos < 0 || topPos >= topLatex.Length || consumed[topPos]) continue;

                var canonical = "bb" + c;
                var ensemblePreview = RenderAfterMutation(source, i, 1, canonical);
                var identityPreview = RenderAfterMutation(source, i, 1, c.ToString());

                var alts = new List<AmbiguityAlternative>
                {
                    // Alt 0 (focus par défaut) : ensemble \mathbb{X} via mutation
                    new AmbiguityAlternative(
                        ensemblePreview,
                        new SourceMutation(i, 1, canonical)),
                    // Alt 1 : lettre identity (no mutation, R reste variable)
                    new AmbiguityAlternative(identityPreview, mutation: null),
                };

                var spot = new AmbiguitySpot(RuleCanonicalSet, c.ToString(), alts);
                consumed[topPos] = true;
                output.Add(new AmbiguityMatch(spot, topPos, topPos + 1));
            }
        }

        // Caractères qui suivent une lettre canonique en contexte « ensemble ».
        // Whitespace OU ponctuation OU fermeture de groupement. Les opérateurs
        // math (+ - * / ^ _) sont volontairement exclus pour préserver les
        // formules variables (pi*R², 2N+1, R^2, etc.).
        private static bool IsCanonicalSetDelimiter(char c)
            => char.IsWhiteSpace(c) || c == ',' || c == ';' || c == '.'
               || c == ')' || c == ']' || c == '}';

        /// <summary>
        /// Scan SOURCE : `f(<arg>, <arg>[, <arg>])` où <c>f</c> est un ident
        /// 1 lettre typique fonction (f, g, h, F, G, H), suivi d'une paren
        /// avec 2 ou 3 arguments séparés par virgule. Default = function call
        /// (top-1, déjà rendu), alt = vec coords ligne `\vec{f}(...)`.
        /// Cf. brief 2026-04-29-vector-coordinates-shorthand §3.1.
        ///
        /// Détection légère sur source brute (heuristique) :
        /// 1) Lettre f/g/h/F/G/H isolée (word boundary à gauche)
        /// 2) Suivie d'une `(` (avec ou sans espace optionnel)
        /// 3) Contenu jusqu'à `)` matchant : 2 ou 3 segments virgule top-level
        /// 4) AUCUN espace top-level entre les segments (sinon c'est déjà
        ///    layout colonne → parser a tranché coords sans ambig)
        ///
        /// Mutation source : `f` → identifiant vec-typique (`u`) à la position
        /// de la lettre. Force le parser à reconnaître le pattern coords sur
        /// le re-parse, et on substitue ensuite le nom vec rendu (`\vec{u}`)
        /// par `\vec{f}` pour l'aperçu fidèle.
        /// </summary>
        /// <summary>
        /// Si l'AST top est une <see cref="VectorCoordinates"/> seule (toute
        /// la formule = un vecteur+coords, sans autre opération autour), propose
        /// le layout opposé en alternative :
        /// <list type="bullet">
        /// <item>Saisie <c>u (1 2)</c> → default colonne, alt ligne `\vec{u}(1, 2)`</item>
        /// <item>Saisie <c>u(1, 2)</c> → default ligne, alt colonne `\vec{u}\begin{pmatrix}1 \\ 2\end{pmatrix}`</item>
        /// </list>
        /// V1 limité au top-level pour éviter les conflits de positions avec
        /// les autres ambig (AB, V/E…). Si l'utilisateur tape `vec u + u (1 2)`,
        /// l'AST top n'est PAS une VectorCoordinates (c'est un Bin) et on ne
        /// propose pas le flip — V2 si besoin.
        /// </summary>
        private static void ScanVectorLayoutFlipTopLevel(AstNode topAst, string topLatex,
            List<AmbiguityMatch> output, bool[] consumed)
        {
            if (!(topAst is VectorCoordinates vc)) return;
            // Note (bug Etienne 30-04) : on NE check PLUS `consumed[i]` ici.
            // Le flip layout est une désambig orthogonale aux désambigs
            // lexicales (AB→vec/paren via RuleTwoUppercase consume `AB` du
            // top). Les 2 alts coexistent légitimement dans la popup —
            // l'une décide le layout column/row, l'autre décide vec/paren.
            // Avant ce fix, RuleTwoUppercase bloquait le flip pour AB(1,2).

            string flippedLayout = vc.Layout == "column" ? "row" : "column";
            var altVc = new VectorCoordinates(vc.Name, vc.Values, flippedLayout, vc.IsPoint);
            string altLatex = LatexRenderer.Render(altVc);
            if (altLatex == topLatex) return; // identique → pas d'ambig

            // alt[0] = current (default = ce qu'on voit déjà dans top), alt[1]
            // = layout flipped. Le bridge filtre alt[0] (== top) et n'expose
            // que alt[1] dans la popup.
            var alts = new List<AmbiguityAlternative>
            {
                new AmbiguityAlternative(topLatex, mutation: null),
                new AmbiguityAlternative(altLatex, mutation: null),
            };
            var spot = new AmbiguitySpot(RuleVectorLayoutFlip, topLatex, alts);
            for (int i = 0; i < topLatex.Length; i++) consumed[i] = true;
            output.Add(new AmbiguityMatch(spot, 0, topLatex.Length));
        }

        /// <summary>
        /// Scan SOURCE re-parsé avec divers flags du <see cref="Parser"/> qui
        /// modifient le groupement (tight chain extension, asterisk associativity
        /// flip). Toute alt qui diffère du default est exposée en cascade.
        /// Cf. ADR 30-04 <c>Feat-tight-implicit-mult-grouping</c> et
        /// <c>Feat-asterisk-tightness-associativity</c>.
        ///
        /// Cas typiques :
        /// <list type="bullet">
        /// <item><c>1/x+1</c> : default <c>\frac{1}{x}+1</c>, alt <c>\frac{1}{x+1}</c>
        ///   (tight extension)</item>
        /// <item><c>x^a+b</c> : default <c>x^{a}+b</c>, alt <c>x^{a+b}</c></item>
        /// <item><c>u_n+1</c> : default <c>u_{n}+1</c>, alt <c>u_{n+1}</c></item>
        /// <item><c>1/2*3/4</c> : default <c>\frac{(1/2)\cdot 3}{4}</c>, alt
        ///   <c>\frac{1}{2}\cdot \frac{3}{4}</c> (asterisk flip)</item>
        /// <item><c>1/2 * 3/4</c> : default <c>\frac{1}{2}\cdot \frac{3}{4}</c>,
        ///   alt <c>\frac{(1/2)\cdot 3}{4}</c> (asterisk flip inverse)</item>
        /// </list>
        ///
        /// Exposé en TOP-LEVEL uniquement (= toute la formule comme spot) pour
        /// rester simple. V2 ciblera la sous-expression précise si plusieurs
        /// élargissements coexistent dans la formule.
        /// </summary>
        private static void ScanTightChainExtension(string source, string topLatex,
            List<AmbiguityMatch> output, bool[] consumed)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(topLatex)) return;
            // Si le top-latex est déjà entièrement consommé par un autre match
            // (V→∀, vec coords, etc.), on ne propose pas l'alt (priorité aux
            // ambig structurelles).
            for (int i = 0; i < topLatex.Length; i++)
                if (consumed[i]) return;

            // Tester chaque combinaison non-default de flags. Collecter les
            // alts distinctes (déduplication via HashSet, ordre préservé via
            // List). Combinaisons : extension seule, flip seul, les deux.
            var seen = new HashSet<string> { topLatex };
            var altList = new List<string>();
            (bool extend, bool flip)[] combos = { (true, false), (false, true), (true, true) };
            foreach (var (extend, flip) in combos)
            {
                string? altLatex = TryReparse(source, extend, flip);
                if (altLatex == null) continue;
                if (seen.Add(altLatex)) altList.Add(altLatex);
            }
            if (altList.Count == 0) return;

            var alts = new List<AmbiguityAlternative>
            {
                new AmbiguityAlternative(topLatex, mutation: null),  // default
            };
            foreach (var alt in altList)
                alts.Add(new AmbiguityAlternative(alt, mutation: null));

            var spot = new AmbiguitySpot(RuleTightChainExtension, topLatex, alts);
            for (int i = 0; i < topLatex.Length; i++) consumed[i] = true;
            output.Add(new AmbiguityMatch(spot, 0, topLatex.Length));
        }

        /// <summary>
        /// Scan SOURCE pour le pattern `\d+\.\d+` (= deux nombres séparés
        /// par un point). Default = mult (rendu actuel `n \cdot m`), alt =
        /// décimal `n{,}m` (substitution typographique FR). Cf. ADR 30-04
        /// Feat-dot-as-multiplier §3.
        ///
        /// Critère strict : les deux côtés du `.` sont **purement
        /// numériques**. `a.b`, `2.x`, `x.2` ne déclenchent pas cette règle
        /// (ils sont rendus en mult sans alt décimal possible).
        ///
        /// Le scan se fait sur la SOURCE (pas le topLatex rendu) parce que
        /// le pattern est trivial à matcher via regex sur le texte saisi.
        /// La position dans topLatex est récupérée par recherche du
        /// substring `n\\cdot m` (ou `n\\times m` selon setting).
        /// </summary>
        private static void ScanDecimalVsMultiplication(string source, string topLatex,
            List<AmbiguityMatch> output, bool[] consumed)
        {
            if (string.IsNullOrEmpty(source)) return;
            int i = 0;
            while (i < source.Length)
            {
                if (!IsDigit(source[i])) { i++; continue; }
                int numStart = i;
                while (i < source.Length && IsDigit(source[i])) i++;
                int numEnd = i;
                // Doit être suivi de `.` puis d'au moins un chiffre.
                if (i >= source.Length || source[i] != '.') continue;
                int dotPos = i;
                if (i + 1 >= source.Length || !IsDigit(source[i + 1])) continue;
                i++;
                int frac1Start = i;
                while (i < source.Length && IsDigit(source[i])) i++;
                int frac1End = i;
                // On a un pattern `\d+\.\d+` complet : [numStart..frac1End).
                string num = source.Substring(numStart, numEnd - numStart);
                string frac = source.Substring(frac1Start, frac1End - frac1Start);
                // Default rendu = `num \cdot frac` (le `.` rend toujours \cdot).
                string defaultLatex = $"{num}\\cdot {frac}";
                string altDecimalLatex = $"{num}{{,}}{frac}";
                // Localiser dans topLatex
                int pos = topLatex.IndexOf(defaultLatex, System.StringComparison.Ordinal);
                if (pos < 0) continue;
                bool free = true;
                for (int k = pos; k < pos + defaultLatex.Length; k++)
                    if (consumed[k]) { free = false; break; }
                if (!free) continue;

                var alts = new List<AmbiguityAlternative>
                {
                    new AmbiguityAlternative(defaultLatex, mutation: null),
                    new AmbiguityAlternative(altDecimalLatex, mutation: null),
                };
                var spot = new AmbiguitySpot(RuleDecimalVsMultiplication, defaultLatex, alts);
                for (int k = pos; k < pos + defaultLatex.Length; k++) consumed[k] = true;
                output.Add(new AmbiguityMatch(spot, pos, pos + defaultLatex.Length));
            }
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        /// <summary>Re-parse avec les flags spécifiés. Retourne le LaTeX, ou null si erreur.</summary>
        private static string? TryReparse(string source, bool tightExtendsToOps, bool flipAsterisk)
        {
            try
            {
                var edges = Lexer.Lex(source);
                var paths = LatticePathFinder.TopK(edges, source.Length, 1);
                if (paths.Count == 0) return null;
                var parser = new Parser(paths[0].Edges)
                {
                    TightExtendsToOps = tightExtendsToOps,
                    FlipAsteriskAssociativity = flipAsterisk,
                };
                var ast = parser.Parse();
                return LatexRenderer.Render(ast);
            }
            catch { return null; }
        }

        private static void ScanFunctionTypicalWithCommaCoords(string source, string topLatex,
            List<AmbiguityMatch> output, bool[] consumed)
        {
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (c != 'f' && c != 'g' && c != 'h'
                    && c != 'F' && c != 'G' && c != 'H') continue;
                // Word boundary à gauche
                if (i > 0 && char.IsLetter(source[i - 1])) continue;
                // Suivi (peut-être après un espace) par `(`
                int j = i + 1;
                while (j < source.Length && source[j] == ' ') j++;
                if (j >= source.Length || source[j] != '(') continue;
                int openParen = j;
                // Trouver `)` matching (gestion parens imbriquées + brackets)
                int depth = 1;
                int k = openParen + 1;
                while (k < source.Length && depth > 0)
                {
                    char ck = source[k];
                    if (ck == '(' || ck == '[') depth++;
                    else if (ck == ')' || ck == ']') depth--;
                    if (depth == 0) break;
                    k++;
                }
                if (k >= source.Length) continue; // pas de `)` trouvée
                int closeParen = k;
                // Découpe top-level par virgule (les espaces sont juste de la
                // mise en forme à l'intérieur des cellules, pas des séparateurs).
                var segments = SplitTopLevelByComma(source, openParen + 1, closeParen);
                if (segments.Count < 2 || segments.Count > 3) continue;

                // Position dans topLatex de la lettre `f`. Heuristique : LaTeX
                // rendu garde généralement la lettre telle quelle (f → f).
                int topPos = i < topLatex.Length && topLatex[i] == c
                    ? i
                    : topLatex.IndexOf(c, 0);
                if (topPos < 0 || topPos >= topLatex.Length) continue;
                if (consumed[topPos]) continue;

                // Le default LaTeX correspond au function call rendu : `f\left(...\right)`.
                // On le retrouve en partant de `f` jusqu'à la `\right)` qui
                // correspond. Pour rester simple : on prend le span Latex
                // entre topPos et la fin du \left(...\right) qu'on identifie
                // par comptage. Si pas trouvable proprement, on saute.
                int defaultEnd = FindFuncCallEndInLatex(topLatex, topPos);
                if (defaultEnd < 0) continue;
                // Vérifier que toutes les positions [topPos, defaultEnd) sont
                // libres dans consumed (sinon collision avec un autre match).
                bool free = true;
                for (int p = topPos; p < defaultEnd; p++)
                    if (consumed[p]) { free = false; break; }
                if (!free) continue;

                var defaultLatex = topLatex.Substring(topPos, defaultEnd - topPos);

                // Construit l'aperçu de l'alt : muter `f` → ident vec-typique
                // (`u`) puis re-rendre, puis substituer `\vec{u}` → `\vec{f}`
                // (préservation du nom original).
                string vecTypicalLetter = "u";
                var alt1Source = MutateSource(source, i, 1, vecTypicalLetter);
                var alt1Rendered = RenderRaw(alt1Source);
                var alt1Latex = alt1Rendered.Replace($"\\vec{{{vecTypicalLetter}}}", $"\\vec{{{c}}}");

                // Pas de mutation au sens "appliquer l'alt" : la mutation logique
                // serait `f` → `vec f` (préfixer) mais le pipeline ne reconnaît
                // pas ça nativement comme coords ligne. Plus pragmatique : on
                // pose le LaTeX rendu directement comme alt, sans SourceMutation.
                // (Si l'utilisateur sélectionne, l'adapter substitue le LaTeX.)
                var alts = new List<AmbiguityAlternative>
                {
                    // Alt 0 : function call (default, identity — pas de mutation)
                    new AmbiguityAlternative(defaultLatex, mutation: null),
                    // Alt 1 : vec coords ligne `\vec{f}(...)`
                    new AmbiguityAlternative(alt1Latex, mutation: null),
                };
                var spot = new AmbiguitySpot(RuleVectorCoordsVsCall, defaultLatex, alts);
                for (int p = topPos; p < defaultEnd; p++) consumed[p] = true;
                output.Add(new AmbiguityMatch(spot, topPos, defaultEnd));
            }
        }

        /// <summary>
        /// Découpe [start, end) (string source) par virgules au depth 0
        /// (parens/brackets ignorées). Retourne les segments comme paires
        /// (start, end). Au moins 1 segment si la zone est non-vide.
        /// </summary>
        private static List<(int start, int end)> SplitTopLevelByComma(string source, int start, int end)
        {
            var result = new List<(int, int)>();
            int depth = 0;
            int prev = start;
            for (int i = start; i < end; i++)
            {
                char c = source[i];
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (c == ',' && depth == 0)
                {
                    result.Add((prev, i));
                    prev = i + 1;
                }
            }
            result.Add((prev, end));
            return result;
        }

        /// <summary>
        /// Trouve la fin d'un function call rendu en LaTeX : `<name>\left(<args>\right)`.
        /// Retourne l'index juste après la `\right)`. Si pas de pattern reconnu
        /// (ex: rendu intégré sans \left/\right), -1.
        /// </summary>
        private static int FindFuncCallEndInLatex(string topLatex, int nameStart)
        {
            // Avancer après les lettres du nom (1 char ASCII pour notre cas
            // f/g/h/F/G/H — par sécurité, on consomme toutes les lettres ASCII).
            int p = nameStart;
            while (p < topLatex.Length && char.IsLetter(topLatex[p])) p++;
            // Doit être suivi de `\left(`
            const string LeftParen = "\\left(";
            if (p + LeftParen.Length > topLatex.Length) return -1;
            if (topLatex.Substring(p, LeftParen.Length) != LeftParen) return -1;
            p += LeftParen.Length;
            // Chercher la `\right)` qui matche, en gérant l'imbrication
            int depth = 1;
            while (p < topLatex.Length)
            {
                if (p + LeftParen.Length <= topLatex.Length
                    && topLatex.Substring(p, LeftParen.Length) == LeftParen)
                { depth++; p += LeftParen.Length; continue; }
                const string RightParen = "\\right)";
                if (p + RightParen.Length <= topLatex.Length
                    && topLatex.Substring(p, RightParen.Length) == RightParen)
                {
                    depth--;
                    p += RightParen.Length;
                    if (depth == 0) return p;
                    continue;
                }
                p++;
            }
            return -1;
        }

        // Helpers source → LaTeX
        private static string MutateSource(string source, int offset, int length, string replacement)
            => source.Substring(0, offset) + replacement + source.Substring(offset + length);

        private static string RenderRaw(string source)
        {
            var edges = Lexer.Lex(source);
            var paths = LatticePathFinder.TopK(edges, source.Length, 1);
            if (paths.Count == 0) return source;
            var ast = new Parser(paths[0].Edges).Parse();
            return LatexRenderer.Render(ast);
        }

        /// <summary>
        /// Parcourt <paramref name="topLatex"/> et émet un match pour chaque
        /// séquence de 2 ou 3 majuscules consécutives entourées de non-lettres
        /// (word boundary). Évite les positions déjà consommées par d'autres
        /// matches AST-based.
        /// </summary>
        private static void ScanUppercaseSequences(string topLatex,
            List<AmbiguityMatch> output, bool[] consumed)
        {
            int i = 0;
            while (i < topLatex.Length)
            {
                if (!char.IsUpper(topLatex[i]) || consumed[i]) { i++; continue; }
                // Word boundary left
                if (i > 0 && char.IsLetter(topLatex[i - 1])) { i++; continue; }
                int j = i;
                while (j < topLatex.Length && char.IsUpper(topLatex[j]) && !consumed[j]) j++;
                int len = j - i;
                // Word boundary right (if more letters follow, c'est un mot plus
                // long, on n'émet aucun match — ex: ABCD ne propose ni AB ni ABC)
                if (j < topLatex.Length && char.IsLetter(topLatex[j])) { i = j; continue; }

                AmbiguitySpot? spot = null;
                if (len == 2)
                {
                    var pair = topLatex.Substring(i, 2);
                    spot = new AmbiguitySpot(
                        ruleId: RuleTwoUppercase,
                        defaultLatex: pair,
                        alternatives: new[]
                        {
                            new AmbiguityAlternative($"\\vec{{{pair}}}"),
                            new AmbiguityAlternative($"\\left({pair}\\right)"),
                            new AmbiguityAlternative($"\\left[{pair}\\right]"),
                        });
                }
                else if (len == 3)
                {
                    var triplet = topLatex.Substring(i, 3);
                    spot = new AmbiguitySpot(
                        ruleId: RuleThreeUppercase,
                        defaultLatex: triplet,
                        alternatives: new[]
                        {
                            new AmbiguityAlternative($"\\widehat{{{triplet}}}"),
                            new AmbiguityAlternative($"\\triangle {triplet}"),
                        });
                }
                // 4+ majuscules : on n'émet rien (pas de pattern géométrique
                // standard pour 4 lettres, et l'utilisateur n'a probablement
                // pas tapé ça sciemment).
                if (spot != null)
                {
                    for (int k = i; k < j; k++) consumed[k] = true;
                    output.Add(new AmbiguityMatch(spot, i, j));
                }
                i = j;
            }
        }

        /// <summary>
        /// Scan SOURCE (pas LaTeX rendu) : `V ` (V suivi d'un espace ou EOF)
        /// déclenche un Spot avec 3 alternatives :
        /// <list type="number">
        /// <item>V (identity) — pas de mutation, garde V comme variable. C'est
        ///   le choix par défaut si l'utilisateur continue à taper sans
        ///   sélectionner explicitement une alt.</item>
        /// <item>∀ (forall scope) — mutation `V` → `forall`, le pipeline rend
        ///   `\forall x \in R` (avec Holes pour les args manquants).</item>
        /// <item>√ (racine scope) — mutation `V` → `racine`, le pipeline rend
        ///   `\sqrt{x}` (avec Hole si pas d'arg).</item>
        /// </list>
        /// Idem E : 2 alts (E identity / ∃).
        ///
        /// Convention typographique : ∀ ressemble à un V à l'envers, ∃ à un E
        /// miroir, √ à un V étiré — l'utilisateur tape V/E et choisit dans la
        /// popup ce qu'il voulait dire.
        ///
        /// Trigger strict « V suivi d'un espace ou EOF » : les autres cas
        /// (V*x, V+x, Vx, V), V_x) ne déclenchent PAS l'ambig (V garde sa
        /// sémantique de variable / multiplicateur).
        /// </summary>
        private static void ScanVAsForallEAsExists(string source, string topLatex,
            List<AmbiguityMatch> output, bool[] consumed)
        {
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (c != 'V' && c != 'E') continue;
                if (i > 0 && char.IsLetter(source[i - 1])) continue;
                bool followedByDelim = i + 1 == source.Length || source[i + 1] == ' ';
                if (!followedByDelim) continue;

                int topPos = i < topLatex.Length && topLatex[i] == c ? i : topLatex.IndexOf(c, 0);
                if (topPos < 0 || topPos >= topLatex.Length || consumed[topPos]) continue;

                var ruleId = c == 'V' ? RuleVAsForall : RuleEAsExists;

                // Alt 0 : identity (pas de mutation). Aperçu = la source telle
                // qu'elle, rendue par le pipeline normal. Sélectionner cette
                // alt = "garder V" (popup se ferme, source inchangée).
                var identityPreview = RenderAfterMutation(source, i, 1, c.ToString()); // no-op mutation
                var alts = new List<AmbiguityAlternative>
                {
                    new AmbiguityAlternative(identityPreview, mutation: null),
                };

                if (c == 'V')
                {
                    // Alt 1 : ∀ — mutation V→forall
                    var forallPreview = RenderAfterMutation(source, i, 1, "forall");
                    alts.Add(new AmbiguityAlternative(
                        forallPreview,
                        new SourceMutation(i, 1, "forall")));

                    // Alt 2 : √ — mutation V→racine
                    var racinePreview = RenderAfterMutation(source, i, 1, "racine");
                    alts.Add(new AmbiguityAlternative(
                        racinePreview,
                        new SourceMutation(i, 1, "racine")));
                }
                else // E → ∃
                {
                    var existsPreview = RenderAfterMutation(source, i, 1, "exists");
                    alts.Add(new AmbiguityAlternative(
                        existsPreview,
                        new SourceMutation(i, 1, "exists")));
                }

                var spot = new AmbiguitySpot(ruleId, c.ToString(), alts);
                consumed[topPos] = true;
                output.Add(new AmbiguityMatch(spot, topPos, topPos + 1));
            }
        }

        private static void CollectAllMatchesRec(AstNode node, string topLatex,
            List<AmbiguityMatch> output, bool[] consumed)
        {
            var spot = MatchAmbiguity(node);
            if (spot != null)
            {
                // Cherche la position du defaultLatex dans topLatex avec :
                //  - Position non-consommée par un match précédent (sinon AB
                //    et BC dans "AB+BC" pointeraient au même endroit).
                //  - Word boundary : entouré de non-lettres (ou bord de
                //    string). Sans ça, "ABC" matcherait dans "ABCD" et
                //    "AB" matcherait dans "ABC" — le pattern serait juste
                //    un sous-string d'un mot plus long, pas un objet
                //    géométrique distinct.
                int idx = LastIndexOfWordBoundary(topLatex, spot.DefaultLatex, consumed);
                if (idx >= 0)
                {
                    int end = idx + spot.DefaultLatex.Length;
                    for (int i = idx; i < end; i++) consumed[i] = true;
                    output.Add(new AmbiguityMatch(spot, idx, end));
                    return; // match validé, on ne descend pas plus profond
                }
                // Pas de boundary trouvée : ce match parent est rejeté
                // (probablement sub-string d'un mot plus long). On descend
                // QUAND MÊME pour voir si un sous-AST plus petit matche
                // ailleurs dans la formule.
            }
            foreach (var child in GetChildrenRightFirst(node))
                CollectAllMatchesRec(child, topLatex, output, consumed);
        }

        /// <summary>
        /// Cherche la dernière occurrence de <paramref name="needle"/> dans
        /// <paramref name="text"/> qui satisfait :
        ///  1) toutes ses positions [idx..idx+len) sont libres dans consumed
        ///  2) word boundary : le caractère AVANT et APRÈS n'est pas une lettre
        ///     (ou bord de chaîne). Empêche le match d'un sub-string de mot
        ///     plus long (ex: AB dans ABCD).
        /// Retourne -1 si aucune occurrence valide.
        /// </summary>
        private static int LastIndexOfWordBoundary(string text, string needle, bool[] consumed)
        {
            int idx = text.LastIndexOf(needle, System.StringComparison.Ordinal);
            while (idx >= 0)
            {
                bool free = true;
                for (int i = idx; i < idx + needle.Length; i++)
                    if (consumed[i]) { free = false; break; }
                if (free)
                {
                    bool boundLeft = idx == 0 || !char.IsLetter(text[idx - 1]);
                    bool boundRight = idx + needle.Length == text.Length
                                   || !char.IsLetter(text[idx + needle.Length]);
                    if (boundLeft && boundRight) return idx;
                }
                if (idx == 0) return -1;
                idx = text.LastIndexOf(needle, idx - 1, System.StringComparison.Ordinal);
            }
            return -1;
        }

        private static (AstNode? sub, AmbiguitySpot? spot) TraverseRightmost(AstNode node)
        {
            // PRÉ-ORDRE right-first : on teste le node COURANT avant de descendre
            // dans les enfants. Ça favorise les patterns les plus LARGES (= les
            // plus contextuels). Ex: pour ABC (Bin(*, Bin(*, A, B), C)) on veut
            // exposer l'ambig sur ABC entier (angle/triangle), pas l'ambig
            // partielle sur AB qui n'a aucun sens dans ce contexte.
            var spot = MatchAmbiguity(node);
            if (spot != null) return (node, spot);

            foreach (var child in GetChildrenRightFirst(node))
            {
                var result = TraverseRightmost(child);
                if (result.spot != null) return result;
            }
            return (null, null);
        }

        private static IEnumerable<AstNode> GetChildrenRightFirst(AstNode node) => node switch
        {
            Bin b => new[] { b.Rhs, b.Lhs },
            Sup s => new[] { s.Exp, s.Base },
            Sub s => new[] { s.Idx, s.Base },
            Group g => new[] { g.Expr },
            Frac f => new[] { f.Den, f.Num },
            Sqrt sq => new[] { sq.Arg },
            Func fn => new[] { fn.Arg },
            Sum sum => new[] { sum.Body, sum.End, sum.Start, sum.Var },
            Lim l => new[] { l.Body, l.Target, l.Var },
            Int it => new[] { it.Body, it.High, it.Low },
            Unary u => new[] { u.Arg },
            _ => Enumerable.Empty<AstNode>(),
        };

        // ----------------- Règles de matching -----------------

        /// <summary>
        /// Teste si un nœud AST est ambigu (patterns structurels uniquement).
        /// Les patterns string-based (séquences de majuscules AB/ABC pour les
        /// objets géométriques) sont gérés par <see cref="ScanUppercaseSequences"/>
        /// qui scanne directement le topLatex rendu — l'AST gauche-associatif
        /// ne regroupe pas toujours les lettres ensemble (ex: AB*CD donne
        /// ((A*B)*C)*D, donc CD n'est jamais un sous-Bin direct).
        /// </summary>
        private static AmbiguitySpot? MatchAmbiguity(AstNode node)
        {
            // Règle 2 : Sup d'une lettre 1-char par un Number, MAIS seulement
            // si le Sup est IMPLICITE (issu de la règle Number-tight, ex: x2).
            // Si l'utilisateur a tapé `x^2` explicitement, IsImplicit=false →
            // pas d'ambig (l'utilisateur a déjà tranché en mettant le ^).
            if (node is Sup s && s.IsImplicit
                && s.Base is Atom sb && sb.Kind == "ident" && sb.Value.Length == 1
                && s.Exp is Atom se && se.Kind == "number")
            {
                return new AmbiguitySpot(
                    ruleId: RuleLetterSupNumber,
                    defaultLatex: $"{sb.Value}^{{{se.Value}}}",
                    alternatives: new[]
                    {
                        new AmbiguityAlternative($"{sb.Value}_{{{se.Value}}}"),
                    });
            }

            // Règle 3 : multiplication EXPLICITE entre deux lettres simples
            // (`a*b`, `u*v`, `A*B`, `x*y`…) → propose le produit scalaire de
            // vecteurs en alt :
            //   default `a\cdot b`   alt `\vec{a} \cdot \vec{b}`
            // Restriction au `*` explicite (pas la juxtaposition `ab`/`AB`)
            // pour ne pas voler le terrain aux autres cascades :
            //   `AB` juxtaposition → RuleTwoUppercase (vec/paren/crochet)
            //   `V x` juxtaposition → RuleVAsForall (∀)
            //   `xy` juxtaposition → variables liées, pas de promotion vec
            // La présence du `*` est un signal explicite que l'utilisateur
            // distingue les opérandes — d'où la pertinence de la cascade.
            if (node is Bin b && (b.Op == "*" || b.Op == ".") && !b.Implicit
                && b.Lhs is Atom la && la.Kind == "ident" && IsSingleLetterIdent(la.Value)
                && b.Rhs is Atom ra && ra.Kind == "ident" && IsSingleLetterIdent(ra.Value))
            {
                // DefaultLatex doit matcher le rendu actuel pour que le scan
                // localise correctement le spot dans topLatex.
                // - Bin("*") rend selon GlobalOptions.MultSymbol (cf. ADR
                //   Feat-explicit-mult-times-vs-cdot).
                // - Bin(".") rend toujours `\cdot` (lecture littérale du point,
                //   cf. ADR Feat-dot-as-multiplier).
                // L'alt vec dot product reste en `\cdot` indépendamment du
                // setting (convention math du produit scalaire vectoriel).
                string symbol = b.Op == "."
                    ? "\\cdot "
                    : LatexRenderer.GlobalOptions.MultSymbol;
                string defaultLatex = $"{la.Value}{symbol}{ra.Value}";
                string altLatex = $"\\vec{{{la.Value}}} \\cdot \\vec{{{ra.Value}}}";
                return new AmbiguitySpot(
                    ruleId: RuleVecDotProduct,
                    defaultLatex: defaultLatex,
                    alternatives: new[]
                    {
                        new AmbiguityAlternative(altLatex),
                    });
            }

            return null;
        }

        // Identifiant 1-lettre alphabétique (lower ou upper). Permet de
        // proposer une interprétation vecteur sur `a*b`, `x*y`, `A*B`, etc.
        // — l'utilisateur trie via la cascade.
        private static bool IsSingleLetterIdent(string ident)
            => ident.Length == 1 && char.IsLetter(ident[0]);

        // Identifiants des règles d'ambiguïté — utilisés par la popup pour
        // mémoriser les préférences utilisateur par TYPE de pattern (et non
        // par instance string).
        public const string RuleTwoUppercase = "two-uppercase";
        public const string RuleThreeUppercase = "three-uppercase";
        public const string RuleLetterSupNumber = "letter-sup-number";
        public const string RuleVAsForall = "v-as-forall";
        public const string RuleEAsExists = "e-as-exists";
        public const string RuleCanonicalSet = "canonical-set";
        // Ambig f(1, 2) : function call (default) vs vec coords \vec{f}(1, 2).
        // Cf. brief 2026-04-29-vector-coordinates-shorthand §3.1. Le pattern
        // n'est déclenché QUE pour les idents typique fonction (f, g, h, F,
        // G, H) suivis d'une paren contenant 2 ou 3 valeurs séparées par
        // virgule (= layout row only — pour layout column le parser a déjà
        // tranché en faveur des coords sans ambig).
        public const string RuleVectorCoordsVsCall = "vector-coords-vs-call";
        public const string RuleVectorLayoutFlip = "vector-layout-flip";
        public const string RuleVecDotProduct = "vec-dot-product";
        // Élargissement tight chain : default `1/x+1` → \frac{1}{x}+1 (PEMDAS) ;
        // alt `\frac{1}{x+1}` (chaîne tight aux ops). Cf. ADR 2026-04-30
        // Feat-tight-implicit-mult-grouping. Pareil pour `^a+b`, `_n+1`.
        public const string RuleTightChainExtension = "tight-chain-extension";
        // Décimal vs multiplication : `3.4` rendu `3 \cdot 4` par défaut
        // (notation FR pure, ADR Feat-dot-as-multiplier), alt = décimal `3{,}4`.
        // Concerne UNIQUEMENT le pattern `\d+\.\d+` (deux nombres séparés
        // par `.`). Lettres ou expressions ne déclenchent pas cette règle.
        public const string RuleDecimalVsMultiplication = "decimal-vs-multiplication";

        /// <summary>
        /// Simule l'application d'une <see cref="SourceMutation"/> sur la
        /// source brute et renvoie le LaTeX rendu post-mutation. Utilisé pour
        /// produire un aperçu fidèle de ce que l'utilisateur verra s'il
        /// accepte l'alternative — sans ça, l'aperçu serait toujours générique
        /// (carrés vides) même quand la source contient déjà var/set.
        /// </summary>
        private static string RenderAfterMutation(string source, int offset, int length, string replacement)
        {
            if (offset < 0 || offset + length > source.Length) return replacement;
            var muted = source.Substring(0, offset) + replacement + source.Substring(offset + length);
            var edges = Lexer.Lex(muted);
            var paths = LatticePathFinder.TopK(edges, muted.Length, 1);
            if (paths.Count == 0) return replacement;
            var ast = new Parser(paths[0].Edges).Parse();
            return LatexRenderer.Render(ast);
        }
    }
}
