> **⚠️ Document hérité de DocMath (gelé au 2026-05-21).** Sur la branche
> `beta-clean`, l'état d'avancement vit dans [`PLAN.md`](../../../PLAN.md)
> (consolidation beta : phases 0-5). Ce fichier est conservé pour référence
> historique des chantiers DocMath.

# ROADMAP — État des chantiers MathCursor

**Dernière mise à jour** : 2026-05-21
**Audience** : moi-future / Claude Code re-démarré / contributeur tiers

Document unique d'orientation. Lecture en 2 minutes → état complet des
plans en cours. **Mis à jour à chaque fin de sous-livraison.**

---

## Comment se mettre dans le bain (nouvel agent)

1. **CLAUDE.md** racine — contexte produit + stack + règles dev + process décision
2. **Ce fichier ROADMAP.md** — état des chantiers en cours
3. **`docs/dev/architecture/cartography.md`** — image archi détaillée par fichier, dette identifiée
4. **`docs/dev/decisions/README.md`** — index chrono de tous les ADRs (≥ 50 décisions)
5. **`git log --oneline -30`** — contexte des derniers commits
6. **Skills disponibles** :
   - `/mathcursor-plan <sujet>` — force le bon process de planification (couche + tradeoff qualité + règles MC)
   - `/mathcursor-adr` — génère ADR au format projet
   - `/deploy-prod` — pipeline release
   - `/build-iss` — build installer

---

## Branches et état git

| Branche | État | Note |
|---|---|---|
| `main` | référence stable | derniers releases |
| `refactor/big-bang-clean-arch` | **active** | refacto DDD + extensibilité en cours (toutes les sessions 2026-05-13 ici) |
| `multiline-systems` | obsolète | mergée |

---

## Chantier 1 — Refacto archi extensibilité (5 axes)

**Brief source** : `MATHCURSOR_ARCHITECTURE_EXTENSIBILITE.md` (téléchargé 2026-05-13, archivé hors repo).
**ADR-clé** : [`2026-05-13-Meta-extensibility-axes-abstractions.md`](../decisions/2026-05-13-Meta-extensibility-axes-abstractions.md)

### Étapes du brief

- [x] **Étape 1** — Cartographie (`docs/dev/architecture/cartography.md`)
- [x] **Étape 2** — Projet `MathCursor.Core.Abstractions/` avec 6 contrats (`IConstructStrategy`, `IDomainParser`, `ILocaleLexer`, `ILocaleNER`, `IOutputSerializer<TFormat>`, `ParseContext`)
- [ ] **Étape 3** — Implémentation des contrats par types existants (`LatticeEngine` → `IDomainParser`, etc.) — **OPTIONNEL**, déclencher si extension de domaine ou format effective
- [x] **Étape 4** — Visitor sur AST (`IAstVisitor<TResult>` + 18 `Accept` overrides + `LatexRenderingVisitor`). ADR [`Refactor-ast-visitor`](../decisions/2026-05-13-Refactor-ast-visitor.md)
- [ ] **Étape 5** — Sortir chaînes FR du Core → `locales/fr/keywords.yaml` + `FrenchLocaleLexer`. **Active MC0002** à ce stade
- [ ] **Étape 6** — `DomainRouter` (placeholder math-only)
- [ ] **Étape 7** — `ShortcutResolver` (overlay YAML user `~/.mathcursor/shortcuts.yaml`)
- [ ] **Étape 8** — Test d'intégration extensibilité (EmptySetStrategy + sérialiseur Unicode factice + raccourci user)

### Refacto source-mutation des pins sidecar (sous-chantier)

**ADR-clé** : [`2026-05-13-Refactor-source-mutation-pins-sidecar.md`](../decisions/2026-05-13-Refactor-source-mutation-pins-sidecar.md)
**Objectif** : éliminer le hit MC0006 sur `ZoneResolver:205` (splice latex anti-pattern racine).

