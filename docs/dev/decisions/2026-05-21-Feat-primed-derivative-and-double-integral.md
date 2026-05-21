# Feat — PrimedDerivative (.cs) + DoubleIntegral (YAML) — P9g + P9h

**Date :** 2026-05-21
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Feat-yaml-pattern-specs.md`](2026-05-21-Feat-yaml-pattern-specs.md) — P9e (DSL YAML)
- [`2026-05-21-Feat-matrix-pattern.md`](2026-05-21-Feat-matrix-pattern.md) — P9f (modèle pattern custom)

## Citation acté

> « pendant que je teste tu peux me matcher les derivée premiere et seconde ? f' et f'' f" ? ainsi que integrale doubles ? » — utilisateur, 2026-05-21

## Contexte

L'utilisateur teste manuellement P9f en parallèle et demande 2 nouveaux
patterns rapides :
- **Dérivées primées Lagrange** (`f'`, `f''`, `f"`) — postfix sur identifier
- **Intégrales doubles** (`iint`, `∬`) — variante 2D de Integral

Le 1er cas (postfix) ne rentre pas dans le DSL YAML actuel (= heads
préfixes uniquement). Le 2e cas est typiquement le moule "args espace"
→ YAML pur.

Cette PR valide les **deux voies** :
- Pattern custom complexe (postfix) → `.cs`
- Pattern simple data-driven → 1 fichier YAML

## Décision

### P9g — PrimedDerivativeTemplate (.cs custom)

| Aspect | Détail |
|---|---|
| Approche | Postfix scan sur identifier 1-lettre + 1-4 marqueurs `'` ou `"` |
| Heads literals | **Aucun** — pas de keyword head fixe |
| Conversion | `"` (guillemet ASCII) → `''` (= 2 apostrophes canoniques) |
| Args optionnels | `f'(x)` — `(` tight après les primes |
| Limite | 4 primes max (= `f''''`) |
| Description Unicode | `f′`, `f″`, `f‴`, `f⁗` |
| Rendu LaTeX | `f'`, `f''`, `f'''`, `f''''` (= natif LaTeX) |
| Mutation source | Normalise `"` → `''` canonique |

Exemples :
- `f'` → `f'`
- `f''` → `f''`
- `f"` → `f''` (= conversion auto guillemet)
- `f'(x)` → `f'(x)` (avec args tight)
- `f''(2x+1)` → `f''(2x+1)` (expression complexe en arg)

### P9h — DoubleIntegralTemplate (YAML pur)

| Aspect | Détail |
|---|---|
| Fichier | `data/patterns/double_integral.yaml` (embedded) |
| Heads | `iint` / `Iint` / `intint` / `∬` (4 variants) |
| 3 slots positionnels | `var1` / `var2` / `expression` (multi-token) |
| Rendu LaTeX | `\iint <expression> \, d<var1> \, d<var2>` |
| Description Unicode | `∬ <expr> d<var1> d<var2>` |
| Mutation source | `iint <var1> <var2> <expression>` (canonique) |

Exemples :
- `iint x y f(x,y)` → `\iint f(x,y) \, dx \, dy`
- `∬ u v f(u,v)+g(u,v)` → `\iint f(u,v)+g(u,v) \, du \, dv`
- `Iint x y sin(x*y)` → `\iint sin(x*y) \, dx \, dy`

**Pattern créé entièrement via YAML, sans aucun .cs.** Validation que
le DSL P9e supporte les patterns "args espace" standard.

## Tradeoff & alternatives écartées

### P9g (Primed)

- **Postfix via DSL YAML** : rejeté. Le DSL actuel ne supporte que les
  heads préfixes literals. Étendre serait un mini-DSL programmable
  sur-engineered.
- **Variante Leibniz `\frac{df}{dx}`** en multi-completion : rejeté
  pour P9g. L'user a demandé "matcher" les primes — convention Lagrange.
  Leibniz est un autre pattern à part (= DerivativeTemplate déjà
  existant via YAML).
- **Limite > 4 primes** : rejeté. Au-delà, l'user utilise `f^{(5)}`
  notation Lagrange étendue. Pas dans le scope actuel.

### P9h (DoubleIntegral)

- **Avec bornes (= 6 slots vars + bornes)** : rejeté pour P9h. Lourd
  syntaxiquement (`iint x y a b c d f(x,y)`). Le user peut ajouter les
  bornes manuellement après commit. P10+ pourra ajouter
  `BoundedDoubleIntegralTemplate` si demande.
- **Avec domaine D explicite** : rejeté. Demande sub-pattern domain
  (= compositionnel). Trop complexe pour P9h minimal.

## Conséquences

### Code touché

- **Nouveau** :
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/PrimedDerivativeTemplate.cs` (~175 lignes)
  - `data/patterns/double_integral.yaml`
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/PrimedDerivativeTemplateTests.cs` (21 tests)
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/DoubleIntegralYamlTests.cs` (12 tests)
- **Modifié** :
  - `core-csharp/src/MathCursor.Core/Patterns/DefaultPatternRegistry.cs` — ajout `new PrimedDerivativeTemplate()` (= 10 templates pilotes : 5 .cs + 5 YAML)

### Tests

- **Core** : 1228/1235 verts (post-P9f = 1195/1202). Delta : **+33 nouveaux verts** (21 Primed + 12 DoubleIntegral), 6 préexistants rouges idem.
- **Adapter** : 393/393 inchangé.

### API publique

- Nouveau type public : `PrimedDerivativeTemplate`.
- `double_integral` YAML auto-discovered via wildcard MSBuild
  (`data/patterns/*.yaml`) — aucun changement code requis.

### Régression UX

Aucune. Ajout pur. L'utilisateur Word peut maintenant taper :
- `f'`, `f''`, `f"` → notations primées
- `f'(x)`, `f"(2x+1)` → primées avec args
- `iint x y f(x,y)` → intégrale double
- `∬ u v expr` → idem unicode

### Règles MC impactées

Aucune.

## Validation post-fix

```bash
# Tests Primed + DoubleIntegral
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~PrimedDerivative|FullyQualifiedName~DoubleIntegralYaml"
# → 33/33 verts (21 + 12)

# Suite Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1228/1235 verts (6 préexistants rouges)

# Validation manuelle dans Word :
# 1. Taper f'(x) + Ctrl+Espace → popup f'(x)
# 2. Taper f''(2x+1) + Ctrl+Espace → popup f''(2x+1)
# 3. Taper f"(x) + Ctrl+Espace → popup f''(x) (= guillemet converti)
# 4. Taper iint x y f(x,y) + Ctrl+Espace → popup \iint f(x,y) \, dx \, dy
```

## Plan Patterns — état d'avancement

- [x] **P9e** — DSL YAML + auto-discovery (commit `cf1fbad`)
- [x] **P9f** — MatrixTemplate (commit `40d4aa3`)
- [x] **Fix matrice WPF popup** (commit `1485a86`)
- [x] **P9g** — PrimedDerivativeTemplate (cet ADR, .cs)
- [x] **P9h** — DoubleIntegralTemplate (cet ADR, YAML)
- [ ] **P8** — Validation manuelle PAP-friendly via `/build-iss` (en cours)

**10 templates pilotes actifs** : 5 .cs custom (forall-belongs,
ensemble, interval-union, matrix, primed-derivative) + 5 YAML
data-driven (lim, sum, integral, derivative, probability,
double_integral — soit 6 YAML).
