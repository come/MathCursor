using System.Collections.Generic;
using MathCursor.Core.Lattice;

namespace MathCursor.Core
{
    /// <summary>
    /// Résultat de <see cref="ZoneResolver.Resolve"/> : le pipeline complet
    /// (Lex → TopK → Parse → Render → ambig spot) appliqué à une source brute,
    /// avec les préférences de désambiguïsation déjà mutées.
    ///
    /// <para>Conçu pour être passé tel quel à la popup : tout ce dont elle a
    /// besoin pour s'afficher est ici, plus le flag <see cref="IsIncomplete"/>
    /// qui pilote l'extension de zone côté adapter (popup reste ouverte tant
    /// que la zone n'est pas finie).</para>
    /// </summary>
    public sealed class ResolvedZone
    {
        /// <summary>La source telle que tapée par l'utilisateur (avant prefs).</summary>
        public string RawSource { get; }

        /// <summary>La source après application des prefs source-mutation.
        /// Égale à <see cref="RawSource"/> si aucune pref n'a été appliquée.</summary>
        public string MutedSource { get; }

        /// <summary>Top-1 LaTeX du pipeline sur la source mutée.</summary>
        public string TopLatex { get; }

        /// <summary>Ambiguïté la plus à droite (s'il y en a une), ou null.</summary>
        public AmbiguitySpot? Spot { get; }

        /// <summary>Toutes les ambiguïtés détectées (pour cascade popup).</summary>
        public IReadOnlyList<AmbiguityMatch> AllMatches { get; }

        /// <summary>Position du Spot dans <see cref="TopLatex"/>.</summary>
        public int? SpotStart { get; }
        public int? SpotEnd { get; }

        /// <summary>True si la zone est en attente d'un input — la popup doit
        /// rester ouverte si l'utilisateur tape un espace au-delà de la zone NER.
        /// Deux conditions :
        /// <list type="number">
        /// <item>Le LaTeX rendu contient un <c>\square</c> (slot Hole vacant,
        ///   ex: scope <c>somme k</c> sans body, ou <c>forall x</c> sans set).</item>
        /// <item>Le dernier caractère non-whitespace de la source brute est un
        ///   opérateur binaire (+, -, *, /, =, &lt;, &gt;, ^, _, ,) — l'opérande
        ///   suivante reste à taper.</item>
        /// </list>
        /// </summary>
        public bool IsIncomplete { get; }

        public ResolvedZone(string rawSource, string mutedSource, string topLatex,
            AmbiguitySpot? spot, int? spotStart, int? spotEnd,
            IReadOnlyList<AmbiguityMatch> allMatches, bool isIncomplete)
        {
            RawSource = rawSource;
            MutedSource = mutedSource;
            TopLatex = topLatex;
            Spot = spot;
            SpotStart = spotStart;
            SpotEnd = spotEnd;
            AllMatches = allMatches;
            IsIncomplete = isIncomplete;
        }
    }

    /// <summary>
    /// Point d'entrée unique pour la résolution de zone : transforme une source
    /// brute en <see cref="ResolvedZone"/> en tenant compte des préférences de
    /// désambiguïsation accumulées dans la session.
    ///
    /// <para>État interne : un dictionnaire ruleId → altIdx qui mémorise les
    /// choix de l'utilisateur. Ex : « pour cette session, V isolé doit être
    /// résolu en ∀ (altIdx=1) ». Les futures résolutions appliquent
    /// automatiquement la mutation source correspondante avant le pipeline.</para>
    ///
    /// <para>Cycle de vie : un <see cref="ZoneResolver"/> par session de saisie
    /// (= entre l'ouverture de la popup et son commit/Esc). Reset via
    /// <see cref="Clear"/>.</para>
    /// </summary>
    public sealed class ZoneResolver
    {
        // Caractères opérateurs binaires qui justifient une extension forward
        // (l'utilisateur attend l'opérande suivante).
        private const string TrailingOperatorChars = "+-*/=<>^_,";

        // Garde-fou : nombre max d'itérations de mutation pour éviter les
        // boucles infinies si une mutation crée un nouveau pattern qui
        // re-déclenche le même ruleId.
        private const int MaxMutationIterations = 16;

        private readonly LatticeEngine _engine;
        private readonly Dictionary<string, int> _preferences
            = new Dictionary<string, int>();

        public ZoneResolver(LatticeEngine engine)
        {
            _engine = engine ?? throw new System.ArgumentNullException(nameof(engine));
        }

        /// <summary>
        /// Mémorise un choix d'alternative pour la session. Les futures
        /// résolutions appliqueront automatiquement la mutation source
        /// correspondante avant de lancer le pipeline.
        /// </summary>
        public void AddPreference(string ruleId, int altIdx)
        {
            if (string.IsNullOrEmpty(ruleId)) return;
            _preferences[ruleId] = altIdx;
        }

        /// <summary>Reset les préférences (Esc, commit final, sortie de zone).</summary>
        public void Clear() => _preferences.Clear();

        /// <summary>Indique si une préférence est mémorisée pour ce ruleId.</summary>
        public bool HasPreference(string ruleId)
            => !string.IsNullOrEmpty(ruleId) && _preferences.ContainsKey(ruleId);

