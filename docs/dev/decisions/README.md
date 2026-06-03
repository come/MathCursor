# Journal de décisions — MathCursor

Une entrée par décision produit ou technique, dans un fichier séparé,
nommé `YYYY-MM-DD-<Kind>-<slug>.md`.

Format et conventions : voir
[2026-04-24-Meta-adr-format.md](2026-04-24-Meta-adr-format.md).

## Kinds
`Feat-` · `Fix-` · `UX-` · `Release-` · `Meta-` · `Test-` · `Limit-`

## Températures
- **[forte]** — structurelle, coûte cher à changer. On n'y revient qu'avec un très bon argument.
- **[molle]** — décision de travail, ouverte à révision si bon argument.
- **[provisoire]** — "on fait comme ça aujourd'hui mais on reverra". À re-examiner activement.

## Statuts
`proposé` · `acté` · `retracté`

## Index chronologique (plus récent en haut)

### 2026-06-03
- `[forte]` Refactor — [Retrait du moteur legacy Lattice (LatticeEngine + Lattice/)](2026-06-03-Refactor-retrait-lattice-legacy-engine.md) — Le legacy P32, gardé en « fallback ~10% », n'était en réalité jamais consulté (v2 ne rend null que sur exception ; `LegacyFallbackCalls==0` verrouillé). Suppression de ~3540 lignes (LatticeEngine + Lattice/ + ILatexEngine + tests), simplification ZoneResolver/SuggestionService/ThisAddIn (ctors sans `engine`, kill-switch retiré). Patterns/ conservé (orthogonal). Sur exception v2 → zone identité dégradée.

