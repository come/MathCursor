# Feat — Intégration PatternPipeline dans ZoneResolver (P7a)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Meta-pattern-templates-vs-ambig-closed.md`](2026-05-21-Meta-pattern-templates-vs-ambig-closed.md) — ADR de cadrage P0
- [`2026-05-21-Feat-forall-belongs-pattern.md`](2026-05-21-Feat-forall-belongs-pattern.md) — P5 (commit `417e373`)
- [`2026-05-21-Refactor-remove-legacy-quantifier-set-scanners.md`](2026-05-21-Refactor-remove-legacy-quantifier-set-scanners.md) — P6 (commit `affbbf6`)

## Citation acté

L'utilisateur a validé chaque point de design via AskUserQuestion
interactive un par un (2026-05-21) :

> « met moi le recommandé et explique mieux a chaque fois »

Choix consolidés P7 :
- **Injection ctor optionnelle** du `PatternPipeline` + `PatternRegistry` dans `ZoneResolver`
- **Factory statique** `DefaultPatternRegistry.Build()` dans `Patterns/`
- **`PatternScanContext.TopAst` nullable**
- **Rendu `\square` LaTeX → OMath natural** pour les slots vides
- **Pattern d'abord, puis AmbigMatch** dans la popup
- **Préserver le live actuel** + Ctrl+Espace force (« aujourd'hui c'est live, ctrl+espace force, c'est ce que je veux »)
- **Commits séparés P7a + P7b + P7c (+ P7d doc)**
- **Nettoyer les stubs** `new Atom("Ident", "x")` dans les tests (passer null)
- **Localisation** `Patterns/DefaultPatternRegistry.cs`
- **Accepter le doublon overlap** Pattern↔AmbigMatch pour P7, fix si visible en P8

## Contexte

P7a est la sous-étape **Core** de P7 (branchement Patterns ↔ ZoneResolver
↔ Popup). Restaure l'UX user après la régression de P6 et l'étend avec
la composition forall-belongs + ensemble + interval-union.

Au niveau Core, il s'agit de :
1. Permettre au `ZoneResolver` d'invoquer le `PatternPipeline` quand il
   est construit avec un pipeline non-null.
2. Exposer les `PatternCompletion[]` dans `ResolvedZone`.
3. Factoriser la construction de la registry pilote via une factory
   statique réutilisable (consumer Core/Adapter, tests).

P7b (Adapter) et P7c (WPF popup) viendront consommer cette extension
dans des commits suivants.

## Décision

### 1. Ctor étendu `ZoneResolver`

```csharp
public ZoneResolver(LatticeEngine engine)
    : this(engine, patternPipeline: null, patternRegistry: null) { }

public ZoneResolver(
    LatticeEngine engine,
    MathCursor.Core.Patterns.PatternPipeline? patternPipeline,
    MathCursor.Core.Patterns.PatternRegistry? patternRegistry)
{
    // ...
}
```

Le ctor 1-arg historique délègue au 3-args avec null/null = **rétro-compat
totale**. Tous les tests P1-P6 et autres callers existants continuent
à fonctionner sans changement.

### 2. Champ privé `_patternPipeline` + invocation conditionnelle

Dans `Resolve(string rawSource, int? caretOffset = null)`, après le
calcul de `baseResolved` mais avant la construction finale de
`ResolvedZone`, on invoque :

```csharp
var patternCompletions = RunPatternPipeline(rawSource, ambig.TopLatex, caretOffset);
```

Helper privé :

```csharp
private IReadOnlyList<PatternCompletion>? RunPatternPipeline(
    string rawSource, string topLatex, int? caretOffset)
{
    if (_patternPipeline == null) return null;
    var patternCtx = new PatternScanContext(
        topAst: null,
        topLatex: topLatex ?? string.Empty,
        source: rawSource,
        caretOffset: caretOffset,
        startPos: 0,
        registry: _patternRegistry);
    return _patternPipeline.Run(patternCtx);
}
```

**Convention** : on passe `rawSource` (= ce que l'user a tapé, `V x app a R`)
au `PatternScanContext.Source`, pas la source mutée. C'est sur ce texte
que les templates matchent leur head. La source mutée (= post préprocesseur
canonical + prefs) ne ferait plus matcher `V`→∀ si une pref l'avait déjà
transformé en `forall`.

### 3. `TopAst` nullable

`PatternScanContext.TopAst` passe de `AstNode` à `AstNode?`. Aucun des
templates pilotes actuels n'utilise l'AST (ils scannent `Source`). Le
ZoneResolver passe `null` car `LatticeEngine.ConvertWithAmbiguity` ne
retourne pas l'AST dans son `AmbiguityResult`. Si P9+ ajoute un template
AST-aware, il fera `if (ctx.TopAst != null)` et le caller sera adapté.

### 4. `ResolvedZone.PatternCompletions`

Nouvelle propriété publique `IReadOnlyList<PatternCompletion>` ; default
`Array.Empty` pour rétro-compat. Le ctor accepte un nouveau paramètre
optionnel `patternCompletions = null` à la fin de la signature.

Tous les sites de construction de `ResolvedZone` ont été mis à jour
pour propager les `patternCompletions` :
- `Resolve(rawSource, caretOffset)` — peuple via `RunPatternPipeline`
- `Resolve(rawSource, globalCtx, sidecar, caretOffset)` — propage depuis
  `baseResolved`
- `ApplyCaretAware(zone, caretOffset)` — préserve les complétions du zone source

### 5. `DefaultPatternRegistry` factory

Nouveau fichier `core-csharp/src/MathCursor.Core/Patterns/DefaultPatternRegistry.cs` :

```csharp
public static class DefaultPatternRegistry
{
    public static PatternRegistry Build()
        => new PatternRegistry(new IPatternTemplate[] {
            new ForallBelongsTemplate(),
            new EnsembleTemplate(),
            new IntervalUnionTemplate(),
        });

    public static (PatternPipeline Pipeline, PatternRegistry Registry) BuildBoth()
        => (...);
}
```

Source unique de vérité pour "les templates pilote". P7b (adapter VSTO)
appellera `DefaultPatternRegistry.BuildBoth()`. Tests d'intégration
P7a aussi. Migration P9+ vers YAML = remplacer le contenu de cette
méthode, **aucun caller à modifier**.

### 6. Nettoyage stubs tests P3/P4/P5

Les 5 fichiers de tests Patterns qui construisaient un `PatternScanContext`
avec un stub `new Atom("Ident", "x")` pour `TopAst` passent maintenant
`null`. Aligne les tests sur l'usage réel (= aucun template ne consomme
TopAst). Files modified :

- `PatternPipelineSanityTests.cs`
- `Templates/IntervalUnionTemplateTests.cs`
- `Templates/EnsembleTemplateTests.cs`
- `Templates/ForallBelongsCompositionTests.cs`
- `Templates/ForallBelongsTemplateTests.cs`

## Tradeoff & alternatives écartées

Toutes les alternatives validées via AskUserQuestion. Voir l'ADR de
cadrage P0 et le plan détaillé P7 pour le raisonnement complet.

- **B. Registry par défaut interne au resolver** : rejeté (force l'alloc
  même si test sans patterns).
- **C. Param optionnel par appel Resolve** : rejeté (pollution
  signature).
- **B. Garder TopAst requis + refactor LatticeEngine pour exposer AST** :
  rejeté (refactor non motivé puisque pas de template AST-aware actuel).
- **C. Garder TopAst requis + passer un AST stub depuis resolver** :
  rejeté (sémantiquement faux, bug latent si futur template AST-aware
  arrive).
- **B. Auto-discovery via reflection** pour DefaultPatternRegistry :
  rejeté (magie, dépendance reflection, ordre non-déterministe).
- **B. Tests stubs `new Atom("Ident", "x")` laissés** : rejeté par le
  user (« nettoyer maintenant »).

## Conséquences

### Code touché

- **Nouveau** :
  - `core-csharp/src/MathCursor.Core/Patterns/DefaultPatternRegistry.cs` (~55 lignes)
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/ZoneResolverPatternsIntegrationTests.cs` (~120 lignes, 10 tests)
- **Modifié** :
  - `core-csharp/src/MathCursor.Core/Patterns/PatternScanContext.cs` — `TopAst` nullable + doc enrichie
  - `core-csharp/src/MathCursor.Core/ZoneResolver.cs` — ResolvedZone gagne `PatternCompletions`, ZoneResolver gagne ctor 3-args + champ `_patternPipeline`/`_patternRegistry` + helper `RunPatternPipeline` + propagation dans les 3 points de construction `ResolvedZone`
  - 5 fichiers de tests Patterns (stubs nettoyés : `new Atom(...)` → `null`)

### Tests

- **Core** : 1098/1105 verts (post-P6 = 1088/1095). Delta : **+10 nouveaux verts** (tous dans `ZoneResolverPatternsIntegrationTests`), 0 régression, 6 préexistants rouges idem.
- **Adapter** : 393/393 inchangé.

**Test pilote** au niveau ZoneResolver :
```
ZoneResolverPatternsIntegrationTests.PILOT_V_x_app_a_interval_union_via_zone_resolver
  Source : "V x app a [0,1]U[3,4]"
  Resolver : new ZoneResolver(engine, pipeline, registry) via DefaultPatternRegistry.BuildBoth()
  Result : ResolvedZone.PatternCompletions contient « ∀x ∈ [0,1]∪[3,4] »
  PreviewLatex : \forall x \in \left[0,1\right] \cup \left[3,4\right]
  → ✓ Vert
```

### API publique

- **Nouveau type public** : `MathCursor.Core.Patterns.DefaultPatternRegistry` (factory statique).
- **Ctor étendu** : `ZoneResolver(LatticeEngine, PatternPipeline?, PatternRegistry?)`. L'ancien ctor 1-arg délègue.
- **`ResolvedZone.PatternCompletions`** : nouvelle propriété publique.
- **`ResolvedZone` ctor** : nouveau paramètre optionnel `patternCompletions` à la fin.
- **`PatternScanContext.TopAst`** : type changé `AstNode` → `AstNode?`. Breaking change mineur sans impact pratique (callers passent déjà des stubs ou null).

### Règles MC impactées

- Aucune. Pas de Regex, pas de splice, pas de SuppressMessage.

### Performance

- `RunPatternPipeline` : un appel par `Resolve`. Le pipeline avec 3 templates fait :
  - ForallBelongsTemplate : O(n × variants) ≤ O(4n) sur source
  - EnsembleTemplate : O(n) sur source
  - IntervalUnionTemplate : O(n) sur source + récursion ≤ profondeur 3-5
  - Total ≤ O(n) par template, soit O(3n) au plus. Négligeable.
- Si pipeline null (ZoneResolver legacy) : ZERO coût additionnel.

### Régression UX user-visible

- **Toujours dégradée sur main jusqu'à P7b et P7c.** Le Core produit
  maintenant les `PatternCompletion[]` mais le `SuggestionService` adapter
  et la `SuggestionPopupWindow` WPF ne les consomment pas encore.
- P7b va modifier `SuggestionService` pour construire et injecter le
  Registry + Pipeline.
- P7c va adapter `SuggestionPopupWindow` pour afficher les
  `PatternCompletion[]` en tête de la liste.

## Validation post-fix

```bash
# Build
dotnet build core-csharp/src/MathCursor.Core/MathCursor.Core.csproj
# → 8 warnings préexistants, 0 erreur

# Tests intégration P7a
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~ZoneResolverPatternsIntegration"
# → 10/10 verts

# Test pilote
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~PILOT_V_x_app_a_interval_union_via_zone_resolver"
# → 1/1 vert

# Suite Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1098/1105 verts (6 préexistants rouges)

# Adapter
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts
```

## Plan Patterns — état d'avancement (ROADMAP Chantier 6)

- [x] **P0** — Attendre commit stable du WIP popup
- [x] **P1** — Caret-aware `ZoneResolver` + `CaretLocator` (commit `607a6f8`)
- [x] **P2** — Squelette `Patterns/` (commit `023e03d`)
- [x] **P3** — `EnsembleTemplate` (commit `7af2b37`)
- [x] **P4** — `IntervalUnionTemplate` (commit `121b092`)
- [x] **P5** — `ForallBelongsTemplate` + composition bout-en-bout (commit `417e373`)
- [x] **P6** — Retrait scanners legacy (commit `affbbf6`)
- [→] **P7a** — Branchement PatternPipeline ↔ ZoneResolver (cet ADR)
- [ ] **P7b** — Adapter VSTO : SuggestionService construit le registry et injecte
- [ ] **P7c** — WPF popup : SuggestionPopupWindow consomme PatternCompletion[]
- [ ] **P7d** — Test bout-en-bout dans Word
- [ ] **P8** — Validation manuelle PAP-friendly
- [ ] **P9+** — `LimTemplate`, `SumTemplate`, etc.