- [x] **S0** — Extraction Strategy `IAmbiguityScanner` + Pipeline. ADR [`Refactor-ambiguity-scanners-strategy`](../decisions/2026-05-13-Refactor-ambiguity-scanners-strategy.md). Commit `e5b8ee7`
- [x] **S1** — Mutations vec/paren sur `ScanUppercaseSequences` source-based. ADR [`Refactor-s1-twoupper-source-mutations`](../decisions/2026-05-13-Refactor-s1-twoupper-source-mutations.md). Fixe bug 06-05 single-line
- [ ] **S2** — `ApplyAllMutations` étendu (cross-merge multi-ligne, pins sidecar, offset tracking Pin v2)
- [ ] **S3** — Élagage splice loop + retirer `MC0006` du `WarningsNotAsErrors` Core.csproj + bench perf

---

## Chantier 2 — Harnais d'analyse statique

**Brief source** : `MATHCURSOR_HARNESS_BRIEF.md` (téléchargé 2026-05-13).

### Phases

- [x] **Phase 0+1** — Projet `analyzers/MathCursor.Analyzers/` + règle MC0001 (Regex sur XML structuré). ADR [`Meta-harness-phase-0-1-mc0001`](../decisions/2026-05-13-Meta-harness-phase-0-1-mc0001.md). Commit `fc3e1da`
- [x] **Phase 2** — `Directory.Build.props` racine généralise l'analyzer à tous les projets. Commit `2845fdd`
- [x] **Phase 2.5** — Règles MC0006 (splice LaTeX anti-pattern racine) + MC0009 (SuppressMessage sans ADR). ADR [`Meta-mc0006-mc0009`](../decisions/2026-05-13-Meta-mc0006-mc0009.md). Commit `b3d3c79`
- [x] **Phase 3** — Skills `/mathcursor-plan` + `/mathcursor-adr` versionnées via `.claude/skills/`. Commit `70a45e6`
- [ ] **Phase 4** — Règles MC additionnelles : MC0002 (VSTO leak in Core, post-étape 5 axes) + MC0003 (chaîne `if/else` sur discriminant de type) + MC0005 (god method) — voir brief pour la liste complète
- [ ] **Phase 5** — Diff summarizer Python (SARIF + filtres anti-bruit + rapport markdown classifié + pre-commit hook)
- [ ] **Phase 6** — Sources Tier 2 (T2-NEWTYPE, T2-NEWDEP, T2-COMPLEXITY-DELTA, T2-PUBLIC-SURFACE, T2-MAGIC-LITERAL, T2-NOVEL-PATTERN)
- [ ] **Phase 7** — Sources Tier 3 (T3-COVERAGE-GAP, T3-DOC-DENSITY, T3-NAMING-DEVIANCE)
- [ ] **Phase 8** — **Agents formels** : `architecte`, `developpeur`, `auditeur`, `harness-maintainer` avec configs Claude Code dans `.claude/agents/` (versionné via `.gitignore` exception déjà en place — cf. brief §"Couche 3")
- [ ] **Phase 9** — Boucle feedback (verdicts user + recalibration auto des poids des signaux)

### Règles MC actives

| ID | Smell | Severity | Hits actuels |
|---|---|---|---|
| MC0001 | Regex sur XML/OMath/MathML | warning | ~~6 Core (`LatexToUnicodeMath`)~~ (code mort supprimé, ADR 2026-06-22) + 1 test + 10 Adapter (`WpfMathAdapter`, `MixedLatexRenderer`, `OMathParaJcPatcher`) |
| MC0006 | Splice LaTeX sur texte rendu | warning | 2 Core (`ZoneResolver:205` — éliminés par S2+S3) + 2 test (légitimes, ADR SuppressMessage à venir) |
| MC0009 | SuppressMessage sans ADR | warning | 0 |

`WarningsNotAsErrors` côté `MathCursor.Core.csproj` : `MC0001;MC0006` (à retirer au fil du nettoyage).

---

## Chantier 5 — Pipeline d'insertion simplifié (2026-05-14)

**Insight** : la recette minimale `SetRange + TypeText + OMaths.Add + BuildUp + Justification` (validée par bouton debug) couvre 99% des cas d'insertion proprement. Tout le reste (ghost, splice XML, atomic insert, patcher Regex, CaretPositioner) est de l'archi inutile.

### Phases NeighborFinder (extraction méthode UNIQUE de probe voisins)

