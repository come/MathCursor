# Refactor — Caret-aware ZoneResolver via CaretLocator (étape P1 du plan Patterns)

**Date :** 2026-05-21
**Kind :** Refactor
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Meta-pattern-templates-vs-ambig-closed.md`](2026-05-21-Meta-pattern-templates-vs-ambig-closed.md) — ADR de cadrage, P1 du plan d'organisation
- Étape P2-P5 à venir consommera `CaretLocator` pour les `PatternMatch` (mêmes invariants)

## Citation acté

> « ok je valide » — utilisateur, 2026-05-21
> (validation explicite du plan P1 détaillé : Option C — Hybride avec `CaretLocator` séparé + `ZoneResolver` qui délègue ; conventions `caret == End` inclus, span minimal en cas d'overlap, fallback rightmost si caret null ou hors zone)

## Contexte

Le `ZoneResolver` actuel expose toujours le `Spot` rightmost (le match-le-plus-à-droite émis par les scanners). Pour la suite du plan Patterns (P2-P5), il faut pouvoir cibler **le sous-pattern le plus proche du caret** — décision actée dans l'ADR de cadrage.

Aujourd'hui, sur une zone comme `AB+AC=AD` (trois `AmbiguityMatch` two-uppercase), le `Spot` est toujours AD. Si l'utilisateur clique dans Word entre `AB` et `+`, la popup montre quand même AD — incohérent avec son focus visuel.

P1 introduit le mécanisme **avant** P2-P5 pour deux raisons :

1. Permet de découpler la plomberie caret du concept pattern (testable indépendamment).
2. Est immédiatement utile pour les ambig closed actuelles (AB+AC=AD focus-correct dès maintenant), même sans pattern template.

## Décision

Ajout pur : un nouveau service `CaretLocator` + un paramètre optionnel `caretOffset` sur les trois overloads de `ZoneResolver.Resolve`. Comportement legacy strictement préservé quand `caretOffset == null`.

### 1. Service `CaretLocator`

Nouveau dossier `core-csharp/src/MathCursor.Core/Patterns/` (premier fichier de ce qui deviendra le projet Patterns à P2).

```csharp
public static class CaretLocator
{
    public static AmbiguityMatch? FindDeepestMatchAtCaret(
        IReadOnlyList<AmbiguityMatch>? matches, int caretOffset);
}
```

Algorithme : itère sur `matches`, filtre ceux dont `[Start..End]` contient `caretOffset` (inclusion **bilatérale** : `Start <= caret <= End`), retourne celui au plus petit `End - Start`.

### 2. Conventions sémantiques figées

| Cas | Comportement |
|---|---|
| `matches == null` ou vide | retourne `null` |
| `caretOffset < 0` | retourne `null` |
| `caret == Start` | inclus dans le match (début du match) |
| `caret == End` | **inclus** par convention UX (focus reste sur le match qu'on vient de finir) |
| Plusieurs matches overlap | celui au plus petit span (deepest) |
| Égalité de span minimal | premier dans l'ordre d'énumération (déterministe) |
| Aucun match contient le caret | retourne `null` → caller fallback legacy |

### 3. Extension de `ZoneResolver.Resolve` — 3 overloads

```csharp
ZoneResolver.Resolve(string rawSource, int? caretOffset = null)
ZoneResolver.Resolve(string rawSource, ResolutionSidecar sidecar, int? caretOffset = null)
ZoneResolver.Resolve(string rawSource, GlobalContext? globalCtx,
                     ResolutionSidecar? sidecar, int? caretOffset = null)
