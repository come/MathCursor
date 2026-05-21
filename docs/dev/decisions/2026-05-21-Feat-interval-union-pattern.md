# Feat — IntervalUnionTemplate : pattern récursif avec slots (P4)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Meta-pattern-templates-vs-ambig-closed.md`](2026-05-21-Meta-pattern-templates-vs-ambig-closed.md) — ADR de cadrage, P4 du plan
- [`2026-05-21-Refactor-pattern-pipeline-skeleton.md`](2026-05-21-Refactor-pattern-pipeline-skeleton.md) — P2
- [`2026-05-21-Feat-ensemble-pattern.md`](2026-05-21-Feat-ensemble-pattern.md) — P3 (commit `7af2b37`)

## Citation acté

> « on continue » — utilisateur, 2026-05-21
> (validation pour enchaîner P4 directement après P3)
>
> « fait ce que tu recommande, je valide » — utilisateur, 2026-05-21
> (validation des 3 points ouverts : P4 isolé sans branchement EnsembleTemplate, convention boundary gauche pour `(` head, `\square` LaTeX pour HintLatex)

## Contexte

P4 du plan d'organisation cadré par l'ADR de cadrage. Deuxième template
concret, premier à utiliser :

- **Plusieurs slots** : `leftBracket`, `lo`, `hi`, `rightBracket`,
  + optionnels `operator` et `tail`
- **Slot récursif** : `tail` = `FilledSlotSubPattern(PatternMatch{TemplateId="interval-union"})`
- **États partiels** : un interval peut être incomplet (juste `[`, `[0,`,
  `[0,1`, `[0,1] U` sans suite, etc.) — la complétion expose `HintLatex`
  avec `\square` pour les slots vides
- **Pas de SourceMutation** : la source `[0,1]U[3,4]` est déjà parsable
  par le pipeline lattice existant. Le template décrit la forme pour la
  popup, pas pour muter la source

## Décision

### 1. Heads et boundary

- `[` (closed left bracket) : **toujours accepté** (pas d'ambig courante
  avec un caller existant).
- `(` (open left bracket) : accepté **seulement si** le caractère
  précédent n'est ni lettre ni digit (sinon function call `f(...)` ou
  indice numérique `2(...)`).

Aligné sur la convention de `ScanFunctionTypicalWithCommaCoords` legacy
qui distingue function call d'interval par identifier fonction devant.

### 2. Slots dans `PatternMatch.Slots`

| Slot | Type | Présent quand |
|---|---|---|
| `leftBracket` | `FilledSlotAtom("[")` ou `("(")` | toujours après `TryMatchHead` |
| `lo` | `FilledSlotAtom(text, start, end)` ou `EmptySlot` | rempli si borne basse parsable |
| `hi` | idem | rempli si borne haute parsable |
| `rightBracket` | `FilledSlotAtom("]")` ou `(")")` ou `EmptySlot` | rempli si fermeture trouvée |
| `operator` | `FilledSlotAtom` portant `U`/`∪`/`union`/`inter`/`∩` | ajouté si opérateur trouvé après le rightBracket |
| `tail` | `FilledSlotSubPattern(sub)` | ajouté si sub-interval-union trouvé après l'opérateur |

### 3. Bornes acceptées (P4 minimal)

- Nombres : `0`, `42`, `3.14`, `0.5`
- Identifiers : `a`, `x`, `pi`
- Symboles infinis : `+oo`, `-oo`, `+∞`, `-∞`, `∞`
- Sign devant identifier (`+x`) : **rejeté** (P9+ pourrait l'accepter
  comme expression complète via `ExpressionSlot`)

### 4. Opérateurs reconnus

| Token source | Canonical (slot) | LaTeX rendu | Unicode (description) |
|---|---|---|---|
| `U` (seul, boundary droite) | `U` | `\cup` | `∪` |
| `∪` | `∪` | `\cup` | `∪` |
| `union` (boundary droite) | `union` | `\cup` | `∪` |
| `∩` | `∩` | `\cap` | `∩` |
| `inter` (boundary droite) | `inter` | `\cap` | `∩` |

### 5. Rendu Preview vs Hint

| Cas | PreviewLatex | HintLatex |
|---|---|---|
| `[0,1]` complet | `\left[0,1\right]` | identique |
| `[` seul | `\left[,\right]` (lo/hi vides → "") | `\left[\square,\square\right]` |
| `[0,` (hi vide) | `\left[0,\right]` | `\left[0,\square\right]` |
| `[0,1]U` (operator sans tail) | `\left[0,1\right]` (op caché) | `\left[0,1\right] \cup \left[\square,\square\right]` |
| `[0,1]U[3,4]` chaîne | `\left[0,1\right] \cup \left[3,4\right]` | identique |

Le `rightBracket` est **toujours rendu** (valeur ou miroir du `leftBracket`)
pour préserver la structure visuelle même en état partiel.

L'opérateur en preview est **caché si tail absent** (un `\cup` orphelin
serait visuellement faux).

