# Feat — LimTemplate : pattern limite avec 3 slots positionnels (P9a)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Refactor-forall-belongs-arglist-convention.md`](2026-05-21-Refactor-forall-belongs-arglist-convention.md) — P5R (ArgListPatternBase)
- [`2026-05-21-Feat-pattern-trailing-hints-and-isincomplete.md`](2026-05-21-Feat-pattern-trailing-hints-and-isincomplete.md) — P5R+ (carrés + IsIncomplete)

## Citation acté

> « lance p9 » — utilisateur, 2026-05-21
> (validation pour démarrer le chantier P9+ avec le premier template : Lim)

## Contexte

P5R a posé `ArgListPatternBase` abstrait pour factoriser la convention
"head + args espace" entre patterns. P9a livre le premier template
non-forall qui en hérite : `LimTemplate`.

Lim est emblématique :
- 3 slots positionnels (vs slot var-list + domain optionnel pour forall)
- Conversion typographique d'infini (+oo / -oo / ∞)
- Forme idiomatique très visible : `\lim_{x \to 0} f(x)`

Valide le moule `ArgListPatternBase` sur un cas différent de forall, et
prépare le terrain pour Sum/Int/Dérivée (P9b/c/d).

## Décision

### 1. Heads supportés

| Head | LatexSymbol | Mutation | Weight |
|---|---|---|---|
| `Lim` | `\lim` | `lim` | 100 |
| `lim` | `\lim` | `lim` | 95 |

Le head canonique en MathCursor est `Lim` (majuscule, comme `V` pour
forall, `Sum` futur). La minuscule `lim` est aussi acceptée car
convention LaTeX standard.

### 2. 3 slots positionnels (tous requis)

| Position | Slot | Sémantique |
|---|---|---|
| 1 | `var` | Variable de la limite (ex. `x`, `n`) |
| 2 | `limit` | Valeur vers laquelle var tend (ex. `0`, `+oo`, `a`) |
| 3+ | `expression` | Expression à laquelle on applique la limite — peut être multi-tokens (concat depuis arg[2..]) |

Conséquence : `Lim x 0 f x` → var=`x`, limit=`0`, expression=`"f x"` (= 2 tokens concatenés). Permet à l'user de taper des expressions sans devoir mettre des parenthèses autour.

### 3. Rendu LaTeX

```
\lim_{<var> \to <limit>} <expression>
```

Slots vides = `\square` dans HintLatex (= popup), pas dans PreviewLatex
(= commit OMath final propre).

### 4. Conversions infini

`limit` peut être un token raccourci pour l'infini :

| Token source | Rendu LaTeX |
|---|---|
| `oo` | `\infty` |
| `+oo` | `+\infty` |
| `-oo` | `-\infty` |
| `infini` | `\infty` |
| `+infini` | `+\infty` |
| `-infini` | `-\infty` |
| `∞` (unicode) | `\infty` |
| `+∞` | `+\infty` |
| `-∞` | `-\infty` |

Conversion locale au template (= pas via Vocabulary lattice). Si l'user
tape un autre token (ex. `a`), il est rendu littéralement.

### 5. Mutation source canonique

`Lim x 0 f(x)` → mutation `(0, len, "lim x 0 f(x)")`. Le head `Lim`
devient `lim` (= keyword Vocabulary canonique). Le reste préservé.

Le pipeline lattice avec la source mutée rendra correctement le LaTeX
final (= `\lim_{x \to 0} f(x)`).

### 6. CompletenessScore progressif

| Args remplis | Score |
|---|---|
| 0 (head seul) | 25 |
| 1 (var) | 50 |
| 2 (var + limit) | 75 |
| 3+ (complet) | 100 |

Cohérent avec P5R+ — score < 100 → IsIncomplete = true (popup reste
ouverte).

## Tradeoff & alternatives écartées

- **Slots requis vs domain optionnel** : pour Lim, tous les slots
  ont un sens math obligatoire. Pas de cas "Lim partiel valide". Donc
  positions strictes, hint `\square` permanent.
- **Convention args sans concat** : si on n'avait pas la concat
  expression, `Lim x 0 f x` produirait expression="f" et "x" en arg
  excédent ignoré. Rejeté : `f x` ou `2 sin(x)` doivent être acceptés
  sans nécessiter `(f x)`.
- **Conversion infini via Vocabulary** : appeler `_engine.Convert` pour
  rendre les tokens limit. Rejeté : couplage à LatticeEngine, allocation
  coûteuse à chaque Expand. Conversion locale au template = O(1).
- **Heads supplémentaires `limite`/`Limite`** : rejetés P9a. Si l'user
  demande, ajout 1 ligne dans `_variants`. YAGNI.

## Conséquences

### Code touché

- **Nouveau** :
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/LimTemplate.cs` (~180 lignes)
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/LimTemplateTests.cs` (18 tests)
- **Modifié** :
  - `core-csharp/src/MathCursor.Core/Patterns/DefaultPatternRegistry.cs` —
    ajout `new LimTemplate()` à `Build()` et `BuildBoth()` (4 templates
    pilotes maintenant : forall-belongs, ensemble, interval-union, lim)

### Tests

- **Core** : 1121/1128 verts (post-P5R+ = 1103/1110). Delta : **+18 nouveaux verts**, 6 préexistants rouges idem.
- **Adapter** : 393/393 inchangé (LimTemplate vit dans Core ; l'adapter consomme automatiquement via `DefaultPatternRegistry.BuildBoth()` qui inclut désormais Lim).

### API publique

- Nouveau type public : `MathCursor.Core.Patterns.Templates.LimTemplate` (hérite d'`ArgListPatternBase`).
- `DefaultPatternRegistry.Build()` et `BuildBoth()` retournent maintenant 4 templates au lieu de 3.

### Régression UX

Aucune. Ajout pur. L'utilisateur Word peut maintenant taper :
- `Lim` → popup avec `\lim_{▭ \to ▭} ▭` (hint template)
- `Lim x` → `\lim_{x \to ▭} ▭`
- `Lim x 0` → `\lim_{x \to 0} ▭`
- `Lim x 0 f(x)` → `\lim_{x \to 0} f(x)` (complet)
- `Lim x +oo 1/x` → `\lim_{x \to +\infty} 1/x`

### Règles MC impactées

Aucune.

## Validation post-fix

```bash
# Tests Lim
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~LimTemplate"
# → 18/18 verts

# Suite Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1121/1128 verts (6 préexistants rouges)

# Adapter
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts

# Validation manuelle en P8 via /build-iss :
# 1. Taper "Lim" + Ctrl+Espace
# 2. Vérifier popup affiche `\lim_{▭ \to ▭} ▭`
# 3. Taper progressivement "Lim x 0 f(x)" + Ctrl+Espace
# 4. Vérifier popup affiche `\lim_{x \to 0} f(x)`
# 5. Enter → OMath inséré (sans carrés)
```

## Plan Patterns — état d'avancement (ROADMAP Chantier 6)

- [x] **P8 → P9a** — `LimTemplate` (cet ADR) ✨
- [ ] **P9b** — `SumTemplate` (`Sum k 0 n k²` → `\sum_{k=0}^{n} k²`)
- [ ] **P9c** — `IntegralTemplate` (`Int 0 1 f(x)` → `\int_0^1 f(x) dx`)
- [ ] **P9d** — `DerivativeTemplate` (`Derive f x` → `\frac{df}{dx}`)
- [ ] **P9e** — Migration YAML pour patterns triviaux
- [ ] **P8** — Validation manuelle PAP-friendly via `/build-iss`
