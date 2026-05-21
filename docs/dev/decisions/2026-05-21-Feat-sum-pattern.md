# Feat — SumTemplate : pattern sommation avec 4 slots positionnels (P9b)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Feat-lim-pattern.md`](2026-05-21-Feat-lim-pattern.md) — P9a (commit `154e947`)
- [`2026-05-21-Refactor-forall-belongs-arglist-convention.md`](2026-05-21-Refactor-forall-belongs-arglist-convention.md) — P5R (ArgListPatternBase)

## Citation acté

> « oui » — utilisateur, 2026-05-21
> (validation pour enchaîner P9b après P9a)

## Contexte

P9b livre `SumTemplate`, deuxième template positionnel après LimTemplate.
4 slots tous requis (vs 3 pour Lim), conversion infini partagée via
helpers promus dans `ArgListPatternBase` (P9b.1).

## Décision

### 1. Heads supportés (5 variants)

| Head | LatexSymbol | Mutation | Weight |
|---|---|---|---|
| `Sum` | `\sum` | `sum` | 100 |
| `sum` | `\sum` | `sum` | 95 |
| `somme` | `\sum` | `sum` | 90 |
| `Σ` (unicode) | `\sum` | `sum` | 100 |
| `∑` (n-ary sum) | `\sum` | `sum` | 100 |

Mélange canonique + FR + EN + unicode.

### 2. 4 slots positionnels (tous requis)

| Position | Slot | Sémantique |
|---|---|---|
| 1 | `var` | Variable d'itération (ex. `k`, `n`) |
| 2 | `from` | Borne basse (ex. `0`, `1`) |
| 3 | `to` | Borne haute (ex. `n`, `+oo`) |
| 4+ | `expression` | Terme général — multi-tokens via `ConcatArgsFrom` |

### 3. Rendu LaTeX

```
\sum_{<var>=<from>}^{<to>} <expression>
```

Le `=` entre var et from est **implicite** — l'user tape juste les args
séparés par espaces.

### 4. Helpers communs remontés dans `ArgListPatternBase` (P9b.1)

- `ConvertInfinityToken(text)` : `+oo`/`-oo`/`∞` → `+\infty`/`-\infty`/`\infty`
- `ConvertInfinityToUnicode(text)` : variante description popup
- `ConcatArgsFrom(args, startIdx, source)` : concat args en préservant whitespaces

Ces helpers étaient privés dans LimTemplate (P9a), promus en
`protected static` pour partage avec SumTemplate. LimTemplate refactor
pour les utiliser (= cleanup de la dup).

### 5. Description Unicode

```
Σ_{<var>=<from>}^{<to>} <expression>
```

Lisible en monospace popup. `▭` pour slots vides.

## Tradeoff & alternatives écartées

- **Slots avec syntaxe explicite `k=0`** (= user tape `Sum k=0 n k²`).
  Rejeté : casse la convention args-espace uniforme avec Lim. Le `=`
  reste implicite.
- **Promotion des helpers vers une utility class séparée** au lieu
  d'`ArgListPatternBase`. Rejeté : les helpers sont sémantiquement
  liés au pattern parsing args. La base les fournit naturellement.
- **`Σ` et `∑` mappés comme heads distincts** : valeur weight identique
  100 pour les deux car équivalence typographique (Σ Greek vs ∑ math
  symbol). Si l'user veut distinguer plus tard, ajouter à `Hints`.

## Conséquences

### Code touché

- **Nouveau** :
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/SumTemplate.cs` (~140 lignes)
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/SumTemplateTests.cs` (19 tests)
- **Modifié** :
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/ArgListPatternBase.cs` — ajout 3 helpers protected static (`ConcatArgsFrom`, `ConvertInfinityToken`, `ConvertInfinityToUnicode`)
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/LimTemplate.cs` — retrait des 3 helpers privés (utilise base)
  - `core-csharp/src/MathCursor.Core/Patterns/DefaultPatternRegistry.cs` — ajout `new SumTemplate()` (= 5 templates pilotes maintenant)

### Tests

- **Core** : 1140/1147 verts (post-P9a = 1121/1128). Delta : **+19 nouveaux verts**, 6 préexistants rouges idem.
- **Adapter** : 393/393 inchangé.

### API publique

- Nouveau type public : `MathCursor.Core.Patterns.Templates.SumTemplate`.
- 3 helpers promus protected static dans `ArgListPatternBase` :
  `ConcatArgsFrom`, `ConvertInfinityToken`, `ConvertInfinityToUnicode`.
  Sous-classes futures (IntegralTemplate, DerivativeTemplate) les
  réutiliseront.

### Régression UX

Aucune. Ajout pur. L'utilisateur peut maintenant taper :
- `Sum` → popup `\sum_{▭=▭}^{▭} ▭` (template complet)
- `Sum k 0 n k²` → popup et OMath `\sum_{k=0}^{n} k²` (complet)
- `Sum n 1 +oo 1/n²` → `\sum_{n=1}^{+\infty} 1/n²` (avec conversion infini)
- `∑ k 0 n k²` → idem (head unicode)

### Règles MC impactées

Aucune.

## Validation post-fix

```bash
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~SumTemplate"
# → 19/19 verts

# Lim toujours OK après refacto helpers
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~LimTemplate"
# → 18/18 verts

# Suite Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1140/1147 verts (6 préexistants rouges)
```

## Plan Patterns — état d'avancement

- [x] **P9a** — LimTemplate (commit `154e947`)
- [x] **P9b** — SumTemplate (cet ADR)
- [ ] **P9c** — IntegralTemplate (Int a b f(x) → \int_a^b f(x))
- [ ] **P9d** — DerivativeTemplate
- [ ] **P9e** — Migration YAML
