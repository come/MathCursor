# Feat — IntegralTemplate + DerivativeTemplate (P9c + P9d)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Feat-lim-pattern.md`](2026-05-21-Feat-lim-pattern.md) — P9a
- [`2026-05-21-Feat-sum-pattern.md`](2026-05-21-Feat-sum-pattern.md) — P9b
- [`2026-05-21-Refactor-forall-belongs-arglist-convention.md`](2026-05-21-Refactor-forall-belongs-arglist-convention.md) — P5R (ArgListPatternBase)

## Citation acté

> « oui » — utilisateur, 2026-05-21
> (validation pour enchaîner les templates structurels après P9a/P9b)

## Contexte

P9c + P9d livrent les 2 derniers templates structurels prévus dans le
plan P9 : intégrale définie et dérivée. Tous deux héritent
d'`ArgListPatternBase` et réutilisent les helpers communs
(`ConcatArgsFrom`, `ConvertInfinityToken*`) promus en P9b.

Avec P9c + P9d, le `DefaultPatternRegistry` expose **7 templates pilotes** :
- `forall-belongs` (P5R)
- `ensemble` (P3)
- `interval-union` (P4)
- `lim` (P9a)
- `sum` (P9b)
- `integral` (P9c, cet ADR)
- `derivative` (P9d, cet ADR)

## Décision

### IntegralTemplate (P9c)

| Aspect | Détail |
|---|---|
| TemplateId | `integral` |
| Heads | `Int` (canonique), `int`, `intégrale` (FR), `∫` (unicode) |
| Slots positionnels | `var`, `from`, `to`, `expression` (tous requis) |
| Convention ordre | var en premier — cohérence MathCursor avec Lim/Sum, même si LaTeX standard mettrait `dx` en fin |
| Rendu LaTeX | `\int_{<from>}^{<to>} <expression> \, d<var>` |
| Conversion infini | héritée d'`ArgListPatternBase` |
| Mutation source | `Int x 0 1 f(x)` → `int x 0 1 f(x)` |

Exemples :
- `Int x 0 1 f(x)` → `\int_{0}^{1} f(x) \, dx`
- `Int t -oo +oo e^(-t²)` → `\int_{-\infty}^{+\infty} e^(-t²) \, dt`
- `∫ x a b g(x)` → `\int_{a}^{b} g(x) \, dx`

### DerivativeTemplate (P9d)

| Aspect | Détail |
|---|---|
| TemplateId | `derivative` |
| Heads | `Derive` (canonique), `derive`, `dérivée` (FR), `dérive` (FR) |
| Slots positionnels | `var`, `expression` (tous requis, 2 slots) |
| Rendu LaTeX | `\frac{d}{d<var>} <expression>` |
| Mutation source | `Derive x f(x)` → `derive x f(x)` |
| CompletenessScore | 33/66/100 (3 paliers vs 5 pour Sum/Int) |

Exemples :
- `Derive x f(x)` → `\frac{d}{dx} f(x)`
- `Derive t e^t` → `\frac{d}{dt} e^t`
- `Derive x x²+1` → `\frac{d}{dx} x²+1` (expression multi-tokens)

### Inscription registry

```csharp
public static PatternRegistry Build()
{
    return new PatternRegistry(new IPatternTemplate[]
    {
        new ForallBelongsTemplate(),
        new EnsembleTemplate(),
        new IntervalUnionTemplate(),
        new LimTemplate(),
        new SumTemplate(),
        new IntegralTemplate(),    // P9c
        new DerivativeTemplate(),  // P9d
    });
}
```

Le `SuggestionService` adapter VSTO consomme automatiquement les 7
templates via `DefaultPatternRegistry.BuildBoth()` au boot.

## Tradeoff & alternatives écartées

### Ordre des args pour Integral

**Var en premier** retenu (cohérence MathCursor). Alternative : var en
dernier (convention LaTeX `dx`). Rejeté car briserait l'uniformité
avec Lim/Sum (= "var d'abord, contexte ensuite").

### Heads multiples pour Derivative

`Derive`/`derive`/`dérivée`/`dérive` (4 variants). Rejet possible :
`d/dx` (= forme LaTeX directe). Rejeté : `d/dx` ne suit pas le moule
"head + args espace" — c'est une notation inline mixée. Si user veut,
P9e+ peut ajouter un pattern spécifique `LeibnizDerivativeTemplate`.

### Notation primé `f'(x)` pour dérivée

Non couverte par DerivativeTemplate. Le pattern dérivée Lagrange (`'`)
est un opérateur post-fix, pas un head pattern. À traiter dans un
template séparé en P10+ si besoin.

## Conséquences

### Code touché

- **Nouveau** :
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/IntegralTemplate.cs` (~145 lignes)
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/DerivativeTemplate.cs` (~125 lignes)
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/IntegralTemplateTests.cs` (11 tests)
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/DerivativeTemplateTests.cs` (12 tests)
- **Modifié** :
  - `core-csharp/src/MathCursor.Core/Patterns/DefaultPatternRegistry.cs` — ajout `new IntegralTemplate()` + `new DerivativeTemplate()` (= 7 templates pilotes)

### Tests

- **Core** : 1163/1170 verts (post-P9b = 1140/1147). Delta : **+23 nouveaux verts**, 6 préexistants rouges idem.
- **Adapter** : 393/393 inchangé (templates auto-inscrits via DefaultPatternRegistry).

### API publique

- Nouveaux types publics : `IntegralTemplate`, `DerivativeTemplate`.
- `DefaultPatternRegistry.Build()` / `BuildBoth()` retournent 7 templates.

### Régression UX

Aucune. Ajout pur. L'utilisateur Word peut maintenant taper :
- `Int x 0 1 f(x)` → ∫₀¹ f(x) dx
- `Derive x f(x)` → d/dx f(x)
- Plus tous les patterns précédents (forall, ensemble, lim, sum, etc.)

### Règles MC impactées

Aucune.

## Validation post-fix

```bash
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~IntegralTemplate|FullyQualifiedName~DerivativeTemplate"
# → 23/23 verts (11 Int + 12 Derive)

dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1163/1170 verts (6 préexistants rouges)
```

## Plan Patterns — état d'avancement (Chantier 6)

- [x] **P9a** — LimTemplate (commit `154e947`)
- [x] **P9b** — SumTemplate + helpers promus (commit `57a8b6c`)
- [x] **P9c** — IntegralTemplate (cet ADR)
- [x] **P9d** — DerivativeTemplate (cet ADR)
- [ ] **P9e** — Migration YAML pour patterns triviaux (ensemble, possibly autres)
- [ ] **P8** — Validation manuelle PAP-friendly via `/build-iss` + Word

Les 7 templates pilotes (forall, ensemble, interval-union, lim, sum,
integral, derivative) couvrent l'essentiel du vocabulaire math lycée.
P10+ pourra ajouter Matrices, Vecteurs (notation flèche), Dérivées
primées, Limites avec direction (lim x→0+), etc.
