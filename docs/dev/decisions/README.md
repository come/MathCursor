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

### 2026-05-21
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