- [x] **Phase 1+2** — Extraire `NeighborFinder` d'`IntraOMathsMerger`, méthode unique de probe gauche/droite (intra-merge). 0 changement de comportement.
- [ ] **Phase 3** — Propager les `Neighbor` trouvés dans `CommitContext` (champ `AbsorbedNeighbors`)
- [ ] **Phase 4** — `InsertOMathAt` utilise les `Neighbor` pour étendre `absStart/absEnd` correctement (fix bug `F(x) commit + =1 commit → F(x)F(x)=1`). Cleanup bookmarks via `Handle`.
- [ ] **Phase 5** — Ajouter `NeighborFinder.FindAbove(absStart)` pour le ¶ précédent. `MarkerChainCascadeMerger` utilise la même API. **Une seule méthode** de recherche pour intra + cross-merge.

### Cas restants à traiter

- [ ] **Cas « collé à un OMath »** : sera résolu par Phases 3+4 (extension absStart via Neighbor.RangeStart).
- [ ] **Cas multi-ligne (cross-merge align*, cases)** : Phase 5 + adaptation `MarkerChainCascadeMerger`. La recette `OMaths.Add + BuildUp` native fonctionne ?  À tester. Sinon garder `AtomicRangeInserter` + `OMathStagingService` uniquement pour ce cas spécifique.

### Cleanup post-refacto pipeline (à faire avant fin de session 2026-05-14)

- [x] `InsertOMathAt` réécrit en recette minimale (commit `d5962d0`)
- [x] `UndoRecordScope` restauré (BuildUp est une opération undo séparée, le wrapper groupe)
- [x] `om.Justification` setter direct (= retour de `OMaths.Add` qui donne la Range)
- [ ] **Supprimer code mort** (à faire) :
  - `Host/OMathStagingService.cs` (~200 lignes)
  - `Host/Inserters/` (4 fichiers ~400 lignes : PureFastPath, InlineSplice, AtomicRange, WordOMathFinder, InsertContext)
  - `Host/InlineOMathSplicer.cs` (~250 lignes)
  - `Host/OMathParaJcPatcher.cs` (~140 lignes, élimine 5 MC0001 Regex)
  - `Host/Caret/CaretPositioner.cs` + `CaretAfterOMathPolicy.cs`
  - `Host/CaretPositionCalculator.cs`
  - `Host/OMathXmlCache.cs` + `ParaXmlPrefetcher.cs` (si plus utilisés)
  - `BuildInsertContext` + `TryInsertStrategies` dans `SuggestionService`
  - `EnforceOMathParagraphAlignment` + helpers dans `PostCommitLayoutFinalizer`
  - Tests associés (`CaretPositionCalculatorTests`, `OMathParaJcPatcherTests`, `InlineOMathSplicerTests`, `InsertTransplantIntegrationTests`, `Perf/InsertPipelinePerfTests`)
- [ ] `WarmUp` event-driven retiré (plus de ghost à pre-warm)
- [ ] ADR de bilan : pourquoi la recette minimale a remplacé le pipeline complexe (= snapshot des décisions)

---

## Chantier 6 — Pattern Templates (axe A — constructions structurées)

**ADR-clé** : [`2026-05-21-Meta-pattern-templates-vs-ambig-closed.md`](../decisions/2026-05-21-Meta-pattern-templates-vs-ambig-closed.md)
**Objectif** : séparer 2 concepts mélangés aujourd'hui dans `IAmbiguityScanner` — les **ambig closed** (AB/tight-chain/decimal) restent, les **patterns structurés** (V/Lim/Sum/∫/dérivée) passent dans un nouveau contrat `IPatternTemplate` compositionnel + caret-aware.

### Étapes