```

Default `null` = aucun caller existant n'est cassé. Les overloads convenience délèguent à l'overload principal.

### 4. Helper privé `ApplyCaretAware`

Centralise la logique caret au sein du `ZoneResolver` :

```csharp
private static ResolvedZone ApplyCaretAware(ResolvedZone zone, int? caretOffset)
{
    if (caretOffset == null) return zone;
    var deepest = CaretLocator.FindDeepestMatchAtCaret(zone.AllMatches, caretOffset.Value);
    if (deepest == null) return zone;
    return new ResolvedZone(
        rawSource:   zone.RawSource,
        mutedSource: zone.MutedSource,
        topLatex:    zone.TopLatex,
        spot:        deepest.Spot,
        spotStart:   deepest.Start,
        spotEnd:     deepest.End,
        allMatches:  zone.AllMatches,
        isIncomplete: zone.IsIncomplete,
        baseTopLatex: zone.BaseTopLatex);
}
```

Appliqué aux **deux** points de retour de l'overload principal (court-circuit `!hasSidecar && !hasContext` ET fin de pipeline complet) + au point de retour de `Resolve(rawSource, caretOffset)`. Garantit que tous les chemins respectent le caret.

`AllMatches` préservé tel quel (annotations `AppliedAltIdx` conservées). Seul le `Spot` exposé change.

## Tradeoff & alternatives écartées

Évaluation sur qualité + perf + extensibilité uniquement (cf. principe utilisateur 2026-05-13).

- **Option A — Logique caret dans `ZoneResolver` directement, sans service séparé**. Rejeté : single-responsibility flou (Resolver = pipeline + caret), `CaretLocator` non testable en isolation. À P5 il faudrait dupliquer la logique pour les `PatternMatch` (axe extensibilité).

- **Option B — `CaretLocator` séparé, `ZoneResolver` agnostique**. Caller appelle `ZoneResolver.Resolve` puis `CaretLocator.FindSpotAt` manuellement. Rejeté : robustesse dégradée (oubli côté caller = rightmost legacy utilisé silencieusement). API en deux temps moins lisible.

- **Option D — Méthode séparée `ResolveAtCaret(...)`**. Rejeté : duplique la logique entre `Resolve` et `ResolveAtCaret` (risque de drift), 6 nouvelles overloads en plus des 3 existantes, surface API non maîtrisée.

- **Convention `caret == End` exclusif**. Rejeté : ergonomie moins naturelle — l'utilisateur qui finit de taper `AB` et déclenche Ctrl+Espace s'attend à voir AB en focus, pas AC.

## Conséquences

### Code touché

- **Nouveau** :
  - `core-csharp/src/MathCursor.Core/Patterns/CaretLocator.cs` (premier fichier du dossier `Patterns/`)
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/CaretLocatorTests.cs` (13 tests)
  - `core-csharp/tests/MathCursor.Core.Tests/CaretAwareZoneResolverTests.cs` (11 tests)
- **Modifié** :
  - `core-csharp/src/MathCursor.Core/ZoneResolver.cs` — 3 signatures `Resolve` étendues, helper privé `ApplyCaretAware` ajouté, deux points de retour de l'overload principal utilisent le helper

### Tests

- **Core** : 980/987 verts (baseline `8477602` était 956/963 + 6 rouges préexistants). Delta : **+24 nouveaux verts**, 0 régression, 6 rouges identiques (`CrossMergeIndiceExposantBugTests` × 4 + 2 autres préexistants, hors scope P1).
- **Adapter** : 393/393 verts (inchangé, aucun caller adapter ne passe `caretOffset` à ce stade).
- **Analyzer** : non touché.

### API publique

- `ZoneResolver.Resolve(...)` × 3 overloads : nouveau paramètre `int? caretOffset = null` en queue. **Rétro-compatible** (default null = comportement legacy).
- `MathCursor.Core.Patterns.CaretLocator` : nouveau type public.
- `ResolvedZone` : aucun changement.
- `AmbiguityMatch` : aucun changement.

### Règles MC impactées

- **MC0001 / MC0006 / MC0009** : aucun impact.
- Aucun nouveau hit créé.

### Performance

- `CaretLocator.FindDeepestMatchAtCaret` : O(n) sur `AllMatches.Count` (typique ≤ 10 sur une zone). Pas de bench requis.
- `ApplyCaretAware` : 1 allocation `ResolvedZone` supplémentaire **seulement si un deepest est trouvé** ET `caretOffset != null`. Comportement default (null) = 0 allocation additionnelle.

## Validation post-fix

```bash
# Build
dotnet build core-csharp/src/MathCursor.Core/MathCursor.Core.csproj
# → 8 warnings préexistants (MC0001 × 4 LatexToUnicodeMath, MC0006 × 2 ZoneResolver
#   ligne splice fallback existant), 0 erreur

# Tests Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~CaretLocator|FullyQualifiedName~CaretAware"
# → 24/24 verts (13 CaretLocator + 11 CaretAwareZoneResolver)

dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 980/987 verts, 6 rouges préexistants, 1 ignoré S2

# Tests Adapter (aucun changement attendu)
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts
```

## Plan Patterns — état d'avancement (ROADMAP Chantier 6)

- [ ] **P0** — Attendre commit stable du WIP popup ✓ (commits `817c4d3`/`8477602`/`538f61e`)
- [x] **P1** — Caret-aware `ZoneResolver` + `CaretLocator` ✓ (cet ADR)
- [ ] **P2** — Squelette `Patterns/` (contrats `IPatternTemplate`, `PatternMatch`, etc.)
- [ ] **P3** — `EnsembleTemplate`
- [ ] **P4** — `IntervalUnionTemplate`
- [ ] **P5** — `ForallBelongsTemplate`
- [ ] **P6** — Retrait scanners V + canonical-set legacy
- [ ] **P7** — Popup consomme `PatternCompletion` + carrés
- [ ] **P8** — Test bout-en-bout dans Word
- [ ] **P9+** — `LimTemplate`, `SumTemplate`, etc.
