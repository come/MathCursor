# Feat — SuggestionService injecte le PatternPipeline au ZoneResolver (P7b)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Feat-pattern-pipeline-integration-zone-resolver.md`](2026-05-21-Feat-pattern-pipeline-integration-zone-resolver.md) — P7a (commit `a2d2516`)

## Citation acté

> « oui on termine » — utilisateur, 2026-05-21
> (validation pour enchaîner les sous-étapes P7b → P7c → P7d après le commit P7a)

## Contexte

P7b est la sous-étape **Adapter VSTO** de P7. Suite à P7a (Core),
le `ZoneResolver` peut désormais accepter un `PatternPipeline` +
`PatternRegistry` au ctor pour activer les templates. P7b modifie
`SuggestionService` (le constructeur central de la session VSTO) pour
les construire via la factory `DefaultPatternRegistry.BuildBoth()` et
les injecter.

À ce stade : le Core produit les `PatternCompletion[]` et l'adapter les
inclut dans le `ResolvedZone` retourné par `Resolve`. Mais la
`SuggestionPopupWindow` ne les affiche pas encore (= P7c). Donc l'UX
user reste dégradée jusqu'à P7c.

## Décision

Modification ciblée dans `SuggestionService` ctor :

```csharp
// AVANT (post-P7a) :
_resolver = new ZoneResolver(_engine);

// APRÈS (P7b) :
var (patternPipeline, patternRegistry) =
    MathCursor.Core.Patterns.DefaultPatternRegistry.BuildBoth();
_resolver = new ZoneResolver(_engine, patternPipeline, patternRegistry);
```

Une seule ligne modifiée + 3 lignes ajoutées. Le reste du
`SuggestionService` (handlers, sidecar, état session) inchangé.

## Tradeoff & alternatives écartées

- **Constructions paresseuses (lazy)** du registry : rejetée. Le coût
  d'allocation (3 templates instanciés au boot) est négligeable. La
  paresse n'apporte rien.
- **Injection externe via Settings ou config** : rejetée pour P7b.
  La factory `DefaultPatternRegistry` est suffisante. Si plus tard on
  veut désactiver les templates par feature flag, on remplacera l'appel
  par une logique conditionnelle — pas avant un besoin réel.

## Conséquences

### Code touché

- **Modifié** : `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` —
  ctor étendu pour appeler `DefaultPatternRegistry.BuildBoth()` et injecter
  au `ZoneResolver`.

### Tests

- Le projet adapter VSTO ne build pas en CLI (`VSTOOLS` targets requis,
  setup MSBuild Visual Studio). Sera buildé par VS / l'installer ISS.
- Tests adapter (`adapter-vsto/tests/MathCursor.Tests/`) référencent
  uniquement le projet Core (pas le projet VSTO). Ils continuent à
  passer 393/393.
- **Validation manuelle requise** en P7d via `/build-iss` puis test Word.

### API publique

Aucune. Modification interne au ctor d'un service VSTO non-exposé.

### Régression UX

Toujours dégradée sur main jusqu'à P7c (la popup n'affiche pas encore
les PatternCompletion). Le Core+Adapter produisent maintenant les
complétions mais elles restent dans `ResolvedZone.PatternCompletions`
sans consumer côté UI.

### Règles MC impactées

Aucune.

## Validation post-fix

```bash
# Build Core (inclut les types Patterns référencés par adapter)
dotnet build core-csharp/src/MathCursor.Core/MathCursor.Core.csproj
# → 0 erreur

# Tests adapter
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts

# Build VSTO (manuel via VS ou /build-iss skill)
# Validation visuelle en P7d
```

## Plan Patterns — état d'avancement

- [x] **P7a** — Core (commit `a2d2516`)
- [→] **P7b** — Adapter VSTO (cet ADR)
- [ ] **P7c** — WPF popup
- [ ] **P7d** — Test bout-en-bout Word + validation manuelle
