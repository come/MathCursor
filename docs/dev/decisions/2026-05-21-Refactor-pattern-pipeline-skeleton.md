# Refactor — Squelette Patterns/ : contrats IPatternTemplate + pipeline + registry (P2)

**Date :** 2026-05-21
**Kind :** Refactor
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Meta-pattern-templates-vs-ambig-closed.md`](2026-05-21-Meta-pattern-templates-vs-ambig-closed.md) — ADR de cadrage, P2 du plan d'organisation
- [`2026-05-21-Refactor-caret-aware-zone-resolver.md`](2026-05-21-Refactor-caret-aware-zone-resolver.md) — P1, déjà livré (commit `607a6f8`)

## Citation acté

> « ok on valide tout ca » — utilisateur, 2026-05-21
> (validation du plan P2 : hiérarchie `abstract class + sealed subclasses` pour `SlotValue` et `SlotType` ; `PatternPipeline` stateless ; `PatternScanContext` séparé de `ScanContext` ambig ; localisation dans `MathCursor.Core/Patterns/` ; `EmptySlot.Instance` singleton)

## Contexte

P2 du plan d'organisation cadré par l'ADR de cadrage. Pose la **structure** du nouveau projet pattern templates sans aucun template inscrit — l'objectif est de geler les contrats avant d'implémenter les premiers templates en P3-P5.

À ce stade, le `PatternPipeline` tourne à vide : aucun comportement user-visible n'est introduit. Le squelette permet aux étapes suivantes (P3 EnsembleTemplate, P4 IntervalUnionTemplate, P5 ForallBelongsTemplate) de venir s'inscrire dans une infrastructure stable et testable.

## Décision

Création de 9 fichiers dans `core-csharp/src/MathCursor.Core/Patterns/` :

### Types data

| Fichier | Rôle |
|---|---|
| `IPatternTemplate.cs` | Contrat : `TemplateId`, `Order`, `TryMatchHead(ctx)`, `Expand(state, ctx)` |
| `PatternScanContext.cs` | POCO immuable `(TopAst, TopLatex, Source, CaretOffset?)`. Distinct de `ScanContext` ambig pour ne pas forcer la notion caret dans les ambig closed |
| `SlotType.cs` | `abstract class` + 4 sealed : `IdentifierSlot`, `IdentifierListSlot`, `ExpressionSlot`, `PatternRefSlot(patternId)` |
| `SlotSpec.cs` | `{ Name, Type, Required, Opener? }`. Slot optionnel = `Required=false` + `Opener` token-tête |
| `SlotValue.cs` | `abstract class` + 3 sealed : `EmptySlot` (singleton `Instance`), `FilledSlotAtom(Text, Start, End)`, `FilledSlotSubPattern(Sub)` |
| `PatternMatch.cs` | `{ TemplateId, SourceStart, SourceEnd, Slots, IsComplete }` immuable + helpers `WithSourceEnd`, `WithSlot`, `WithComplete` |
| `PatternCompletion.cs` | `{ Description, PreviewLatex, HintLatex, Mutation?, CompletenessScore }`. `HintLatex` = preview avec slots vides matérialisés (carrés à venir P7) |

### Orchestration

| Fichier | Rôle |
|---|---|
| `PatternPipeline.cs` | Stateless. Ctor `(IEnumerable<IPatternTemplate>)` trié par `Order`. `Run(ctx)` retourne `IReadOnlyList<PatternCompletion>`. Avec 0 template : retourne `Array.Empty`, pas de NPE |
| `PatternRegistry.cs` | Map `string → IPatternTemplate`. `Get(id)`, `TryGet(id, out)`, `Count`. Rejette les duplicates `TemplateId` via `ArgumentException` |

### Conventions figées

- **Allocations** : `EmptySlot.Instance` singleton, `IdentifierSlot.Instance`, `IdentifierListSlot.Instance`, `ExpressionSlot.Instance` — aucun slot vide ou type immuable n'alloue à chaque création de pattern partiel.
- **Immutabilité** : `PatternMatch.WithSlot(name, value)` retourne une nouvelle instance avec dictionnaire reconstruit. Pas de mutation en place — cohérent avec l'AST.
- **Stateless pipeline** : aucun cache d'expansion entre `Run` ; le caller (P7 popup) garde l'état si nécessaire.
- **Rejet null robuste** : ctor de `PatternPipeline` et `PatternRegistry` lèvent `ArgumentNullException` ; les entrées `null` à l'intérieur de la liste sont **skipped** (robustesse face aux factories partielles).

## Tradeoff & alternatives écartées

Évaluation sur qualité + perf + extensibilité uniquement (cf. principe utilisateur 2026-05-13).

- **Hiérarchie via C# 9 records** (`record SlotValue` etc.). Rejeté : nécessite polyfills `IsExternalInit` + `RequiredMemberAttribute` côté .NET Standard 2.0, multiplie les shims dans Core. La hiérarchie `abstract class + sealed subclasses` est cohérente avec `AstNode/AstNodes.cs` et n'ajoute aucune dépendance.

- **Pipeline stateful (`Feed(token) → completions`)** avec cache d'expansion entre frappes. Rejeté : partage d'état, tests plus complexes, drift entre cache et état réel. La pipeline stateless est testable, déterministe, parallélisable. Aligné sur `AmbiguityScannerPipeline` qui est également stateless.

- **`PatternScanContext` = extension de `ScanContext` ambig avec `caretOffset` ajouté**. Rejeté : force la notion caret dans la sémantique des ambig closed qui n'en ont pas besoin (couplage indésirable). Le coût "3 champs dupliqués" est cosmétique vs le bénéfice de separation of concerns.

- **`IPatternTemplate` dans `MathCursor.Core.Abstractions/`**. Rejeté pour P2 : les contrats touchent `AmbiguityMatch` et `AstNode` (types Core). Si en P9+ un consumer externe veut implémenter un template custom, déplacement possible — pas de coût aujourd'hui.

- **`SlotType` comme enum + string PatternId**. Rejeté : `switch (slotType)` exhaustif impossible à étendre sans modifier tous les consommateurs. La hiérarchie classes est Open/Closed.

## Conséquences

### Code touché

- **Nouveau** (9 fichiers, ~270 lignes) :
  - `core-csharp/src/MathCursor.Core/Patterns/IPatternTemplate.cs`
  - `core-csharp/src/MathCursor.Core/Patterns/PatternScanContext.cs`
  - `core-csharp/src/MathCursor.Core/Patterns/SlotType.cs`
  - `core-csharp/src/MathCursor.Core/Patterns/SlotSpec.cs`
  - `core-csharp/src/MathCursor.Core/Patterns/SlotValue.cs`
  - `core-csharp/src/MathCursor.Core/Patterns/PatternMatch.cs`
  - `core-csharp/src/MathCursor.Core/Patterns/PatternCompletion.cs`
  - `core-csharp/src/MathCursor.Core/Patterns/PatternPipeline.cs`
  - `core-csharp/src/MathCursor.Core/Patterns/PatternRegistry.cs`
- **Nouveau tests** (2 fichiers, 16 tests) :
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/PatternPipelineSanityTests.cs` (6 tests)
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/PatternRegistrySanityTests.cs` (10 tests)
- **Modifié** : aucun fichier de production existant. P2 est ajout pur.

### Tests

- **Core** : 996/1003 verts (post-P1 = 980/987). Delta : **+16 nouveaux verts**, 0 régression, 6 rouges préexistants `CrossMergeIndiceExposant` idem.
- **Adapter** : 393/393 verts (inchangé).
- **Analyzer** : non touché.

### API publique

- **9 nouveaux types publics** dans le namespace `MathCursor.Core.Patterns` :
  - `IPatternTemplate`
  - `PatternScanContext`
  - `SlotType`, `IdentifierSlot`, `IdentifierListSlot`, `ExpressionSlot`, `PatternRefSlot`
  - `SlotSpec`
  - `SlotValue`, `EmptySlot`, `FilledSlotAtom`, `FilledSlotSubPattern`
  - `PatternMatch`
  - `PatternCompletion`
  - `PatternPipeline`
  - `PatternRegistry`
- Aucun caller existant n'est cassé (ajout pur, aucun fichier modifié).
- À P3+ : les premiers templates concrets viennent implémenter `IPatternTemplate` sans modifier les contrats.

### Règles MC impactées

- Aucune. Code déclaratif pur, pas de Regex, pas de splice, pas de SuppressMessage.

### Performance

- `EmptySlot.Instance` + `IdentifierSlot.Instance` etc. : singletons, zéro allocation pour les types immuables.
- `PatternRegistry.Get(id)` : O(1) lookup `Dictionary`.
- `PatternPipeline.Run` : O(n_templates), chaque template fait son propre `TryMatchHead` + `Expand`. Pas de bench requis tant qu'aucun template n'est inscrit.

## Validation post-fix

```bash
# Build
dotnet build core-csharp/src/MathCursor.Core/MathCursor.Core.csproj
# → 8 warnings préexistants (MC0001 × 6 LatexToUnicodeMath, MC0006 × 2 ZoneResolver),
#   0 erreur