- [x] **P0** — Attendre commit stable du WIP popup en cours (`PopupAltFilter` / `BuildSidecar` / `RemovePreference`) — commits `817c4d3` / `8477602` / `538f61e`
- [x] **P1** — Caret-aware `ZoneResolver` : ajout paramètre `caretOffset` + service `CaretLocator.FindDeepestMatchAtCaret`. ADR [`Refactor-caret-aware-zone-resolver`](../decisions/2026-05-21-Refactor-caret-aware-zone-resolver.md). +24 tests verts (13 `CaretLocator` + 11 `CaretAwareZoneResolver`), 393/393 adapter inchangé.
- [x] **P2** — Squelette `core-csharp/src/MathCursor.Core/Patterns/` : 9 fichiers contrats (`IPatternTemplate`, `PatternScanContext`, `SlotType`+4 sealed, `SlotSpec`, `SlotValue`+3 sealed, `PatternMatch`, `PatternCompletion`, `PatternPipeline`, `PatternRegistry`). ADR [`Refactor-pattern-pipeline-skeleton`](../decisions/2026-05-21-Refactor-pattern-pipeline-skeleton.md). +16 tests sanity verts, aucun template inscrit.
- [x] **P3** — `EnsembleTemplate` : heads R/N/Z/Q/C + 1-2 modifiers (* + -), SourceMutation vers bbX, leaf template. ADR [`Feat-ensemble-pattern`](../decisions/2026-05-21-Feat-ensemble-pattern.md). +33 tests verts. Délégation à `IntervalUnionTemplate` pour `[` reportée à P4.
- [x] **P4** — `IntervalUnionTemplate` : heads `[`/`(` avec boundary pour `(`, slots leftBracket/lo/hi/rightBracket + operator/tail récursifs, opérateurs U/∪/union/inter/∩, hint `\square` pour slots vides, pas de SourceMutation. ADR [`Feat-interval-union-pattern`](../decisions/2026-05-21-Feat-interval-union-pattern.md). +32 tests verts. P4.5 (head `[` dans EnsembleTemplate) reporté à P5 pour intégration parent↔enfant complète.
- [x] **P5** — `ForallBelongsTemplate` v1 avec openers (commit `417e373`)
- [x] **P5R** — Refacto convention args espace (commit `451d94d`)
- [x] **P5R+** — Trailing-space hints + IsIncomplete : la popup reste ouverte à l'espace tant que pattern actif (= V, V x , V x app a sans domain), `\square` affichés pour args attendus dans HintLatex (popup), PreviewLatex propre pour commit. ADR [`Feat-pattern-trailing-hints-and-isincomplete`](../decisions/2026-05-21-Feat-pattern-trailing-hints-and-isincomplete.md). +10 tests verts.
- [x] **P6** — Retrait `VAsForallEAsExistsScanner` + `CanonicalSetLettersScanner` du `AmbiguityScannerPipeline.Default` (= 8 scanners restants) + suppression fichiers + 3 const + 2 méthodes statiques + 20 tests legacy supprimés. ADR [`Refactor-remove-legacy-quantifier-set-scanners`](../decisions/2026-05-21-Refactor-remove-legacy-quantifier-set-scanners.md). **Régression UX temporaire main jusqu'à P7 assumée** (l'utilisateur Word ne verra plus la popup pour V/E/R/N/Z/Q/C — restauré par P7).
- [→] **P7** — Branchement Patterns ↔ ZoneResolver ↔ Popup. Décomposé en 4 sous-étapes commits séparés.
  - [x] **P7a** — Core : ZoneResolver invoque PatternPipeline + expose PatternCompletion[] dans ResolvedZone, DefaultPatternRegistry factory, TopAst nullable. ADR [`Feat-pattern-pipeline-integration-zone-resolver`](../decisions/2026-05-21-Feat-pattern-pipeline-integration-zone-resolver.md). +10 tests verts.
  - [x] **P7b** — Adapter VSTO : SuggestionService.cs construit registry+pipeline via DefaultPatternRegistry.BuildBoth() et les injecte au ZoneResolver. ADR [`Feat-suggestion-service-pattern-injection`](../decisions/2026-05-21-Feat-suggestion-service-pattern-injection.md).
  - [x] **P7c** — Popup spike pass-through : SuggestionPopupWindow.Show accepte patternCompletions optionnel + log diag, pas de rendering modifié (décidé en P7d après observation Word). ADR [`Feat-popup-pattern-completion-spike`](../decisions/2026-05-21-Feat-popup-pattern-completion-spike.md) (provisoire).
  - [x] **P7d** — Popup rendering définitif : sentinel AltIdxPattern + helpers PrependPatternCompletions/MergePrependedMap + handler dans ResolveCurrentAltIfFocused. ADR [`Feat-popup-pattern-completion-rendering`](../decisions/2026-05-21-Feat-popup-pattern-completion-rendering.md). **Régression UX P6 techniquement restaurée**. Validation manuelle Word reportée en P8 via `/build-iss`.