        /// <summary>
        /// Résout une source brute : applique les préférences source-mutation
        /// récursivement, lance le pipeline, calcule <see cref="ResolvedZone.IsIncomplete"/>.
        /// </summary>
        public ResolvedZone Resolve(string rawSource)
        {
            rawSource = rawSource ?? string.Empty;
            // Préprocesseur : `R*`/`N+`/`Z*-` etc. (lettre canonique + 1 ou 2
            // signes modificateurs avec délim derrière) → `bbR*`/`bbN+`/`bbZ*-`.
            // Aliasing direct car la présence d'un modificateur exclut
            // l'interprétation "lettre variable" (pi*R*x ne veut rien dire).
            // Pas de désambig nécessaire, transformation silencieuse.
            var preprocessed = PreprocessCanonicalSetModifiers(rawSource);
            var muted = ApplyPreferences(preprocessed);
            var ambig = _engine.ConvertWithAmbiguity(muted);
            bool incomplete = ComputeIsIncomplete(rawSource, ambig.TopLatex);
            return new ResolvedZone(
                rawSource: rawSource,
                mutedSource: muted,
                topLatex: ambig.TopLatex,
                spot: ambig.Spot,
                spotStart: ambig.SpotStart,
                spotEnd: ambig.SpotEnd,
                allMatches: ambig.AllMatches,
                isIncomplete: incomplete);
        }

        /// <summary>
        /// Détecte les patterns `[RNZQC][*+-]{1,2}` suivis d'un délim et les
        /// remplace par `bb<L><modifs>`. Ex: `R*` → `bbR*`, `N+*` → `bbN+*`.
        /// Pas de mutation si la lettre est précédée d'une autre lettre
        /// (= elle fait partie d'un mot, ex `volume*x`).
        /// </summary>
        private static string PreprocessCanonicalSetModifiers(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            var sb = new System.Text.StringBuilder(source.Length + 16);
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                bool isCanonical = (c == 'R' || c == 'N' || c == 'Z' || c == 'Q' || c == 'C');
                bool wordBoundaryLeft = i == 0 || !char.IsLetter(source[i - 1]);
                if (isCanonical && wordBoundaryLeft)
                {
                    // Compte 1 ou 2 modificateurs tight juste après la lettre
                    int j = i + 1;
                    while (j < source.Length && (j - (i + 1)) < 2
                           && (source[j] == '*' || source[j] == '+' || source[j] == '-'))
                        j++;
                    int modifierCount = j - (i + 1);
                    if (modifierCount > 0)
                    {
                        // Vérifier que ce qui suit n'est PAS une opérande math
                        // (chiffre, lettre, paren ouvrante) — sinon c'est une
                        // expression arithmétique normale (R*x, R+5, etc.)
                        char afterMod = j < source.Length ? source[j] : '\0';
                        bool isTerminal = afterMod == '\0'
                            || char.IsWhiteSpace(afterMod)
                            || afterMod == ',' || afterMod == ';' || afterMod == '.'
                            || afterMod == ')' || afterMod == ']' || afterMod == '}';
                        if (isTerminal)
                        {
                            sb.Append("bb").Append(c);
                            sb.Append(source, i + 1, modifierCount);
                            i = j;
                            continue;
                        }
                    }
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Applique les préférences source-mutation à la source brute.
        /// Itère jusqu'à fixpoint : à chaque tour, on convertit et on cherche
        /// dans <see cref="AmbiguityResult.AllMatches"/> le PREMIER match dont
        /// le ruleId a une pref enregistrée ET dont l'alt préférée a une mutation.
        /// On applique cette mutation, on recommence. Stop quand aucun match
        /// applicable.
        ///
        /// On scanne tous les matches (pas juste le rightmost) parce qu'une
        /// pref peut concerner un match qui n'est pas le plus à droite — ex:
        /// `V x R` avec pref forall pour V mais Spot rightmost = R (canonical-set).
        /// Sans cette généralisation, la pref V→forall ne s'appliquerait jamais.
        /// </summary>
        private string ApplyPreferences(string source)
        {
            if (_preferences.Count == 0 || string.IsNullOrEmpty(source))
                return source;
            for (int i = 0; i < MaxMutationIterations; i++)
            {
                var r = _engine.ConvertWithAmbiguity(source);
                SourceMutation? mutToApply = null;
                foreach (var m in r.AllMatches)
                {
                    if (!_preferences.TryGetValue(m.Spot.RuleId, out var altIdx)) continue;
                    if (altIdx < 0 || altIdx >= m.Spot.Alternatives.Count) continue;
                    var alt = m.Spot.Alternatives[altIdx];
                    if (alt.Mutation == null) continue; // identity, rien à appliquer
                    mutToApply = alt.Mutation;
                    break;
                }
                if (mutToApply == null) return source;
                source = source.Substring(0, mutToApply.Offset)
                       + mutToApply.Replacement
                       + source.Substring(mutToApply.Offset + mutToApply.Length);
            }
            return source;
        }

        private static bool ComputeIsIncomplete(string rawSource, string topLatex)
        {
            // (1) Hole non rempli dans le rendu → un slot reste à taper
            if (!string.IsNullOrEmpty(topLatex) && topLatex.Contains("\\square"))
                return true;
            // (2) Dernier char non-whitespace = opérateur binaire → opérande
            // suivante en attente
            if (!string.IsNullOrEmpty(rawSource))
            {
                int i = rawSource.Length - 1;
                while (i >= 0 && char.IsWhiteSpace(rawSource[i])) i--;
                if (i >= 0 && TrailingOperatorChars.IndexOf(rawSource[i]) >= 0)
                    return true;
            }
            return false;
        }
    }
}