# Tests Patterns sanity uniquement
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~PatternPipelineSanity|FullyQualifiedName~PatternRegistrySanity"
# → 16/16 verts

# Suite complète Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 996/1003 verts (6 préexistants rouges, 1 ignoré S2)

# Adapter (aucun changement attendu)
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts
```

## Plan Patterns — état d'avancement (ROADMAP Chantier 6)

- [x] **P0** — Attendre commit stable du WIP popup (commits `817c4d3`/`8477602`/`538f61e`)
- [x] **P1** — Caret-aware `ZoneResolver` + `CaretLocator` (commit `607a6f8`, ADR `Refactor-caret-aware-zone-resolver`)
- [x] **P2** — Squelette `Patterns/` (cet ADR)
- [ ] **P3** — `EnsembleTemplate` (heads R/R*/N/Z/Q/C + delegate à IntervalUnionTemplate)
- [ ] **P4** — `IntervalUnionTemplate`
- [ ] **P5** — `ForallBelongsTemplate`
- [ ] **P6** — Retrait scanners V + canonical-set legacy
- [ ] **P7** — Popup consomme `PatternCompletion` + carrés
- [ ] **P8** — Test bout-en-bout dans Word
- [ ] **P9+** — `LimTemplate`, `SumTemplate`, etc.