- [ ] **P8** — Test bout-en-bout dans Word : `V x app a [0,1]U[3,4]` → `\forall x \in [0,1]\cup[3,4]` avec carrés pendant la saisie.
- [ ] **P9+** — `LimTemplate`, `SumTemplate`, `IntegralTemplate`, `DerivativeTemplate` + migration YAML des patterns triviaux (`EnsembleTemplate` candidat éligible).

### Couplage avec les autres chantiers

- **Chantier 1 (source-mut pins S2/S3)** : zones de code différentes (`ZoneResolver.ApplyAllMutations` vs `Patterns/`), peut paralléliser. S3 idéalement **après P6** pour ne pas redéplacer du code.
- **Chantier 2 (harnais)** : MC0006 inchangé par ce chantier (les templates produisent toujours `SourceMutation`, jamais de splice).
- **Chantier 5 (pipeline insertion)** : indépendant.

---

## Chantier 3 — Dette de cleanup post-refacto S0

- [ ] **Cleanup S0.7+** : déplacer le vrai code des `Scan*` `internal static` de `AlternativeGenerator` vers les classes scanners (delegation actuelle remplacée par implémentation autonome). Trigger : quand on doit modifier le comportement d'un scanner (typiquement S1 sur `UppercaseSequencesScanner`)
- [x] ~~**Refacto `LatexToUnicodeMath`** : Regex → XDocument ou parser dédié~~ → **code mort supprimé** (ADR 2026-06-22-Refactor-delete-dead-latextounicodemath), élimine les 6 MC0001 d'un coup
- [ ] **Refacto `OMathParaJcPatcher`** : Regex → XDocument, élimine 5 MC0001
- [ ] **Refacto `WpfMathAdapter` / `MixedLatexRenderer`** : élimine 5 MC0001 résiduels
- [ ] **`SuggestionService.cs`** (~1775 lignes) : poursuite extraction DDD si volonté de descendre sous 500 lignes
- [ ] **`AlternativeGenerator.cs`** : passera ~1100 → ~30-50 lignes une fois S0 cleanup fait (déplacement vrai code)

---

## Chantier 4 — Conventions et persistance

- [x] **Format ADR** : `docs/dev/decisions/YYYY-MM-DD-Kind-slug.md` avec Kind + Température + Statut + Supersedes + citation user (ADR [`Meta-adr-format`](../decisions/2026-04-24-Meta-adr-format.md))
- [x] **CLAUDE.md** racine : process décision + règles dev (cf. `## Process de décision`)
- [x] **ROADMAP.md** (ce fichier) : index unique des chantiers, mis à jour à chaque sous-livraison
- [x] **Skills user-invocable** versionnées : `/mathcursor-plan`, `/mathcursor-adr`, `/deploy-prod`, `/build-iss` dans `.claude/skills/`
- [ ] **Mémoire claude** : pointer vers ROADMAP.md depuis `~/.claude/projects/.../memory/MEMORY.md`

---

## Pour un Claude qui reprend la session

**Question 1 : « Sur quoi je travaillais ? »**
→ Lire les 10 derniers commits (`git log --oneline -10`) + cette ROADMAP. La dernière sous-livraison fermée a son commit hash listé.

**Question 2 : « Quelle est la prochaine étape ? »**
→ Cocher dans ROADMAP la première case `[ ]` non cochée du chantier en cours. Si plusieurs chantiers ouverts, demander à l'utilisateur de prioriser.

**Question 3 : « Quel ADR sert de référence pour cette étape ? »**
→ Liens dans chaque section de la ROADMAP. Tous les ADRs récents contiennent une section "Plan d'exécution" ou "Plan refacto — état d'avancement".

**Question 4 : « Comment je commence proprement ? »**
→ Invoquer `/mathcursor-plan <sujet>`. La skill force : couche cible + trade-offs qualité-orientés (jamais "temps") + règles MC pertinentes + plan en étapes numérotées + ADR si nécessaire.

**Question 5 : « Comment je documente ma décision ? »**
→ Invoquer `/mathcursor-adr` après validation utilisateur. Cite la citation user explicite.

---

## Mises à jour de cette ROADMAP

- **Quand** : à la fin de chaque sous-livraison (= chaque commit qui change l'état d'avancement).
- **Quoi** : cocher les cases `[ ]` → `[x]`, ajouter le hash du commit, ajuster la "Dernière mise à jour" en haut.
- **Comment** : éditer ce fichier dans le même commit que la sous-livraison.
