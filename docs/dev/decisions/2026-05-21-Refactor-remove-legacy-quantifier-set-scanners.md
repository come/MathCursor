# Refactor — Retrait des scanners legacy V→∀ et R/N/Z/Q/C→ℝ/ℕ/ℤ/ℚ/ℂ (P6)

**Date :** 2026-05-21
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Meta-pattern-templates-vs-ambig-closed.md`](2026-05-21-Meta-pattern-templates-vs-ambig-closed.md) — ADR de cadrage P0 (P6 du plan)
- [`2026-05-21-Feat-ensemble-pattern.md`](2026-05-21-Feat-ensemble-pattern.md) — P3 (couvre R/N/Z/Q/C, commit `7af2b37`)
- [`2026-05-21-Feat-forall-belongs-pattern.md`](2026-05-21-Feat-forall-belongs-pattern.md) — P5 (couvre V/E + composition, commit `417e373`)
- [`2026-05-13-Refactor-ambiguity-scanners-strategy.md`](2026-05-13-Refactor-ambiguity-scanners-strategy.md) — S0, introduit `IAmbiguityScanner` (architecture parente)

## Citation acté

> « A reecriture » — utilisateur, 2026-05-21
> (validation option A — retrait des scanners legacy MAINTENANT, réécriture
> des ~20 tests Core qui en dépendent, malgré la régression UX temporaire
> sur main jusqu'à P7 — l'utilisateur accepte ce trade-off pour rester
> dans l'ordre du plan P0)

## Contexte

Étape P6 du plan d'organisation cadré par l'ADR de cadrage. Le
comportement des scanners legacy `VAsForallEAsExistsScanner` et
`CanonicalSetLettersScanner` est désormais **couvert au niveau Core**
par les templates Patterns introduits en P3 et P5 :

- `EnsembleTemplate` (P3) : lettres canoniques R/N/Z/Q/C + modifiers
  (couvre exactement `ScanCanonicalSetLetters`).
- `ForallBelongsTemplate` (P5) : V/E + ∀/∃ unicode, avec slot var et
  slot domain optionnel (couvre `ScanVAsForallEAsExists` et l'étend).

Maintenir les 2 chemins (legacy ambig + Patterns) en parallèle créerait
une dette permanente. Retrait maintenant, malgré la régression UX
temporaire user-visible sur main (= entre P6 et P7, la popup n'affichera
plus rien pour `V`/`E`/`R`/`N`/`Z`/`Q`/`C` — les templates Patterns ne
sont pas encore branchés au ZoneResolver côté Core ni à
SuggestionPopupWindow côté Adapter). P7 va brancher les Patterns et
restaurer le comportement user-visible (et l'étendre via la
composition).

Le risque UX temporaire est connu et accepté (aucune release prévue
entre P6 et P7).

## Décision

### 1. Retrait des 2 scanners du `AmbiguityScannerPipeline.Default`

```csharp
public static AmbiguityScannerPipeline Default { get; } = new AmbiguityScannerPipeline(new IAmbiguityScanner[]
{
    new AngleTwoLetterPlaceholderScanner(),     // 0
    new AstBasedScanner(),                       // 1
    new DecoratedTwoThreeUpperScanner(),         // 2
    new UppercaseSequencesScanner(),             // 3
    // VAsForallEAsExistsScanner (4) RETIRÉ — couvert par ForallBelongsTemplate
    // CanonicalSetLettersScanner (5) RETIRÉ — couvert par EnsembleTemplate
    new FunctionTypicalCommaCoordsScanner(),     // 6
    new VectorLayoutFlipTopLevelScanner(),       // 7
    new TightChainExtensionScanner(),            // 8
    new DecimalVsMultiplicationScanner(),        // 9
});
```

Le pipeline passe de **10 à 8 scanners** pour les ambig closed
restantes.

### 2. Suppression du code mort des scanners

- **Fichiers supprimés** :
  - `core-csharp/src/MathCursor.Core/Lattice/Ambiguity/Scanners/VAsForallEAsExistsScanner.cs`
  - `core-csharp/src/MathCursor.Core/Lattice/Ambiguity/Scanners/CanonicalSetLettersScanner.cs`
- **Méthodes statiques supprimées** dans `AlternativeGenerator.cs` :
  - `ScanVAsForallEAsExists(...)` (~73 lignes avec doc)
  - `ScanCanonicalSetLetters(...)` (~58 lignes avec doc)
  - Helper `IsCanonicalSetDelimiter(char)` (utilisé uniquement par
    le scanner canonical-set retiré)
- **Constantes supprimées** dans `AlternativeGenerator.cs` :
  - `RuleVAsForall = "v-as-forall"`
  - `RuleEAsExists = "e-as-exists"`
  - `RuleCanonicalSet = "canonical-set"`
- **Entrées `GetRulePriority`** correspondantes retirées (3 entrées
  switch).

### 3. Adaptation des tests (~20 tests)

#### Tests supprimés (couverts par les templates Patterns)

- `ZoneResolverTests.cs` (6 tests V/E)
  - `Resolve_V_alone_no_pref_yields_ambig_spot`
  - `Resolve_V_with_forall_pref_mutates_to_forall`
  - `Resolve_V_x_dans_R_with_forall_pref_renders_full`
  - `Resolve_V_with_identity_pref_no_mutation`
  - `Resolve_V_with_racine_pref_mutates_to_racine`
  - `IsIncomplete_with_forall_pref_after_dans`
- `Lattice/AlternativeGeneratorTests.cs` (13 tests V/E/R/N/Z)
  - 6 tests `V_*` (V_yields_three, V_alone_yields_three,
    V_alt_previews, V_times_x_no_forall_ambig,
    Forall_x_dans_R_juxtaposition, E_yields_two)
  - 7 tests `R_*`/`N_*`/`Z_*` (R_isolated, R_in_pi_R_squared × 2,
    R_followed_by_op, R_followed_by_comma, N_isolated, Z_isolated)
- `Lattice/VectorCoordinatesTests.cs` (1 test : V_alone_keeps_v_as_forall_ambig)

#### Tests adaptés (utilisent une autre rule comme exemple générique)

- `ZoneResolverTests.cs` : `Clear_resets_preferences` et
  `HasPreference_reflects_state` utilisent désormais
  `RuleTwoUppercase` au lieu de `RuleVAsForall` — la rule ambig vec/
  paren/bracket reste branchée dans le pipeline.

#### Tests préservés (rendu LaTeX, indépendants des scanners)

- `Forall_alone_renders_just_forall` (rendu pipeline forall→\forall )
- `Forall_x_dans_bbR_via_juxtaposition` (rendu juxtaposition)
- `BbR_with_modifier_via_pipeline` (rendu bbR* → \mathbb{R}^*)
- `Vx_collé_no_ambig`, `Volume_no_ambig` (= cas où Spot est null,
  indépendant des scanners retirés)

## Tradeoff & alternatives écartées

Évaluation sur qualité + perf + extensibilité uniquement.

- **Option B — Swap P6↔P7** (brancher Patterns au ZoneResolver+Popup
  d'abord, puis retirer les scanners). Rejeté par l'utilisateur :
  préfère rester dans l'ordre du plan P0. Le coût (régression UX
  temporaire sur main) est accepté car aucune release prod n'est prévue
  entre P6 et P7.

- **Option C — Coexistence permanente** (laisser les 2 chemins en
  parallèle tant que les Patterns ne sont pas validés en prod).
  Rejeté : dette long-terme, 2 systèmes à maintenir, drift potentiel
  entre les RuleId scanners et les TemplateId Patterns.

- **Adaptation des tests legacy au lieu de suppression** (ex.
  ré-écrire `Resolve_V_*` pour appeler `ForallBelongsTemplate`
  directement). Rejeté : les tests d'intégration `forall-belongs` →
  `ZoneResolver` viendront en P7 quand le branchement existera. Les
  tests legacy auraient été tautologiques (assertions sur `MutedSource`
  d'un `Resolve` qui n'a plus de mécanisme V→∀). Mieux : suppression
  propre + nouvelle couverture en P7.

- **Conservation des constantes `RuleVAsForall`/etc. au cas où**.
  Rejeté : ces constantes ne sont plus référencées par aucun code
  vivant après retrait des scanners et adaptation des tests. Les
  conserver serait du code mort caché qui pourrait induire en erreur
  un futur lecteur (« cette rule existe encore ? »). Suppression nette.

## Conséquences

### Code touché

- **Modifié** :
  - `core-csharp/src/MathCursor.Core/Lattice/Ambiguity/AmbiguityScannerPipeline.cs` — 2 entrées retirées du `Default`
  - `core-csharp/src/MathCursor.Core/Lattice/AlternativeGenerator.cs` — 3 const + 2 méthodes statiques + 1 helper + 3 entrées switch retirés (~145 lignes)
  - `core-csharp/tests/MathCursor.Core.Tests/ZoneResolverTests.cs` — 6 tests supprimés, 2 tests adaptés
  - `core-csharp/tests/MathCursor.Core.Tests/Lattice/AlternativeGeneratorTests.cs` — 13 tests supprimés, en-tête de section refondue
  - `core-csharp/tests/MathCursor.Core.Tests/Lattice/VectorCoordinatesTests.cs` — 1 test supprimé
- **Supprimés** :
  - `core-csharp/src/MathCursor.Core/Lattice/Ambiguity/Scanners/VAsForallEAsExistsScanner.cs`
  - `core-csharp/src/MathCursor.Core/Lattice/Ambiguity/Scanners/CanonicalSetLettersScanner.cs`

### Tests

- **Core** : 1088/1095 verts (post-P5 = 1108/1115). Perte de 20 tests = ceux supprimés. **0 régression** sur les autres tests. 6 préexistants rouges idem (4 CrossMergeIndice + 2 CorpusLycee/Seconde par `NotImplementedException`).
- **Adapter** : 393/393 inchangé (aucun caller adapter ne dépendait des 2 scanners legacy).
- **Analyzer** : non touché.

### API publique

- **Constantes retirées** : `AlternativeGenerator.RuleVAsForall`,
  `AlternativeGenerator.RuleEAsExists`, `AlternativeGenerator.RuleCanonicalSet`.
- **Types publics retirés** :
  `MathCursor.Core.Lattice.Ambiguity.Scanners.VAsForallEAsExistsScanner`,
  `MathCursor.Core.Lattice.Ambiguity.Scanners.CanonicalSetLettersScanner`.
- **Méthodes internal retirées** :
  `AlternativeGenerator.ScanVAsForallEAsExists`,
  `AlternativeGenerator.ScanCanonicalSetLetters`.

Si un consumer externe référençait ces APIs : **rupture de compat**.
Aucun consumer externe connu (projet privé, pas d'API publique
diffusée). Aucun fichier de l'adapter VSTO ne les référence (vérifié
par grep).

### Règles MC impactées

- Aucune. Retrait pur de code mort.

### Performance

- `AmbiguityScannerPipeline.Run` : 2 scanners de moins à itérer.
  Gain négligeable (~µs).
- Mémoire : -145 lignes de code dans Core (les fichiers scanner +
  méthodes statiques).

### Régression UX temporaire (assumée)

Entre P6 (cet ADR) et P7 (branchement Patterns au ZoneResolver + Popup),
l'utilisateur Word ne verra **plus rien** dans la popup pour les
patterns suivants :

- `V` seul → plus de proposition `∀` / `√`
- `V x` → plus de proposition `∀x`
- `E` seul → plus de proposition `∃`
- `R` / `N` / `Z` / `Q` / `C` seuls → plus de proposition `ℝ` / `ℕ` / `ℤ` / `ℚ` / `ℂ`

Le rendu côté pipeline (= `forall x dans bbR` → `\forall x \in \mathbb{R}`)
**reste fonctionnel** parce que c'est du pur lattice/parser, pas des
scanners ambig. Donc un utilisateur qui tape `forall` directement (au
lieu de `V`) ou `bbR` (au lieu de `R`) verra le bon rendu. Mais les
raccourcis ASCII V/R/N/etc. ne déclencheront plus la popup.

Aucune release prod planifiée pendant cette fenêtre. P7 restaure
l'UX complète et l'étend (composition forall-belongs + ensemble +
interval-union).

## Validation post-fix

```bash
# Build
dotnet build core-csharp/src/MathCursor.Core/MathCursor.Core.csproj
# → 8 warnings préexistants, 0 erreur

