using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Top-1 LaTeX <b>avant</b> splice contextuel (RulePin /
        /// SpanOverride / SidecarSignal). Identique à <see cref="TopLatex"/>
        /// quand aucun splice contextuel n'est appliqué. Utilisé par la
        /// popup pour ses recalculs sans subir le double-splice
        /// (cf. brief 2026-05-07 fix double-splice).</summary>
        public string BaseTopLatex { get; }

        public ResolvedZone(string rawSource, string mutedSource, string topLatex,
            AmbiguitySpot? spot, int? spotStart, int? spotEnd,
            IReadOnlyList<AmbiguityMatch> allMatches, bool isIncomplete,
            string? baseTopLatex = null)
        {
            RawSource = rawSource;
            MutedSource = mutedSource;
            TopLatex = topLatex;
            Spot = spot;
            SpotStart = spotStart;
            SpotEnd = spotEnd;
            AllMatches = allMatches;
            IsIncomplete = isIncomplete;
            BaseTopLatex = baseTopLatex ?? topLatex;
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

        /// <summary>
        /// Retire la préférence pour <paramref name="ruleId"/>. Utilisé quand
        /// l'utilisateur clique sur le defaultLatex brut dans la popup
        /// (= revert). Re-resolve repartira de la source originale sans
        /// aucune mutation pour cette rule.
        /// </summary>
        public void RemovePreference(string ruleId)
        {
            if (string.IsNullOrEmpty(ruleId)) return;
            _preferences.Remove(ruleId);
        }

        /// <summary>Reset les préférences (Esc, commit final, sortie de zone).</summary>
        public void Clear() => _preferences.Clear();

        /// <summary>
        /// Construit un <see cref="MathCursor.Core.Resolution.ResolutionSidecar"/>
        /// reflétant l'état courant de <c>_preferences</c> sous forme de
        /// <c>RulePins</c> (= choix rule-level session). Pas de
        /// <c>SpanPins</c>/<c>SpanOverrides</c> (legacy).
        ///
        /// <para>Utilisé par le service pour propager les choix utilisateur
        /// aux re-pipelines (cross-merge multi-ligne, store post-commit).
        /// Remplace l'ancien <c>SuggestionPopupWindow.CurrentSidecar</c> qui
        /// dérivait des SpanPins locaux popup — source unique de vérité
        /// maintenant côté resolver. Cf. refacto désambig 2026-05-21 (D).</para>
        /// </summary>
        public MathCursor.Core.Resolution.ResolutionSidecar BuildSidecar()
        {
            if (_preferences.Count == 0)
                return MathCursor.Core.Resolution.ResolutionSidecar.Empty;
            var rulePins = new MathCursor.Core.Resolution.RulePin[_preferences.Count];
            int i = 0;
            foreach (var kv in _preferences)
                rulePins[i++] = new MathCursor.Core.Resolution.RulePin(kv.Key, kv.Value);
            return new MathCursor.Core.Resolution.ResolutionSidecar(
                spanPins: System.Array.Empty<MathCursor.Core.Resolution.SpanPin>(),
                zoneVotes: new Dictionary<string, IReadOnlyDictionary<int, int>>(),
                rulePins: rulePins,
                spanOverrides: System.Array.Empty<MathCursor.Core.Resolution.SpanOverride>());
        }

        /// <summary>Indique si une préférence est mémorisée pour ce ruleId.</summary>
        public bool HasPreference(string ruleId)
            => !string.IsNullOrEmpty(ruleId) && _preferences.ContainsKey(ruleId);

        /// <summary>
        /// Résout avec un <see cref="MathCursor.Core.Resolution.GlobalContext"/>
        /// qui agrège plusieurs <see cref="MathCursor.Core.Resolution.IContextSignal"/>s
        /// (sidecar L1, résolutions ¶ L2, etc.). Point d'entrée recommandé :
        /// le SuggestionService passe son GlobalContext de session.
        ///
        /// <para>Logique :</para>
        /// <list type="number">
        ///   <item>Pipeline normal sur <paramref name="rawSource"/>.</item>
        ///   <item>Pour chaque ambiguïté, cherche un <see cref="MathCursor.Core.Resolution.SpanPin"/>
        ///     matchant span-level (offset + len + rule + source.Substring).
        ///     Si trouvé, last-write-wins → splice direct. C'est l'ancien
        ///     comportement pin du sidecar, préservé pour la précision span.</item>
        ///   <item>Sinon, demande <see cref="MathCursor.Core.Resolution.ScoringHints.BestAltForRule"/>
        ///     pour cette rule. Si une alt a un score positif, splice.</item>
        ///   <item>Sinon laisse le défaut.</item>
        /// </list>
        ///
        /// <para>Cf. brief <c>2026-05-07-global-context-multi-zoom-ranking</c>
        /// + ADR sidecar 06-05 (les pins span-level restent dominants pour
        /// préserver les choix utilisateur localisés).</para>
        /// </summary>
        public ResolvedZone Resolve(
            string rawSource,
            MathCursor.Core.Resolution.GlobalContext? globalCtx,
            MathCursor.Core.Resolution.ResolutionSidecar? sidecar)
        {
            var baseResolved = Resolve(rawSource);
            bool hasSidecar = sidecar != null && !sidecar.IsEmpty;
            bool hasContext = globalCtx != null && globalCtx.SignalCount > 0;
            if (!hasSidecar && !hasContext) return baseResolved;

            string topLatex = baseResolved.TopLatex ?? string.Empty;
            string source = rawSource ?? string.Empty;

            // Hints agrégés depuis tous les signaux configurés.
            MathCursor.Core.Resolution.ScoringHints hints;
            if (globalCtx != null)
            {
                var snapshot = globalCtx.Snapshot(
                    rawSource,
                    sidecar ?? MathCursor.Core.Resolution.ResolutionSidecar.Empty);
                hints = globalCtx.Scorer.Aggregate(snapshot);
            }
            else
            {
                hints = MathCursor.Core.Resolution.ScoringHints.Empty;
            }

            // Construit la liste des splices à appliquer + le mapping
            // match → altIdx appliqué (utilisé pour annoter AppliedAltIdx).
            var (splices, appliedByMatch) = BuildSplices(
                baseResolved.AllMatches, source, sidecar, hints, topLatex.Length);

            // Apply right-to-left pour préserver les positions.
            splices.Sort((a, b) => b.Start.CompareTo(a.Start));
            foreach (var s in splices)
            {
                if (s.Start < 0 || s.End > topLatex.Length || s.Start >= s.End) continue;
                topLatex = topLatex.Substring(0, s.Start) + s.AltLatex + topLatex.Substring(s.End);
            }

            // Comportement V1 souhaité par l'utilisateur (2026-05-07) : la popup
            // d'ambig reste ouverte pour permettre changement (l'utilisateur
            // peut vouloir une autre alt pour ce span précis). Le scoring
            // contextuel a déjà splicé le TopLatex avec l'alt préférée — donc
            // l'utilisateur voit le rendu attendu (\vec{AD}+\vec{DE}=\vec{AE}).
            // S'il valide direct, c'est cette résolution qui passe.
            //
            // Conséquence : Spot et AllMatches sont retournés tels quels,
            // pas filtrés. Itération future : aligner la sélection par
            // défaut de la popup sur l'alt scorée.
            // Enrichit AllMatches avec AppliedAltIdx (= ce que le splice a
            // effectivement appliqué). Garantie de cohérence avec le filtre
            // popup côté SuggestionPopupWindow.
            IReadOnlyList<AmbiguityMatch> enrichedMatches;
            if (appliedByMatch.Count == 0)
            {
                enrichedMatches = baseResolved.AllMatches;
            }
            else
            {
                var list = new System.Collections.Generic.List<AmbiguityMatch>(baseResolved.AllMatches.Count);
                foreach (var m in baseResolved.AllMatches)
                {
                    if (appliedByMatch.TryGetValue(m, out int altIdx))
                        list.Add(m.WithAppliedAlt(altIdx));
                    else
                        list.Add(m);
                }
                enrichedMatches = list;
            }

            return new ResolvedZone(
                rawSource: baseResolved.RawSource,
                mutedSource: baseResolved.MutedSource,
                topLatex: topLatex,
                spot: baseResolved.Spot,
                spotStart: baseResolved.SpotStart,
                spotEnd: baseResolved.SpotEnd,
                allMatches: enrichedMatches,
                isIncomplete: baseResolved.IsIncomplete,
                baseTopLatex: baseResolved.TopLatex);
        }

        /// <summary>
        /// Pour chaque match, décide quel <c>altIdx</c> doit s'appliquer sur
        /// le <c>topLatex</c>. 4 cas :
        /// <list type="number">
        /// <item><b>Pref session avec Mutation native</b> → annote
        /// AppliedAltIdx, PAS de splice (<c>ApplyPreferences</c> a déjà muté
        /// la source en amont, splice ferait du nesting).</item>
        /// <item><b>Pref session sans Mutation native</b> (ex.
        /// <c>tight-chain-extension</c>) → annote + splice <c>alt.Latex</c>
        /// dans <c>topLatex</c>.</item>
        /// <item><b>Pas de pref + ResolveBestAlt &gt;= 0</b> (= sidecar pin
        /// ou hints contextuels) → annote + splice.</item>
        /// <item><b>Aucun</b> → match laissé tel quel.</item>
        /// </list>
        /// </summary>
        /// <param name="topLatexLength">Longueur du <c>topLatex</c> pour bornes-check.</param>
        /// <returns>
        /// <c>(splices, appliedByMatch)</c> : la liste des splices à appliquer
        /// (sans tri) + le mapping <c>match → altIdx appliqué</c> pour
        /// annotation <c>AppliedAltIdx</c>.
        /// </returns>
        private (List<(int Start, int End, string AltLatex)> splices,
                 Dictionary<AmbiguityMatch, int> appliedByMatch)
            BuildSplices(
                IReadOnlyList<AmbiguityMatch> matches,
                string source,
                MathCursor.Core.Resolution.ResolutionSidecar? sidecar,
                MathCursor.Core.Resolution.ScoringHints hints,
                int topLatexLength)
        {
            var splices = new List<(int Start, int End, string AltLatex)>();
            var appliedByMatch = new Dictionary<AmbiguityMatch, int>();

            if (matches == null) return (splices, appliedByMatch);

            foreach (var match in matches)
            {
                if (match.Spot == null || string.IsNullOrEmpty(match.Spot.RuleId)) continue;
                if (match.Start < 0 || match.End > topLatexLength || match.Start >= match.End) continue;

                // Cas 1+2 : pref session prioritaire (vs sidecar/hints).
                if (_preferences.TryGetValue(match.Spot.RuleId, out int prefAlt)
                    && prefAlt >= 0 && prefAlt < match.Spot.Alternatives.Count)
                {
                    var prefAltObj = match.Spot.Alternatives[prefAlt];
                    appliedByMatch[match] = prefAlt;
                    if (prefAltObj.Mutation != null) continue; // cas 1 : déjà muté
                    splices.Add((match.Start, match.End, prefAltObj.Latex)); // cas 2
                    continue;
                }

                // Cas 3 : sidecar / hints contextuels.
                int bestAlt = ResolveBestAlt(match, source, sidecar, hints);
                if (bestAlt < 0 || bestAlt >= match.Spot.Alternatives.Count) continue;

                appliedByMatch[match] = bestAlt;
                splices.Add((match.Start, match.End, match.Spot.Alternatives[bestAlt].Latex));
            }

            return (splices, appliedByMatch);
        }

        /// <summary>
        /// Décide l'alternative à appliquer pour un match donné. Ordre de
        /// précédence (cf. brief 2026-05-07-rule-pin-span-override-refactor) :
        /// <list type="number">
        /// <item><b>SpanOverride</b> par signature (v2) — choix explicite
        /// localisé. Si <see cref="MathCursor.Core.Resolution.SpanOverride.IsRevert"/>
        /// → retourne -1 sans fallback (l'utilisateur veut explicitement le
        /// default ici).</item>
        /// <item><b>RulePin</b> par rule (v2) — choix session-wide.</item>
        /// <item><b>SpanPin legacy</b> par offset+len+source.Substring
        /// (v1) — préservation pour les sidecars non encore migrés.</item>
        /// <item><b>ScoringHints</b> contextuels (via signaux GlobalContext).</item>
        /// </list>
        /// Retourne <c>-1</c> = pas de splice (default reste affiché).
        /// </summary>
        private static int ResolveBestAlt(
            Lattice.AmbiguityMatch match,
            string source,
            MathCursor.Core.Resolution.ResolutionSidecar? sidecar,
            MathCursor.Core.Resolution.ScoringHints hints)
        {
            int altCount = match.Spot.Alternatives.Count;

            // 1) SpanOverride v2 par signature.
            if (sidecar != null && match.Signature != null)
            {
                foreach (var ov in sidecar.SpanOverrides)
                {
                    if (!ov.Signature.Equals(match.Signature)) continue;
                    if (ov.IsRevert) return -1;  // explicit revert → no fallback
                    if (ov.AltIdx >= 0 && ov.AltIdx < altCount)
                        return ov.AltIdx;
                }
            }

            // 2) RulePin v2 par rule.
            if (sidecar != null)
            {
                foreach (var rp in sidecar.RulePins)
                {
                    if (rp.RuleId != match.Spot.RuleId) continue;
                    if (rp.AltIdx >= 0 && rp.AltIdx < altCount)
                        return rp.AltIdx;
                }
            }

            // 3) SpanPin legacy v1. Préservation pour les sidecars non
            // encore migrés (= ouverture d'OMaths anciens en mode edit).
            // Last-write-wins.
            int lastPinAlt = -1;
            if (sidecar != null)
            {
                foreach (var pin in sidecar.SpanPins)
                {
                    if (pin.Rule != match.Spot.RuleId) continue;
                    if (pin.Offset < 0 || pin.Len <= 0) continue;
                    if (pin.Offset + pin.Len > source.Length) continue;
                    if (source.Substring(pin.Offset, pin.Len) != match.Spot.DefaultLatex) continue;
                    if (pin.AltIdx < 0 || pin.AltIdx >= altCount) continue;
                    lastPinAlt = pin.AltIdx;
                }
            }
            if (lastPinAlt >= 0) return lastPinAlt;

            // 4) Hints contextuels (signaux GlobalContext).
            var (alt, score) = hints.BestAltForRule(match.Spot.RuleId);
            if (alt < 0 || score <= 0) return -1;
            return alt;
        }


        /// <summary>
        /// Overload rétro-compat : équivalent à
        /// <see cref="Resolve(string, GlobalContext, ResolutionSidecar)"/>
        /// avec un <see cref="MathCursor.Core.Resolution.GlobalContext"/>
        /// jetable wrappant un <see cref="MathCursor.Core.Resolution.Signals.SidecarSignal"/>.
        /// Conservé pour les tests + appels legacy qui n'ont pas de
        /// <c>GlobalContext</c> de session sous la main.
        /// </summary>
        public ResolvedZone Resolve(string rawSource, MathCursor.Core.Resolution.ResolutionSidecar sidecar)
        {
            if (sidecar == null || sidecar.IsEmpty) return Resolve(rawSource);
            var globalCtx = new MathCursor.Core.Resolution.GlobalContext();
            globalCtx.AddSignal(new MathCursor.Core.Resolution.Signals.SidecarSignal());
            return Resolve(rawSource, globalCtx, sidecar);
        }

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
            // Décoration des matches avec leur Signature (cf. brief 2026-05-07
            // rule-pin-span-override-refactor). L'AlternativeGenerator émet
            // les matches avec les positions topLatex ; on calcule ici
            // l'OccurrenceIdx via un scan ordonné par Start.
            var decoratedMatches = DecorateMatchesWithSignatures(ambig.AllMatches);
            // Annote AppliedAltIdx pour chaque match selon les préférences
            // accumulées de la session. Sans ça, la popup ne sait pas que
            // l'alt vec (par ex.) est déjà la default et la propose à nouveau
            // → l'user re-pick → double splice (\vec{\vec{AB}}). Cf. bug
            // 2026-05-20. Règle : matches reçoivent AppliedAltIdx s'il y a
            // une préférence pour leur ruleId ; popup filtre cet alt en aval.
            var annotatedMatches = AnnotateAppliedAltIdxFromPreferences(decoratedMatches);
            bool incomplete = ComputeIsIncomplete(rawSource, ambig.TopLatex);
            return new ResolvedZone(
                rawSource: rawSource,
                mutedSource: muted,
                topLatex: ambig.TopLatex,
                spot: ambig.Spot,
                spotStart: ambig.SpotStart,
                spotEnd: ambig.SpotEnd,
                allMatches: annotatedMatches,
                isIncomplete: incomplete);
        }

        /// <summary>
        /// Pour chaque match sans <c>AppliedAltIdx</c>, set-le depuis
        /// <c>_preferences[ruleId]</c> si une pref existe. = source de vérité
        /// pour "ce que le ZoneResolver a effectivement appliqué via pref user".
        /// La popup filtre cet alt + ajoute un revert.
        ///
        /// <para>Le filtrage de l'alt qui correspond au "default rendering"
        /// de l'engine (= alt.Latex == Spot.DefaultLatex pour les rules
        /// type <c>tight-chain-extension</c>) est fait CÔTÉ FILTER, sans
        /// passer par AppliedAltIdx — parce qu'il ne s'agit pas d'un choix
        /// user mais d'une sémantique d'affichage (« ne pas afficher l'alt
        /// qui dupliquerait la formule finale »). Donc pas de revert ajouté
        /// dans ce cas (l'user n'a rien à revert).</para>
        /// </summary>
        private IReadOnlyList<AmbiguityMatch> AnnotateAppliedAltIdxFromPreferences(
            IReadOnlyList<AmbiguityMatch> matches)
        {
            if (matches == null) return new List<AmbiguityMatch>();
            if (matches.Count == 0 || _preferences.Count == 0) return matches;
            var result = new List<AmbiguityMatch>(matches.Count);
            foreach (var m in matches)
            {
                if (m.AppliedAltIdx >= 0) { result.Add(m); continue; }
                if (m.Spot != null
                    && !string.IsNullOrEmpty(m.Spot.RuleId)
                    && m.Spot.Alternatives != null
                    && _preferences.TryGetValue(m.Spot.RuleId, out int prefAlt)
                    && prefAlt >= 0 && prefAlt < m.Spot.Alternatives.Count)
                {
                    result.Add(m.WithAppliedAlt(prefAlt));
                }
                else
                {
                    result.Add(m);
                }
            }
            return result;
        }

        /// <summary>
        /// Enrichit chaque <see cref="AmbiguityMatch"/> avec sa
        /// <see cref="MathCursor.Core.Resolution.MatchSignature"/>.
        /// L'OccurrenceIdx est calculé par scan ordonné gauche-à-droite :
        /// pour chaque (RuleId, DefaultLatex), on incrémente un compteur
        /// au fur et à mesure des rencontres.
        ///
        /// <para>Ex : <c>"AB+CD=AB"</c> avec 3 matches two-uppercase →
        /// 1ʳᵉ AB → occ=0, CD → occ=0, 2ᵉ AB → occ=1.</para>
        /// </summary>
        private static IReadOnlyList<AmbiguityMatch> DecorateMatchesWithSignatures(
            IReadOnlyList<AmbiguityMatch> matches)
        {
            if (matches == null || matches.Count == 0) return matches ?? new List<AmbiguityMatch>();

            // Tri par Start ASC (déjà le cas en pratique mais on s'assure).
            // ToArray pour ne pas muter la liste source.
            var ordered = matches.ToArray();
            System.Array.Sort(ordered, (a, b) => a.Start.CompareTo(b.Start));

            var occCount = new Dictionary<(string, string), int>();
            var result = new List<AmbiguityMatch>(ordered.Length);
            foreach (var m in ordered)
            {
                if (m.Spot == null || string.IsNullOrEmpty(m.Spot.RuleId) || m.Start < 0)
                {
                    result.Add(m); // pas de signature possible (cas defensif)
                    continue;
                }
                var key = (m.Spot.RuleId, m.Spot.DefaultLatex ?? string.Empty);
                if (!occCount.TryGetValue(key, out int idx)) idx = 0;
                var sig = new MathCursor.Core.Resolution.MatchSignature(
                    ruleId: m.Spot.RuleId,
                    defaultLatex: m.Spot.DefaultLatex ?? string.Empty,
                    rawSourcePos: m.Start,   // V1 : utilise la position topLatex
                    occurrenceIdx: idx);
                result.Add(m.WithSignature(sig));
                occCount[key] = idx + 1;
            }
            return result;
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
