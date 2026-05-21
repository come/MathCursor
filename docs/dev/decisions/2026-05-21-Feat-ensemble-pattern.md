# Feat — EnsembleTemplate : premier vrai IPatternTemplate (P3)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Meta-pattern-templates-vs-ambig-closed.md`](2026-05-21-Meta-pattern-templates-vs-ambig-closed.md) — ADR de cadrage, P3 du plan
- [`2026-05-21-Refactor-pattern-pipeline-skeleton.md`](2026-05-21-Refactor-pattern-pipeline-skeleton.md) — P2, squelette Patterns/ (commit `023e03d`)
- `CanonicalSetLettersScanner` (legacy ambig closed) — sera retiré en P6, comportement remplacé par ce template

## Citation acté

> « commit p2 puis enchainer » — utilisateur, 2026-05-21
> (valide l'enchaînement P3 directement après commit P2)
>
> Plan P3 contenu validé en amont via « ok je valide » (plan P1) et
> « ok on valide tout ca » (plan P2 + position globale P3-P5 = pilote).

## Contexte

P3 du plan d'organisation cadré par l'ADR de cadrage. Premier vrai
template inscrit dans le système Patterns. Couvre les **leaf cases**
de l'ensemble (lettres canoniques + modifiers), laisse les intervals
pour P4 (`IntervalUnionTemplate`).

Ce template est :

- **Autonome** : peut être appelé directement (utilité hypothétique : un
  user qui tape juste `R*` sans contexte).
- **Compositionnel** : sera consommé comme sub-pattern par
  `ForallBelongsTemplate` en P5 via `PatternRefSlot("ensemble")`.

À ce stade, aucun caller ne l'utilise — il vit dans
`MathCursor.Core/Patterns/Templates/` sans intégration `ZoneResolver`
ni `PatternRegistry` global. P5 et P7 brancheront l'usage.

## Décision

### 1. Heads supportés

Lettres canoniques `R`, `N`, `Z`, `Q`, `C` avec modifiers optionnels
(`*`, `+`, `-`) tight derrière, 1 ou 2 max.

Exemples : `R`, `R*`, `R+`, `R-`, `R+*`, `R*+`, `N*`, `Z*`, `Q*`, `C`.

Heads à venir P4 : `[` (delegate à `IntervalUnionTemplate`).

### 2. Convention alignée sur le scanner legacy

`CanonicalSetLettersScanner` (à retirer P6) utilise déjà :
- Word boundary à gauche (`i == 0 || !IsLetter(source[i-1])`).
- Délimiteur terminal à droite après modifiers (whitespace, `,`, `;`,
  `.`, `)`, `]`, `}`, ou EOF).
- Limit 2 modifiers max (cf. `PreprocessCanonicalSetModifiers`).

`EnsembleTemplate` reprend exactement ces règles pour rester
behavior-compatible avec ce qui existe (le user ne verra aucune
différence en P6 quand on retire le scanner).

### 3. SourceMutation alignée sur le préprocesseur

Le `PatternCompletion` émis transforme `R*` → `bbR*`. Le préprocesseur
existant `ZoneResolver.PreprocessCanonicalSetModifiers` produit la
même substitution avant le pipeline lattice. Donc :

- Si l'utilisateur valide la complétion : la mutation est appliquée à
  la source brute, le préprocesseur la voit déjà mutée (no-op), le
  pipeline rend `\mathbb{R}^*`.
- En P5 quand `ForallBelongsTemplate` délègue à `EnsembleTemplate`, la
  mutation reste précise à l'offset source du sub-pattern.

### 4. Rendu LaTeX

| Source | PreviewLatex | Description (Unicode) |
|---|---|---|
| `R` | `\mathbb{R}` | `ℝ` |
| `N` | `\mathbb{N}` | `ℕ` |
| `Z` | `\mathbb{Z}` | `ℤ` |
| `Q` | `\mathbb{Q}` | `ℚ` |
| `C` | `\mathbb{C}` | `ℂ` |
| `R*` | `\mathbb{R}^*` | `ℝ*` |
| `R+` | `\mathbb{R}^+` | `ℝ+` |
| `R+*` | `\mathbb{R}^{+*}` | `ℝ+*` |

Description Unicode = lisibilité immédiate dans la popup.
`HintLatex` identique au `PreviewLatex` (pas de slot vide → pas de
carré à afficher).

### 5. Pas de slot, pas d'`Expand` itératif

Le template est **leaf** : `TryMatchHead` consomme directement
l'intégralité du match (head + modifiers) et `Expand` produit une
unique `PatternCompletion` complète (CompletenessScore = 100). Pas de
slot à remplir progressivement.

### 6. Helper `EmptySlots` interne

Singleton `IReadOnlyDictionary<string, SlotValue>` vide partagé pour
éviter les allocations de dictionnaires vides à chaque `TryMatchHead`
sur un template leaf. Visible dans `core-csharp/src/MathCursor.Core/Patterns/EmptySlots.cs`,
internal.

## Tradeoff & alternatives écartées

Évaluation sur qualité + perf + extensibilité uniquement.

- **Inclure `[...]` (intervals) dans EnsembleTemplate**. Rejeté pour
  P3 : couplage avec `IntervalUnionTemplate` qui n'existe pas encore.
  P4 ajoutera la délégation via `PatternRefSlot("interval-union")`.

- **Réutiliser `AlternativeGenerator.ScanCanonicalSetLetters` en
  appelant le helper depuis le template**. Rejeté : le helper est
  internal et lié à la pipeline ambig (besoin du `consumed[]` + bornes
  `topLatex`). Le template a un contrat différent (PatternMatch +
  PatternCompletion) — DRY violation acceptable pour ~5 lignes.

- **Source mutation = `\mathbb{R}^*` directement (skip le `bb` prefix)**.
  Rejeté : `bb` est ce que le `Vocabulary.cs` + `LatexRenderer`
  attendent. Casser cette convention forcerait un chemin parallèle
  parser/renderer pour les ensembles, dégrade la maintenabilité.

- **Préfixer le template avec Order < 0 pour qu'il tourne avant les
  scanners ambig closed**. Hors scope P3 : aucune intégration ZoneResolver
  encore. Décision sur Order à prendre quand le pipeline pattern est
  branché (P5+).

## Conséquences

### Code touché

- **Nouveau** :
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/EnsembleTemplate.cs` (~110 lignes)
  - `core-csharp/src/MathCursor.Core/Patterns/EmptySlots.cs` (~15 lignes, helper interne)
- **Nouveau tests** :
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/EnsembleTemplateTests.cs` (33 tests)
- **Modifié** : aucun fichier de production existant. P3 est ajout pur.

### Tests

- **Core** : 1029/1036 verts (post-P2 = 996/1003). Delta : **+33 nouveaux verts**, 0 régression, 6 préexistants rouges idem.
- **Adapter** : 393/393 inchangé.

### API publique

- Nouveau type public : `MathCursor.Core.Patterns.Templates.EnsembleTemplate` (implémente `IPatternTemplate`).
- Aucun caller existant n'est cassé.

### Règles MC impactées

- **MC0006** : aucun nouveau hit. Le template émet une `SourceMutation` (modèle natif) et pas un splice latex.
- **MC0001 / MC0009** : aucun impact.

### Performance

- `TryMatchHead` : O(n) sur `source.Length`. Scan linéaire avec early-exit dès qu'une lettre canonique + boundary + délim est trouvée.
- `Expand` : O(1) après matching.
- Allocations : 1 `PatternMatch` + 1 `PatternCompletion` + 1 `SourceMutation` + 1 string `replacement` par appel — tous nécessaires. `EmptySlots.Instance` partagé, pas d'allocation dictionnaire.

## Validation post-fix

```bash
# Build
dotnet build core-csharp/src/MathCursor.Core/MathCursor.Core.csproj
# → 8 warnings préexistants, 0 erreur

# Tests EnsembleTemplate
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~EnsembleTemplate"
# → 33/33 verts

# Suite Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1029/1036 verts (6 préexistants rouges)

# Adapter
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts
```

## Plan Patterns — état d'avancement (ROADMAP Chantier 6)

- [x] **P0** — Attendre commit stable du WIP popup (commits `817c4d3`/`8477602`/`538f61e`)
- [x] **P1** — Caret-aware `ZoneResolver` + `CaretLocator` (commit `607a6f8`)
- [x] **P2** — Squelette `Patterns/` (commit `023e03d`)
- [x] **P3** — `EnsembleTemplate` (cet ADR)
- [ ] **P4** — `IntervalUnionTemplate` (heads `[`, opérateurs `U`/`union`/`inter`, récursif)
- [ ] **P5** — `ForallBelongsTemplate` (head V/E, slot var, slot domain optionnel ref `ensemble`)
- [ ] **P6** — Retrait scanners V + canonical-set legacy
- [ ] **P7** — Popup consomme `PatternCompletion` + carrés
- [ ] **P8** — Test bout-en-bout dans Word
- [ ] **P9+** — `LimTemplate`, `SumTemplate`, etc.