# Suite Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1088/1095 verts (6 préexistants rouges)

# Adapter (inchangé)
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts
```

## Fenêtre de réversibilité

Température **forte** — suppression structurelle. Conditions qui
justifieraient un revert via ADR superseding :

1. **P7 dérape ou est repoussé** : si le branchement Patterns au
   ZoneResolver+Popup ne peut pas être fait dans un délai raisonnable,
   on peut envisager de restaurer temporairement les 2 scanners (via
   `git revert`) pour préserver l'UX. Le code des scanners est dans
   l'historique git (commit pré-P6).
2. **Découverte d'un cas user non couvert par les templates** : si
   un test bout-en-bout révèle que `ForallBelongsTemplate` ou
   `EnsembleTemplate` ne couvre pas un cas que les scanners legacy
   géraient correctement (peu probable vu les 38 tests bout-en-bout
   verts en P3+P5). Revert + fix + nouveau retrait.

## Plan Patterns — état d'avancement (ROADMAP Chantier 6)

- [x] **P0** — Attendre commit stable du WIP popup (commits `817c4d3`/`8477602`/`538f61e`)
- [x] **P1** — Caret-aware `ZoneResolver` + `CaretLocator` (commit `607a6f8`)
- [x] **P2** — Squelette `Patterns/` (commit `023e03d`)
- [x] **P3** — `EnsembleTemplate` (commit `7af2b37`)
- [x] **P4** — `IntervalUnionTemplate` (commit `121b092`)
- [x] **P5** — `ForallBelongsTemplate` + composition bout-en-bout (commit `417e373`)
- [x] **P6** — Retrait scanners V + canonical-set legacy (cet ADR)
- [ ] **P7** — Popup consomme `PatternCompletion` + carrés (restaure UX et l'étend)
- [ ] **P8** — Test bout-en-bout dans Word
- [ ] **P9+** — `LimTemplate`, `SumTemplate`, etc.