### 6. Description Unicode pour la popup

`[0,1]∪[3,4]` (concise, lisible), avec `▭` pour les slots vides (carré
visuel léger), `∪`/`∩` pour les opérateurs.

### 7. CompletenessScore

Calcul pondéré récursif :
- Interval courant : `filled / 4 * 100` (4 slots fixes)
- Si tail présent : `(courant * 70 + sub * 30) / 100`

Permet à la popup de trier ou colorer les complétions selon leur degré
de finition.

## Tradeoff & alternatives écartées

Évaluation sur qualité + perf + extensibilité uniquement.

- **Représentation chaîne en `List<IntervalSegment>` plutôt qu'en
  récursion `tail`**. Rejeté : casse le contrat plat `Slots: Dictionary`
  du P2, force une dérivée de `PatternMatch`. Le coût d'une récursion
  ≤ 3 niveaux (chaînes typiques) est négligeable.

- **Délégation au `LatticeEngine.ConvertWithAmbiguity` pour parser
  `[0,1]U[3,4]` puis lire l'AST**. Rejeté : couple `IPatternTemplate`
  à l'engine, init coûteux par test, drift potentiel si Parser change.
  La duplication ~50 lignes de parsing minimal est largement amortie
  par l'autonomie testable.

- **Émettre une `SourceMutation` cosmétique** (normaliser espaces).
  Rejeté : surface API, peu de valeur. La source brute est déjà
  parsable par le pipeline existant.

- **Sign devant identifier `(+x, -x)` accepté comme borne**. Rejeté
  pour P4 : sémantique d'expression arithmétique pas dans le scope
  minimal. P9+ via `ExpressionSlot` qui invoque l'engine sur le sub-span.

- **Détecter et accepter `]a,b[` (notation française inversée)**.
  Rejeté pour P4 : non observé dans le corpus FR du projet, peut être
  ajouté plus tard sans changer le contrat (juste accepter `]` et `[`
  comme leftBracket aussi).

## Conséquences

### Code touché

- **Nouveau** :
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/IntervalUnionTemplate.cs` (~310 lignes)
- **Nouveau tests** :
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/IntervalUnionTemplateTests.cs` (32 tests)
- **Modifié** : aucun fichier de production existant. P4 est ajout pur.

### Tests

- **Core** : 1061/1068 verts (post-P3 = 1029/1036). Delta : **+32 nouveaux verts**, 0 régression, 6 préexistants rouges idem.
- **Adapter** : 393/393 inchangé.

### API publique

- Nouveau type public : `MathCursor.Core.Patterns.Templates.IntervalUnionTemplate` (implémente `IPatternTemplate`).
- Aucun caller existant n'est cassé.

### Règles MC impactées

- **MC0006** : aucun nouveau hit. Le template n'émet ni `SourceMutation` ni splice.
- **MC0001 / MC0009** : aucun impact.

### Performance

- `TryMatchHead` : O(n) sur source, early-exit dès qu'une bracket valide est trouvée.
- `Expand` / `ParseFromState` : O(n) sur le sub-span de la chaîne d'intervals. Récursion `tail` proportionnelle au nombre d'intervals (≤ 3-5 typique).
- Allocations : 1 `PatternMatch` par niveau de récursion + 1 `PatternCompletion` final. Tous nécessaires.

## Validation post-fix

```bash
# Build
dotnet build core-csharp/src/MathCursor.Core/MathCursor.Core.csproj
# → 8 warnings préexistants, 0 erreur

# Tests IntervalUnionTemplate
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~IntervalUnionTemplate"
# → 32/32 verts

# Suite Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1061/1068 verts (6 préexistants rouges)

# Adapter
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts
```

## Plan Patterns — état d'avancement (ROADMAP Chantier 6)

- [x] **P0** — Attendre commit stable du WIP popup (commits `817c4d3`/`8477602`/`538f61e`)
- [x] **P1** — Caret-aware `ZoneResolver` + `CaretLocator` (commit `607a6f8`)
- [x] **P2** — Squelette `Patterns/` (commit `023e03d`)
- [x] **P3** — `EnsembleTemplate` (commit `7af2b37`)
- [x] **P4** — `IntervalUnionTemplate` (cet ADR)
- [ ] **P5** — `ForallBelongsTemplate` (head V/E, slot var, slot domain optionnel ref `ensemble`)
- [ ] **P6** — Retrait scanners V + canonical-set legacy
- [ ] **P7** — Popup consomme `PatternCompletion` + carrés
- [ ] **P8** — Test bout-en-bout dans Word
- [ ] **P9+** — `LimTemplate`, `SumTemplate`, etc.

**Note P4.5** (envisagé puis annulé pour micro-scope) : ajout d'un
head `[` à `EnsembleTemplate` qui délègue à `IntervalUnionTemplate`
via `PatternRefSlot("interval-union")`. Repoussé à P5 où la
composition parent→enfant sera mise en place de bout en bout.