### 2026-06-02
- `[forte]` Feat — [Insertion OMML (structure native) au lieu de UnicodeMath + BuildUp](2026-06-02-Feat-omml-insertion.md) — Word re-parsait l'UnicodeMath linéaire → `lim_(x→0) 1/(x+1)` rendu `\frac{lim 1}{x+1}`. On insère désormais l'OMML natif (`LatexToOmml` → `Range.InsertXML`, chirurgical, range locale 1-char) : Word ne re-devine rien. Inclut `\left`/`\right` → `<m:d>` auto-sizé (fin du « fleft(xright) ») et l'échappement caret `MoveRight` (frappe après équation en texte plat). Batterie Word 16/17 (seul fail = bug intra-merge pré-existant). Bug lim/fraction MORT.
- `[forte]` Feat — [Collisions récursives génériques (Variants propagés à toute profondeur)](2026-06-02-Feat-recursive-collisions-variants.md) — Le fork Principe 5 était limité au top-level ; les corps d'anchor (chunks) restaient mono-chaîne. Chaque Item porte désormais ses lectures alternatives (`Variants`), `ResolveChunk` les attache, l'émission les propage → une collision remonte de n'importe quelle profondeur. `somm k=1 2 f(x)/x+1` → collision `…\frac{f(x)}{x+1}` ; imbriqué `1/sum…1/k+1` aussi. Best déterministe, latex en dernière étape.
- `[forte]` Fix — [Précédence des relations (`=`, `<`, `>`…) : niveau proposition, le plus lâche](2026-06-02-Fix-relation-precedence.md) — `f(x) = 1/x+1` donnait `\frac{f(x)=1}{x}+1` (le `=` absorbé en numérateur). Catégorie `Relation` (≠ Expr, non absorbable par une fraction) + `Statement = Expr ∪ Relation` (corps de quantificateur) + phase relations la plus lâche (après l'arithmétique). Top devient `f(x) = \frac{1}{x}+1` + collision `f(x) = \frac{1}{x+1}`.

### 2026-05-30
- `[forte]` Feat — [Principe 5 : multi-chains (collisions par fork d'ordres de composition)](2026-05-30-Feat-beam-search-principe-5.md) — Le Principe 5 (beam search) n'était pas implémenté : moteur mono-chaîne glouton, collisions limitées au tie-break même-span. Ajout d'un fork borné qui explore les ordres de composition primitifs → lectures alternatives structurelles. `1/x+1` → collision `\frac{1}{x+1}` ; `x2`/`AB` préservés (mécanisme unifié). Best déterministe (tops golden intacts) ; latex sérialisé en dernière étape (adapter). 166 golden + 47 adapter verts.
- `[forte]` Fix — [Partial-match des anchors (typing-flow à carrés) enfin réalisé](2026-05-30-Fix-partial-match-anchors.md) — Le Principe 4 de l'ADR moteur V2 était dormant : flag `allow_partial` absent de toutes les règles, ET le chemin partial des anchors récursait (stack overflow). 2 gardes moteur (un Literal ne matche qu'un token brut ; partial seulement après match de l'anchor) + activation `allow_partial` sur les anchors. `1/som` lève désormais `\frac{1}{\sum_{□=□}^{□}□}` et se remplit frappe par frappe. Flake concurrence (cache static Tokenizer) noté pour suivi.

### 2026-05-29
- `[molle]` Feat — [Collision majuscules `AB` (produit / vecteur / paren) en V2](2026-05-29-Feat-collision-uppercase-seq.md) — Phase 1 du portage des collisions legacy. Nouvelle catégorie `UpperSeq` (token tout-majuscules 2-3 lettres) + 3 règles concurrentes même-span (produit top, vecteur, paren) dans `collisions.yml`. Remonte à la popup via le tie-break existant, zéro nouveau mécanisme. `ab`/`X` → aucune collision.
- `[molle]` Test — [Harnais e2e headless moteur V2 → UnicodeMath (sans Word)](2026-05-29-Test-engine-adapter-e2e-headless.md) — Nouveau projet `MathCursor.Engine.Adapter.Tests` qui exerce `texte → EngineZoneSource → ResolvedZone.TopLatex → LatexToUnicodeMath` et asserte l'UnicodeMath final (= ce que Word reçoit). Attrape un éventuel écart de vocabulaire entre le LaTeX émis par V2 (intervalles/ensembles/matrices/setminus) et `LatexToUnicodeMath` AVANT d'ouvrir Word. ~15 cas.

### 2026-05-28
- `[forte]` Refactor — [Moteur rewriting V2 from scratch (architecture cible)](2026-05-28-Refactor-rewriting-engine-v2-clean.md) — Consolidation de toutes les discussions session 26-28 mai. Refonte from-scratch du moteur avec 6 principes : YAML déclaratif, catégories typées + subsumption (Set⊃Interval), scan-keywords + scoping top-down, partial match obligatoire en typing flow, multi-chains beam search + scoring, anchor unifié 3-formes + prefix-match 3-chars. Sprint dédié de 3-4 jours. Tests cibles : `1/sum k 0 n f(k)`, `forall x R U [0;1]`, etc.

### 2026-05-26
- `[forte]` Refactor — [Anchor unifié `KEYWORD args` ≡ `KEYWORD(args)` ≡ `KEYWORD(args,...)`](2026-05-26-Refactor-anchor-callable-unified.md) — Liberté de style utilisateur, 3 formes équivalentes au matching pour les règles avec anchor literal. ~30 LOC matcher.
- `[provisoire]` Refactor — [Règles trig (sin/cos/...) différées](2026-05-26-Refactor-trig-rules-deferred.md) — `sin x+1` format `lim` reporté à après bascule prod (= cohabitation MathEngine/RewriteEngine bloque sinon).

### 2026-05-25
- `[forte]` Refactor — [Chantier 4 Phase B — audit des gaps RewriteEngine vs concepts YAML](2026-05-25-Refactor-chantier4-phaseB-gaps-audit.md) — 15 probes sur les 9 concepts existants : 10 passent tels quels, 4 gaps structurels identifiés (greedy slot, bound précédence, élément optionnel, filler optionnel) + 1 bonus (paren-group → règle YAML). ~95 LOC d'extensions matcher à prévoir pour Phase C. 331/331 engine verts.
- `[forte]` Refactor — [Chantier 4 Phase A — POC RewriteEngine isolé](2026-05-25-Refactor-chantier4-phaseA-rewriting-poc.md) — Nouveau dossier `Rewriting/` parallèle au moteur actuel : Item typé (TokenItem/RewriteItem), Category enum, Pattern+RewriteRule, RewriteMatcher (subsumption Expr), RewriteEngine (loop fixed-point leftmost-longest). 7 règles pilote. Test `interval-union` démontre composition bottom-up. 311/311 engine verts (= 302 préservés + 9 POC). Zéro touche au `MathEngine`.
- `[forte]` Refactor — [Chantier 3 — extraction pre-resolvers (multi-line + prefix-match)](2026-05-25-Refactor-chantier3-preresolvers.md) — `MathEngine.Resolve` simplifié : 2 pre-passes inlinées (≈300 LOC) extraites en `Resolution/MultiLineBlockResolver` + `Resolution/PrefixMatchResolver` via interface `IPreResolver`. Main loop reste lisible d'un coup d'œil. 302/302 engine v2 verts + 393/393 adapter VSTO.
- `[forte]` Refactor — [Chantier 2 — extraction module `Normalization/`](2026-05-25-Refactor-chantier2-normalizer-extract.md) — `Tokenizer` ne mélange plus char→Token avec normalisation de données. Extraction `PrimeNormalizer` (= primes Lagrange Unicode → ASCII `'`) + `CaseToleranceLookup` (= Cos→\cos, OMEGA→\Omega via stratégie d'essais successifs) + façade `Normalizer`. Tokenizer plus mince, helpers testables individuellement. 297/297 engine v2 verts (+31 nouveaux probes).
- `[forte]` Refactor — [Chantier 1 — hardcoded FR (stopwords/delimiters/keywords) → YAML](2026-05-25-Refactor-chantier1-data-driven-fr-keywords.md) — Migration de 4 listes hardcodées en C# (= `ManualTriggerController.Stopwords` 29 mots, `Delimiters` 9 chars, `ZoneRefiner.MathPrefixKeywords` 19 mots, `Tokenizer.multiCharOps` 21 ops) vers `data-v2/locale/fr.yml` (= sections `stopwords:`, `span_delimiters:`, `math_prefix_keywords:`, multi-char ops dérivés de `relations:`). `LocaleVocabulary` expose 3 nouvelles propriétés ; `MathEngine.Vocab` accessor public pour adapter VSTO. Chantier 1/6 du plan de simplification du Resolve. 266/266 engine v2 + 393/393 adapter.

### 2026-05-23
- `[forte]` Feat — [Engine v2 : capacité multi-line (align*/cases) portée depuis le legacy](2026-05-23-Feat-engine-v2-multiline-port.md) — Bug user-reported « le merge interligne vers multiligne est completement cassé, ça merge sur 1 ligne ». Cause : P32 a supprimé le fallback legacy → engine v2 mange les `\n` dans son tokenizer + ne sait pas composer align*/cases → LaTeX 1-ligne. Port direct du legacy : tokenizer émet `Sep("\n")`, nouveau AST `MultiLineBlockNode`, pre-pass `TryBuildMultiLineBlock` dans `MathEngine.Resolve` (détection align/cases), case `RenderMultiLineBlock` dans `LatexEmitter`. Engine v2 devient autonome pour le multi-line align/cases. Légacy `[Obsolete]` reste fallback pour FuncDef + ~10% autres cas.
- `[forte]` Fix — [Engine v2 : leading unary `+`/`-` préservé](2026-05-23-Fix-engine-leading-unary-prefix.md) — Bug user-reported « x2 commit + y2 commit le + est mangé ». Cause root : `StackParser.cs:97-101` skip silencieusement les operators initiaux (= dette POC P11.4-6 « leading unary, pas géré au POC »). Engine v2 promu moteur principal hier → bug devient visible. Fix : nouveau AST `UnaryPrefixNode`, whitelist `{+, -}` dans StackParser, case dans LatexEmitter, produit implicite top-level pour `+ y2` séparé par Sep. Préserve les 211 tests engine v2 actuels + fait passer les 3 cas du probe `PlusY2BugProbeTests`.
- `[forte]` Refactor — [`ZoneSpan` unifié pour popup → commit (vire 10 fields + `TranslateNerToInternal`)](2026-05-23-Refactor-zonespan-popup-commit-coords.md) — Fix root-cause du bug « Soit f gt g » : le path Ctrl+Espace propageait des coords mixtes (paraStart interne + spanStart string) car il n'alimentait pas les fields que le path NER alimentait. Nouveau type `ZoneSpan { ParaAbsStart, StringStart, StringEnd, ParaText, OMaths }` + `TryToInternal(doc)` seul point d'interop. Supprime 5 fields dans `SuggestionService` + 5 dans `ManualTriggerController` + `TranslateNerToInternal` + 2 callbacks à 5 args. Phase 1 du big bang ; Phase 2 (silent-fail `ZoneCleaner`, bug « ff » 1er commit) reportée.
- `[forte]` Feat — [Engine v2 promu moteur principal, legacy `[Obsolete]` (P32)](2026-05-23-Feat-engine-v2-promotion.md) — `MathCursor.Engine` sort du POC : moteur principal pour toute nouvelle saisie. Legacy `MathCursor.Core` (LatticeEngine, Parser, AlternativeGenerator) marqué `[Obsolete]` mais reste fonctionnel comme fallback (~10% cas non couverts). Kill-switch d'urgence : `MATHCURSOR_ENGINE_V2=0`. Supersedes P11. Cleanup legacy différé jusqu'à couverture 100%.
- `[provisoire]` Meta — [YAML collision DSL (brief gardé pour plus tard)](2026-05-23-Meta-yaml-collision-dsl-future.md) — Brief design pour migrer les 7 détecteurs C# (`VecLetter`, `DotVec`, `TripleUpper`, `VectorCoords`, `LetterSupSub`, `SlurpFraction`, `SlurpSupSub`) vers un format YAML `data-v2/collisions/*.yml` avec DSL pattern token-stream + scopes (operand-isolated/per-operand/cross-operand). Reporté à plus tard (« on reverra activement » — user). Reprise quand ≥ 8 détecteurs ou besoin override locale.

### 2026-05-22
- `[forte]` Feat — [Popup IDE-style : composition top-level + 2 candidats + voir plus (P14+P15)](2026-05-22-Feat-popup-ide-style.md) — Refonte `MathEngine.Resolve` en parseur séquentiel top-level (= ancres et atoms compositées via infixes). Permet `lim x 0 f + lim x 1 g` → `\lim_{x \to 0} f+\lim_{x \to 1} g`. Popup WPF affiche max 2 candidats + bouton `+ N autres` qui expand. Reset à chaque nouvelle zone. 83/83 engine + 393/393 adapter verts.
- `[forte]` Feat — [Whitespace = Sep réel + Pratt par tier + {body} greedy-anchor (P13)](2026-05-22-Feat-whitespace-sep-and-pratt-tiers.md) — Brief v5 complet : Sep tokenisé pour whitespace (= boundary explicite, fini l'heuristique `prev.End==start.Start`), Pratt par tier pour {expr}/{bound}/{term} (= s'arrête sur op de tier supérieur), {body} greedy-jusqu'à-ancre avec imbrication (= `lim x 0 sum k 1 n a` OK). Compose `lim f + lim g` proprement. Pas d'espace autour de `+`/`-` (= conv math compact). 81/81 engine verts dont composition + wide-steno.
- `[forte]` Feat — [Slots typés `{var}` `{const}` `{expr}` + quantificateurs (P12)](2026-05-22-Feat-typed-slots.md) — Extension du moteur Engine v2 avec slots typés (var/const/expr) et quantificateurs regexp-like (?, *, +). `{expr}` borné par heuristique token-run O(n) 0-backtracking : un atome après un atome est pris uniquement si collé (= produit implicite `2n`), sinon stop (= `1 n` = 2 slots). Rendu slot par concat brut sauf si `/` (= préserve `n+1` vs `n + 1`). Backward-compat avec `$slot` P11. Référencement positionnel `$1 $2 $N`. Couvre `prod k 1 n+1 f(k)`, `sum k 0 2n+3 g(k)`, `lim x 0 2x+1`, `lim x +oo f(x)`. 80/80 tests verts (= +6 cas user-cases vs P11).
- `[molle]` Feat — [Tutoriel Word `.docx` généré, ouvert en fin d'install](2026-05-22-Feat-tutorial-docx-generated-onboarding.md) — Onboarding natif Word : tableau 2 colonnes (consigne / espace d'essai), 8 sections (Fractions, Exposants/Indices, Vecteurs, Ensembles, Intervalles, Racines/Fonctions, Sommes/Intégrales, Désambig popup), ~30 items. Generated via nouveau projet `tools/TutorialBuilder/` (OpenXML SDK) depuis `data/tutorial-spec.json`. Test xUnit anti-stale vérifie chaque item vs `LatticeEngine.ConvertWithAmbiguity` (= casse au CI si une règle parser diverge des consignes). Installer : `[Files]` → `{userdocs}\MathCursor\` + `[Tasks]/[Run]` checkbox post-install. Règle dure anti-MC0001 : `DocxRenderer.cs` objets OpenXML typés uniquement, zéro XML brut.
- `[forte]` Feat — [POC moteur de détection isolé `MathCursor.Engine` (P11)](2026-05-22-Feat-engine-poc-isolation.md) — Nouveau projet C# séparé, passe-pile déterministe O(n) avec précédence 5-6 tiers + combinateur `liste(X, sep)` + vocab par locale + ranker gaté (≥ 2 candidats). Drop-in derrière `ZoneResolver` via feature flag optionnel — NER + mutations intacts. POC sur 2 concepts (`limites` + `sommes` sum+prod), gate sur parité corpus existant (`LimAmbigBugTests`) avant extension. Cible : < 2 000 LOC engine vs ~2 500 LOC Parser+AlternativeGenerator legacy. Citation user : brief v4 reçu + « on garde le NER bien sur! et les mutation, isole bien ca ». 15 jalons P11.0-P11.15.

### 2026-05-21
- `[forte]` Feat — [Pattern Ranker : dédup + scoring + NMS overlap (P10)](2026-05-21-Feat-pattern-ranker.md) — Nouveau contrat `IPatternRanker` + `DefaultPatternRanker` qui dédup les completions (clé span+preview), score (CompletenessScore + bonus span complet +30 + caret-aware +15), et applique NMS strict sur overlap (jeter perdants). Intégré dans `PatternPipeline` via ctor optionnel. Résout cas `F'(x)=1/x` (= 2× `(x,▢)` parasites) et ouvre la voie à un ranker bayésien/learned plus tard. Boundary fix `IntervalUnion` gardé comme defense in depth.
- `[molle]` Feat — [PrimedDerivative (.cs) + DoubleIntegral (YAML) — P9g + P9h](2026-05-21-Feat-primed-derivative-and-double-integral.md) — Dérivées primées Lagrange (f', f'', f" → conversion guillemet auto en ''), args tight optionnels (f'(x)). Intégrales doubles (iint/Iint/intint/∬, 3 slots) en YAML pur. +33 tests verts. 10 templates pilotes actifs (5 .cs + 6 YAML, dont double_integral auto-discovered).
- `[forte]` Feat — [MatrixTemplate : 3 modes + désambig auto-layout (P9f)](2026-05-21-Feat-matrix-pattern.md) — Matrice avec 3 modes : auto-detect (multi-completion via diviseurs), explicit séparateurs (`,` cols + `;` lignes pour expressions complexes), head paramétré (`mat3x4`). Notation culture-aware (pmatrix FR / bmatrix US) via RenderOptions.MatrixDelim. Heads mat/Mat/matrice/matrix. Mutation canonique normalisée. ArgListPatternBase.TryMatchHead devient virtual. +21 tests verts, 9 templates pilotes au total.
- `[forte]` Feat — [Pattern specs en YAML + auto-discovery (P9e)](2026-05-21-Feat-yaml-pattern-specs.md) — Système data-driven. PatternSpec POCO + YamlDotNet + YamlArgListPatternTemplate générique. 4 templates migrés (Lim/Sum/Integral/Derivative .cs → .yaml). Nouveau pattern Probability créé via YAML pur (zéro .cs). Auto-discovery double : wildcard MSBuild *.yaml + reflection runtime. **Workflow ajout règle : créer 1 .yaml, rebuild, c'est tout** (0 ligne C#). +11 tests verts, 60 tests existants préservés (= comportement YAML identique à C#).
- `[molle]` Feat — [IntegralTemplate + DerivativeTemplate (P9c + P9d)](2026-05-21-Feat-integral-and-derivative-patterns.md) — 2 templates structurels supplémentaires héritant d'ArgListPatternBase. IntegralTemplate (4 slots var/from/to/expression, heads Int/int/intégrale/∫). DerivativeTemplate (2 slots var/expression, heads Derive/derive/dérivée/dérive). DefaultPatternRegistry = 7 templates pilotes complets. +23 tests verts.
- `[molle]` Feat — [SumTemplate : pattern sommation avec 4 slots positionnels (P9b)](2026-05-21-Feat-sum-pattern.md) — Heads Sum/sum/somme/Σ/∑, slots var/from/to/expression (tous requis), conversion infini héritée. ArgListPatternBase enrichi avec 3 helpers protected static (ConcatArgsFrom, ConvertInfinityToken, ConvertInfinityToUnicode) — LimTemplate refactor pour les utiliser. DefaultPatternRegistry = 5 templates pilotes. +19 tests verts. Saisie type "Sum k 0 n k²" → \sum_{k=0}^{n} k².
- `[molle]` Feat — [LimTemplate : pattern limite avec 3 slots positionnels (P9a)](2026-05-21-Feat-lim-pattern.md) — Premier template post-forall héritant d'ArgListPatternBase. Heads Lim/lim, slots var/limit/expression tous requis (vs slot optionnel pour forall), conversions +oo/-oo/∞ → \infty, expression multi-tokens concat depuis arg[2..]. +18 tests verts. Saisie type "Lim x 0 f(x)" → \lim_{x \to 0} f(x). DefaultPatternRegistry inclut maintenant 4 templates pilotes.
- `[forte]` Feat — [Patterns : trailing-space hints + IsIncomplete pour popup persistante (P5R+)](2026-05-21-Feat-pattern-trailing-hints-and-isincomplete.md) — ForallBelongsTemplate ajoute hint `\in \square` quand source termine par whitespace + domain absent. ZoneResolver.IsIncomplete inclut maintenant les patterns partiels (score<100) → la popup ne se ferme PLUS à l'espace tant que pattern actif. SuggestionPopupWindow affiche HintLatex (= avec carrés) et commit utilise PreviewLatex (= sans carrés). +10 tests verts. UX guide-saisie complète : `V` → ∀□∈□ / `V x ` → ∀x∈□ / `V x R` → ∀x∈ℝ
- `[forte]` Refactor — [ForallBelongs : convention "args séparés par espaces" (P5R)](2026-05-21-Refactor-forall-belongs-arglist-convention.md) — Supersedes P5 partiellement. Retrait des 6 openers textuels (app a/appartient/dans/in/(-/∈), nouveau ArgListPatternBase abstrait avec ParseArgs+ClassifyArgs, ForallBelongsTemplate hérite, discrimination var/domain via "dernier arg = ensemble identifié", OpenerAlias.cs supprimé (code mort), tests réécrits 32 verts. Doctrine "rapidité de saisie" : V x R < V x app a R. Préfigure LimTemplate/SumTemplate/IntegralTemplate (P9+) héritant de la même base
- `[forte]` Feat — [Popup affiche les PatternCompletion (rendering définitif P7d)](2026-05-21-Feat-popup-pattern-completion-rendering.md) — sentinel AltIdxPattern local popup, helpers PrependPatternCompletions+MergePrependedMap, branche patterns-only, handler `if (realAltIdx == AltIdxPattern)` set `_resolvedLatex = patternAlt.Latex` + ferme zone ambig + focus on final. Enter commit standard insère l'OMath via CurrentFinalLatex. **Régression UX P6 techniquement restaurée**, validation manuelle P8 via `/build-iss`
- `[provisoire]` Feat — [Popup consomme PatternCompletion (spike pass-through P7c)](2026-05-21-Feat-popup-pattern-completion-spike.md) — SuggestionPopupWindow.Show accepte patternCompletions optionnel, log diag pass-through, pas de rendering modifié (= rendering UX décidé en P7d après observation Word), pas de click handler dédié (zéro risque casser flow actuel), tests adapter 393/393 préservés
- `[molle]` Feat — [SuggestionService injecte le PatternPipeline au ZoneResolver (P7b)](2026-05-21-Feat-suggestion-service-pattern-injection.md) — SuggestionService.cs ctor construit registry+pipeline via DefaultPatternRegistry.BuildBoth() et les passe au ZoneResolver, modification minimale (1 ligne change + 3 ajout), tests adapter 393/393 préservés, build VSTO via VS/ISS
- `[forte]` Feat — [Intégration PatternPipeline dans ZoneResolver (P7a)](2026-05-21-Feat-pattern-pipeline-integration-zone-resolver.md) — ZoneResolver ctor étendu avec PatternPipeline + PatternRegistry optionnels (rétro-compat), ResolvedZone expose PatternCompletions, DefaultPatternRegistry factory `Patterns/`, TopAst nullable, stubs Atom tests nettoyés, 10 tests intégration verts incluant pilote `V x app a [0,1]U[3,4]` au niveau resolver, P7b/c à venir pour finaliser le branchement
- `[forte]` Refactor — [Retrait des scanners legacy V→∀ et R/N/Z/Q/C→ℝ/... (P6)](2026-05-21-Refactor-remove-legacy-quantifier-set-scanners.md) — VAsForallEAsExistsScanner et CanonicalSetLettersScanner retirés du AmbiguityScannerPipeline.Default (8 scanners restants), fichiers scanner supprimés, 3 const Rule* + 2 méthodes statiques + 1 helper retirés de AlternativeGenerator, 20 tests legacy supprimés (couverts par ForallBelongs/Ensemble templates), 2 tests adaptés vers RuleTwoUppercase, régression UX temporaire main jusqu'à P7 assumée
- `[forte]` Feat — [ForallBelongsTemplate : cœur du pilote compositionnel (P5)](2026-05-21-Feat-forall-belongs-pattern.md) — heads V/E/∀/∃ + slot var CSV identifier-list + slot domain optionnel avec 6 openers pondérés (app a, appartient, dans, (-, ∈, in), composition parent↔enfant via PatternRegistry/PatternScanContext, EnsembleTemplate étendu avec head `[` qui délègue à interval-union (P4.5 intégré), IntervalUnion TryMatchHead eager, mutation composite couvrant la zone parente, structures data-ready QuantifierVariant/OpenerAlias prêtes pour YAML, test PILOTE `V x app a [0,1]U[3,4]` → ∀x ∈ [0,1]∪[3,4] vert bout-en-bout, +47 tests verts
- `[molle]` Feat — [IntervalUnionTemplate : pattern récursif avec slots (P4)](2026-05-21-Feat-interval-union-pattern.md) — heads `[`/`(` + boundary gauche pour `(` (function call), slots leftBracket/lo/hi/rightBracket + operator/tail optionnels récursifs, opérateurs U/∪/union/inter/∩, hint `\square` pour slots vides, bornes texte brut (nombre/ident/+oo/-oo/∞), pas de SourceMutation (source déjà parsable), +32 tests verts
- `[molle]` Feat — [EnsembleTemplate : premier vrai IPatternTemplate (P3)](2026-05-21-Feat-ensemble-pattern.md) — heads R/N/Z/Q/C + 1-2 modifiers (* + -), SourceMutation vers bbX, PreviewLatex `\mathbb{X}^{...}`, description Unicode `ℝ*`, leaf template (pas de slot), aligné sur convention `PreprocessCanonicalSetModifiers` + `CanonicalSetLettersScanner` legacy (retrait P6), +33 tests verts
- `[molle]` Refactor — [Squelette Patterns/ : contrats IPatternTemplate + pipeline + registry (P2)](2026-05-21-Refactor-pattern-pipeline-skeleton.md) — 9 fichiers contrats + orchestration dans `core-csharp/src/MathCursor.Core/Patterns/`, `abstract class + sealed subclasses` cohérent avec l'AST, `EmptySlot.Instance` singleton, +16 tests sanity verts, aucun template inscrit, aucun comportement user-visible
- `[molle]` Refactor — [Caret-aware ZoneResolver via CaretLocator (P1 du plan Patterns)](2026-05-21-Refactor-caret-aware-zone-resolver.md) — nouveau service `CaretLocator.FindDeepestMatchAtCaret` + paramètre optionnel `caretOffset` sur les 3 overloads `ZoneResolver.Resolve`, default null = legacy rightmost préservé, +24 tests verts, 393/393 adapter inchangé
- `[forte]` Meta — [Séparation Pattern Template vs Ambig Closed + désambig caret-aware](2026-05-21-Meta-pattern-templates-vs-ambig-closed.md) — `IAmbiguityScanner` pour les ambig fermées (AB/tight-chain) + nouveau `IPatternTemplate` compositionnel (V/Lim/Sum) avec slots optionnels et sous-patterns isolables, pilote `forall-belongs` + `ensemble` + `interval-union` en C# pur, retrait du scanner V legacy, plan en 9 étapes P1-P9+

### 2026-05-19
- `[forte]` Feat — [Anchor CC pattern : CC adjacent à l'OMath au lieu de wrap](2026-05-19-Feat-anchor-cc-pattern.md) — fix display math propre sans `<w:br/>` + élimine sticky auto-grow + caret naturel post-commit, CC tiny sur ZWSP hidden avant l'OMath, lookup backward probe O(1) (1-3 positions)

### 2026-05-18
- `[molle]` Feat — [Intra-OMaths merger : revival LaTeX-preserving, voisin gauche uniquement](2026-05-18-Feat-intra-omaths-merger-revival.md) — fix `F(x)` + `=1` → 1 OMath `F(x)=1` (pas 2), `mergedLatex = leftLatex + newLatex` lu depuis `cc.Tag.Latex` (pas de re-rendu), marker guard (`=`/`<=>`/`=>`/`{`), skip si hash drift OMML détecté

### 2026-05-13
- `[forte]` Fix — [Garde strip list_mode : ¶ avec OMath ne peut JAMAIS être effacé](2026-05-13-Fix-list-mode-strip-guard-omath.md) — fix bug perte formule après cross-merge + Escape (log user 13:29), nouvelle classe pure `ListModeStripGuard` testée TDD, reset complet list_mode si inject échoue
- `[molle]` Fix — [Alignment `m:jc=left` uniforme post-insert (tous chemins)](2026-05-13-Fix-omath-alignment-uniform-post-insert.md) — remonte l'appel `EnforceOMathParagraphAlignment` à `InsertOMathAt` (1 call site uniforme), supprime duplication + flag `_wasXmlTransplant`, élimine OMath centré sur fast_path/splice
- `[forte]` Refactor — [S1 : `ScanUppercaseSequences` source-based + Mutations vec/paren](2026-05-13-Refactor-s1-twoupper-source-mutations.md) — fixe immédiatement le bug 06-05 single-line (pref vec sur AB rend `\vec{AB}`), +4 tests verts, 1 cross-merge multi-ligne reporté en S2
- `[molle]` Fix — [WarmUp ghost doc déclenché par le 1ᵉʳ `WindowActivate`, plus inline dans `Install()`](2026-05-13-Fix-warmup-event-driven.md) — élimine la COMException `Selection.get returned null` au boot, handler one-shot auto-désabonnant (pas de timer ni de field flag), respect doctrine events natifs
- `[forte]` Refactor — [Scanners d'ambiguïté en Strategy + Pipeline (`IAmbiguityScanner`)](2026-05-13-Refactor-ambiguity-scanners-strategy.md) — S0 du refacto source-mut, 10 scanners statiques extraits en classes indépendantes, alignement doctrine `IZoneMerger`/`ICommitStage`/`IContextSignal`
- `[forte]` Refactor — [Source-mutation pure pour les pins sidecar (élimine MC0006 du Core de prod)](2026-05-13-Refactor-source-mutation-pins-sidecar.md) — unifie les 2 chemins d'application des pins sur le modèle ApplyPreferences (source-mut), splice latex devient fallback résiduel (bracket uniquement)
- `[molle]` Meta — [Règles MC0006 (splice LaTeX) + MC0009 (SuppressMessage sans ADR)](2026-05-13-Meta-mc0006-mc0009.md) — Phase 2.5 du harnais, MC0006 capture l'anti-pattern du bug double-wrap (4 hits réels), MC0009 verrou anti-suppression (0 hit), 16 nouveaux tests verts
- `[molle]` Fix — [Ghost doc invisible dès création (`Documents.Add(Visible:false)`) + pre-warming au boot](2026-05-13-Fix-ghost-doc-invisible.md) — élimine le flash visuel au 1ᵉʳ commit math, ~50ms plus rapide en bonus
- `[forte]` Refactor — [Visitor sur AST (`IAstVisitor<TResult>` + 18 Accept overrides + `LatexRenderingVisitor`)](2026-05-13-Refactor-ast-visitor.md) — étape 4, élimine le switch exhaustif de `LatexRenderer`, API publique inchangée, 0 régression
- `[molle]` Meta — [Harnais Phase 0+1 : projet analyzer + règle MC0001](2026-05-13-Meta-harness-phase-0-1-mc0001.md) — Roslyn analyzer + MC0001 (Regex sur XML), branché sur Core, severity warning non bloquante, 11/11 tests verts
- `[molle]` Meta — [Projet `MathCursor.Core.Abstractions` (5 axes d'extensibilité)](2026-05-13-Meta-extensibility-axes-abstractions.md) — étape 2 du plan refacto, ajout pur, 0 régression test

### 2026-05-12
- `[forte]` Refactor — [Merger pur + insert atomique (élimine legacy path et pré-suppression)](2026-05-12-Refactor-pure-merger-atomic-insert.md)
- `[forte]` Perf — [Stack 3 couches sur le commit pipeline (gros doc, ~290ms → ~30-90ms)](2026-05-12-Perf-commit-pipeline-three-stage-stack.md)

### 2026-05-11
- `[molle]` Feat — [Notation d'angle au clavier : `^A`/`^ABC` et `angle(...)`](2026-05-11-Feat-angle-notation-caret-and-keyword.md)
- `[molle]` Feat — [Duo Convertir/Colonnes dans TabHome + onglet "MathCursor" dédié pour le reste](2026-05-11-Feat-ribbon-home-duo-plus-dedicated-tab.md)
- `[forte]` Fix — [Commit groupé dans un seul `UndoRecord` Word](2026-05-11-Fix-commit-grouped-in-single-undo-record.md)
- `[forte]` Refactor — [Lecture du paragraphe courant via `Range.WordOpenXML` (pas `Range.Text`)](2026-05-11-Refactor-paragraph-reader-via-xml.md)
- `[forte]` Fix — [Splice XML navigué par parent/siblings et matching par contenu (durcit pour tableaux + tout conteneur)](2026-05-11-Fix-omath-splice-content-based-navigation.md)

### 2026-05-07
- `[forte]` Fix — [Insertion d'OMath par splice XML du `<w:p>` existant (pas reconstruction depuis Range.Text)](2026-05-07-Fix-insert-via-paragraph-xml-splice.md)

### 2026-05-06
- `[forte]` Meta — [Décomposition L4 : Pipeline déclaratif + Session avec cycle de vie](2026-05-06-Meta-l4-pipeline-and-session.md)
- `[forte]` Meta — [Pipeline de mergers L4 via interface `IZoneMerger` (no if-pile)](2026-05-06-Meta-zone-merger-pipeline.md)
- `[forte]` Feat — [Sidecar de résolutions + doctrine d'architecture en couches](2026-05-06-Feat-resolution-sidecar-and-layers.md)
- `[molle]` Feat — [Ruban revient dans TabHome + pane pivote vers galerie d'exemples concrets multi-syntaxes](2026-05-06-Feat-ribbon-pane-examples-pivot.md) (Supersedes ribbon-refactor-cheatsheet)

### 2026-05-05
- `[molle]` Feat — [Refonte du ruban : ajout d'un panneau Cheatsheet](2026-05-05-Feat-ribbon-refactor-cheatsheet.md) `retracté`
- `[forte]` Limit — [OMath display recentre après fusion ¶ via Backspace (limite Word)](2026-05-05-Limit-omath-jc-stripped-on-fusion.md)
- `[molle]` Feat — [Mode liste cases `{` Phase 2 (multi-ligne + list-mode visible)](2026-05-05-Feat-cases-multiline-phase2.md)
- `[molle]` Feat — [Mode liste multi-ligne visible (auto-injection du marker en texte)](2026-05-05-Feat-multiline-list-mode-visible.md) (Supersedes multiline-list-mode du même jour)
- `[molle]` Feat — [Mode liste invisible pour multi-ligne (préfixage auto du marker)](2026-05-05-Feat-multiline-list-mode.md) `retracté`

### 2026-05-04
- `[molle]` Meta — [Refactor insertion OMath via build isolé + transplant XML (anti-absorption BuildUp)](2026-05-04-Refactor-omath-via-xml-transplant.md)
- `[molle]` Feat — [Édition multi-ligne via cascade cross-merge (2 modes)](2026-05-04-Feat-multiline-edit-cascade-merge.md)
- `[molle]` Meta — [Refactor du pipeline cross-merge (4 phases séquentielles)](2026-05-04-Meta-cross-merge-pipeline-refactor.md)

### 2026-05-01
- `[molle]` Feat — [Backoffice admin (reports + stats) en ligne sur Cloudflare avec Basic Auth](2026-05-01-Feat-admin-backoffice-cloudflare.md)

### 2026-04-30
- `[molle]` Fix — [Fonction trigo + Number tight + Group avale la suite (`cos2(x)+1`)](2026-04-30-Fix-trig-func-power-tight-arg.md)
- `[molle]` Feat — [Le point `.` comme opérateur de multiplication (rendu `\cdot`)](2026-04-30-Feat-dot-as-multiplier.md)
- `[molle]` Feat — [Multiplication explicite `*` rendue selon culture (`×` ou `·`)](2026-04-30-Feat-explicit-mult-times-vs-cdot.md)
- `[molle]` Feat — [Formulaire "Signaler une erreur" pré-rempli + backend Cloudflare](2026-04-30-Feat-feedback-form-cloudflare-backend.md) (Supersedes partiellement feedback-bundle-whatsapp du 23-04)
- `[molle]` Feat — [Associativité de `*` pilotée par sa tightness](2026-04-30-Feat-asterisk-tightness-associativity.md)
- `[molle]` Feat — [Juxtaposition tight = groupement, ops explicites = PEMDAS (avec alt désambig)](2026-04-30-Feat-tight-implicit-mult-grouping.md) (Supersedes revert-tight-as-grouping)
- `[molle]` Feat — [Séparateur `;` pour coordonnées en notation française](2026-04-30-Feat-french-semicolon-coordinates.md)
- `[molle]` Fix — [Refactor `LatexToUnicodeMath` en parser → AST → émetteur (anti-absorption Word OMath)](2026-04-30-Fix-latex-to-unicodemath-refactor.md)
- `[molle]` Feat — [Précédence math standard pour `/` collé (revert tight-as-grouping)](2026-04-30-Feat-revert-tight-as-grouping.md) (Supersedes tight-as-grouping du 29-04) `retracté`

### 2026-04-29
- `[molle]` Test — [Audit follow-up : combler les angles morts de tests (cleanup + désambig + corpus patho + adapter tests + NER inference)](2026-04-29-Test-audit-followup.md)
- `[molle]` Feat — [Vecteur/point + coordonnées au clavier (`u(1, 2)` / `u (1 2)` / `A(1, 2)`)](2026-04-29-Feat-vector-coordinates-shorthand.md)
- `[molle]` Feat — [Fusionner OMath adjacents lors d'une conversion](2026-04-29-Feat-merge-adjacent-omaths.md)
- `[molle]` Feat — [Détection `=>` / `<=>` / `<==` et conversion en flèches math](2026-04-29-Feat-implication-equivalence-arrows.md)
- `[molle]` Feat — [Extension itérative de la zone via Ctrl+Espace répété](2026-04-29-Feat-iterative-zone-expansion-ctrl-space.md)
- `[molle]` Feat — [Décomposition modulaire de `forall`/`exists` (∀ + var + ∈ + set)](2026-04-29-Feat-forall-modular-decomposition.md) (Supersedes scope forall du 28-04)
- `[molle]` Feat — [Définition de fonction au clavier `f:x->expr` → `f : x ↦ expr`](2026-04-29-Feat-function-definition.md)
- `[molle]` Feat — [Ensembles canoniques R/N/Z/Q/C avec modificateurs `*`/`+`/`-`](2026-04-29-Feat-canonical-sets.md)
- `[molle]` Feat — [Juxtaposition tight = groupement implicite pour `/`, `^`, `_`](2026-04-29-Feat-tight-as-grouping.md) `retracté`
- `[molle]` Feat — [Composition d'intervalles : union (`U`/`union`) et intersection (`inter`)](2026-04-29-Feat-interval-union-intersection.md)

### 2026-04-28
- `[molle]` Feat — [Notation intervalle française au clavier (`[a,b[`, `]a,b]`...)](2026-04-28-Feat-interval-notation.md)
- `[forte]` Meta — [Refactor : ZoneResolver, point d'entrée unique pour la résolution de zone](2026-04-28-Meta-zone-resolver-refactor.md)
- `[forte]` Feat — [Quantificateur ∀/∃ via scope clavier + désambiguïsation par mutation source](2026-04-28-Feat-forall-scope-source-mutation.md) `retracté`

### 2026-04-27
- `[molle]` Feat — [Corpus NER v4 (keywords math en début de zone)](2026-04-27-Feat-ner-corpus-v4-keywords.md)

### 2026-04-24
- `[molle]` Feat — [Corpus NER v3 (fixtures projet + `²` + anti-FP) & challenge modèle](2026-04-24-Feat-ner-corpus-v3-and-model-challenge.md)
- `[molle]` UX — [Popup silencieuse jusqu'à la première interaction utilisateur](2026-04-24-UX-popup-silent-until-interaction.md)
- `[molle]` Feat — [Revert popup vers WpfMath + substitutions ciblées](2026-04-24-Feat-popup-revert-wpfmath.md) (Supersedes WebView2)
- `[forte]` Feat — [Rendu popup via WebView2 + KaTeX (remplace WPF-Math)](2026-04-24-Feat-popup-webview-katex.md) `retracté`
- `[molle]` Feat — [Caractère `²` (AZERTY) traité comme `^2`](2026-04-24-Feat-superscript-two-keyboard.md)
- `[molle]` Feat — [`exp(x)` rendu comme `e^x`](2026-04-24-Feat-exp-as-power-e.md)
- `[molle]` Fix — [`\widehat` non converti + tests de conformance de rendu (OMath & WPF)](2026-04-24-Fix-widehat-omath-conformance.md)
- `[molle]` Fix — [Cert importé uniquement dans TrustedPublisher (pas Root)](2026-04-24-Fix-cert-trustedpublisher-only.md)
- `[molle]` UX — [L'installer importe le certificat lui-même (plus de PowerShell)](2026-04-24-UX-installer-imports-cert.md)
- `[molle]` Feat — [Déploiement Cloudflare Pages + R2 + Analytics Engine](2026-04-24-Feat-cloudflare-deployment.md)
- `[forte]` Meta — [Process de décision rappelé dans CLAUDE.md](2026-04-24-Meta-process-decision-claude-md.md)
- `[molle]` Meta — [Format des ADR + température de décision](2026-04-24-Meta-adr-format.md)
- `[molle]` Feat — [Feedback in-popup "Signaler une erreur"](2026-04-24-Feat-feedback-in-popup.md) (scaffold HTTP = provisoire dedans)
- Release — [0.3.0](2026-04-24-Release-0.3.0.md)

### 2026-04-23
- `[molle]` Feat — [Patterns sum/product/integral séparateur espace + angle ABC](2026-04-23-Feat-sum-angle-patterns.md)
- `[molle]` Fix — [Span Ctrl+Espace respecte brackets/parens](2026-04-23-Fix-span-respects-brackets.md)
- `[molle]` Feat — [Matching préfixe (propositions partielles)](2026-04-23-Feat-partial-matching.md)
- Release — [0.2.0](2026-04-23-Release-0.2.0.md)
- `[molle]` UX — [Alignement OMath respecte le paragraphe parent](2026-04-23-UX-alignment-respect-paragraph.md)
- `[molle]` Feat — [Trigger explicite Ctrl+Espace](2026-04-23-Feat-trigger-ctrl-space.md)
- `[molle]` Feat — [Feedback via bundle zip + groupe WhatsApp](2026-04-23-Feat-feedback-bundle-whatsapp.md)
- `[molle]` Fix — [Rendu LaTeX : fallback sur `HasError`](2026-04-23-Fix-latex-render-fallback.md)
- `[molle]` Fix — [Position popup via GetGUIThreadInfo](2026-04-23-Fix-popup-position-guithreadinfo.md)
