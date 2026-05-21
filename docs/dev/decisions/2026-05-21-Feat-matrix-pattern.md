# Feat — MatrixTemplate : 3 modes + désambig auto-layout (P9f)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Feat-yaml-pattern-specs.md`](2026-05-21-Feat-yaml-pattern-specs.md) — P9e (DSL YAML pour patterns simples)
- [`2026-05-21-Refactor-forall-belongs-arglist-convention.md`](2026-05-21-Refactor-forall-belongs-arglist-convention.md) — P5R (ArgListPatternBase)

## Citation acté

> « j'aimerai ajouter la possibilité de faire des matrices, mat arg arg arg et les args peuvent etre des expressions completes, les col row sont auto detectés avec le nombre d'arg passés. et desambig retourne » — utilisateur, 2026-05-21

Choix validés via AskUserQuestion :
- **Mode hybride** : espace = auto-detect, virgule/`;` = explicit
- **Diviseurs entiers** pour énumération layouts + détection `mat4x5`/`mat3x1` head paramétré
- **Head paramétré** mat<rows>x<cols> avec dim fixée, hint pour args manquants
- **Notation culture-aware** (= pmatrix FR, bmatrix US) via `RenderOptions.MatrixDelim`
- **Heads** : `mat` / `Mat` / `matrice` / `matrix`

## Contexte

Matrice est le premier pattern qui pousse les limites du système :

1. **N args variable** : `mat a b` (2 args) vs `mat a b c d e f` (6 args) vs
   `mat a b c d e f g h i` (9 args) — pas de slot count fixe.
2. **Args = expressions complètes** : `mat sin x, cos x` — chaque cell
   peut contenir des espaces (= besoin séparateur explicite).
3. **Désambig auto-layout** : pour N args sans séparateur, plusieurs
   interprétations possibles (1×4, 4×1, 2×2 pour 4 args).
4. **Mode head paramétré** : `mat3x4` (= dim figée dans le head).
5. **Notation culture-aware** : FR `\begin{pmatrix}` vs US `\begin{bmatrix}`.

Le DSL YAML actuel (P9e) ne supporte pas ces concepts. Donc MatrixTemplate
reste en C# custom, hérite d'`ArgListPatternBase` pour le head detection
+ helpers, mais override `TryMatchHead` et `Expand` complètement.

## Décision

### 1. Trois modes d'entrée utilisateur

#### Mode 1 — Auto-detect (multi-completion)

```
mat a b c d
```

ParseArgs whitespace standard → 4 args. Le template énumère tous les
couples (rows, cols) tels que rows × cols = 4 :
- 2×2 (carré exact)
- 1×4 (vecteur ligne)
- 4×1 (vecteur colonne)

Émet **3 PatternCompletion**, triées par "proximité au carré" :
2×2 en premier, puis 1×4 et 4×1. La popup affiche les 3 alternatives,
l'utilisateur choisit.

Tri : `|rows - cols|` ascendant. Pour 9 args → [3×3, 1×9, 9×1].
Pour nombre premier (5) → [1×5, 5×1].

#### Mode 2 — Séparateurs explicites

```
mat 1, 2 ; 3, 4
```

Virgule (`,`) sépare les colonnes d'une ligne. Point-virgule (`;`)
sépare les lignes. Convention MATLAB-like.

Avantage : permet des expressions avec espaces dans les cells
(`mat sin x, cos x ; tan x, cot x` = 2×2 avec sin x, cos x, tan x,
cot x).

Émet **1 PatternCompletion** (= dim figée). Lignes incomplètes paddées
avec null → carrés `\square` dans HintLatex.

#### Mode 3 — Head paramétré

```
mat3x4 a b c d e f g h i j k l
```

TryMatchHead détecte `mat` suivi tight de `<digits>x<digits>`. La
dimension est stockée dans les slots `explicit_rows` / `explicit_cols`.

Args suivants espace-séparés remplissent les cells. Si N args < rows
× cols → carrés pour les cells manquantes dans HintLatex.

Émet **1 PatternCompletion**. Pas de désambig (= dim explicite).

### 2. Détection de mode dans Expand

```csharp
public override IReadOnlyList<PatternCompletion> Expand(state, ctx)
{
    int? explicitRows = TryReadIntSlot(state, "explicit_rows");
    int? explicitCols = TryReadIntSlot(state, "explicit_cols");
    bool hasExplicitSep = sourceAfterHead.IndexOfAny(',', ';') >= 0;

    if (explicitRows.HasValue && explicitCols.HasValue)
        return ExpandExplicitDim(...);     // Mode 3
    if (hasExplicitSep)
        return ExpandExplicitSep(...);     // Mode 2
    return ExpandAutoDetect(...);          // Mode 1
}
```

### 3. Notation LaTeX culture-aware

`RenderOptions.MatrixDelim` ajoutée :

```csharp
public string MatrixDelim { get; set; } = ResolveMatrixDelimCultureDefault();

public static string ResolveMatrixDelimCultureDefault()
{
    var iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    return iso == "fr" ? "pmatrix" : "bmatrix";
}
```

Cohérent avec `MultSymbol` (`\times` FR vs `\cdot` US) déjà en place.

### 4. Mutation source normalisée

Tous les modes produisent une mutation **canonique** :

```
mat<rows>x<cols> <cell1> <cell2> ... <cellN>
```

Exemples :
- Mode 1 `mat a b c d` (2×2 retenu) → `mat2x2 a b c d`
- Mode 2 `mat 1, 2 ; 3, 4` → `mat2x2 1 2 3 4`
- Mode 3 `mat3x4 a b c` (incomplet) → `mat3x4 a b c` (cells manquantes
  omises)

Le pipeline lattice peut alors reconstruire un OMath cohérent depuis
la mutation.

### 5. CompletenessScore

- `mat` seul → 25 (head only)
- Auto-detect ou explicit avec N args remplis sur total = N
  → 25 + 75 × (filled/total)
- Si total = filled → 100

Score < 100 → IsIncomplete = true (cf. P5R+) → popup reste ouverte.

### 6. Heads supportés

| Head | Weight | Note |
|---|---|---|
| `mat` | 100 | raccourci ASCII canonique |
| `Mat` | 100 | majuscule = convention MathCursor (cf. Sum/Lim) |
| `matrice` | 90 | FR explicite |
| `matrix` | 85 | EN |

Tous mutés vers `mat` (= keyword canonique).

## Tradeoff & alternatives écartées

- **Tout en YAML** : rejeté. Le DSL YAML actuel ne supporte pas multi-
  completion via diviseurs, head paramétré avec suffix, ni séparateurs
  explicites custom. Étendre le DSL pour ce cas = mini-DSL programmable,
  sur-engineered pour un seul template.
- **Seulement vecteurs + carré** (au lieu de tous les diviseurs) :
  rejeté par l'utilisateur (« diviseurs entiers »). Math standard.
- **Au plus 4 options par poids** : rejeté pour la même raison. Tous
  les diviseurs.
- **Notation hardcoded pmatrix** : rejeté. Culture-aware via
  `RenderOptions` aligné sur `MultSymbol`.

## Conséquences

### Code touché

- **Nouveau** :
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/MatrixTemplate.cs` (~325 lignes)
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/MatrixTemplateTests.cs` (21 tests couvrant les 3 modes)
- **Modifié** :
  - `core-csharp/src/MathCursor.Core/RenderOptions.cs` — ajout `MatrixDelim` + `ResolveMatrixDelimCultureDefault`
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/ArgListPatternBase.cs` — `TryMatchHead` rendu `virtual` (pour permettre override head paramétré)
  - `core-csharp/src/MathCursor.Core/Patterns/DefaultPatternRegistry.cs` — ajout `new MatrixTemplate()` (= 9 templates pilotes total)

### Tests

- **Core** : 1195/1202 verts (post-P9e = 1174/1181). Delta : **+21 nouveaux verts**, 6 préexistants rouges idem.
- **Adapter** : 393/393 inchangé.

### API publique

- Nouveau type public : `MatrixTemplate`.
- `RenderOptions.MatrixDelim` (nouveau, default culture-aware).
- `ArgListPatternBase.TryMatchHead` devient `virtual` (= peut être
  overridé par les sous-classes, comme MatrixTemplate le fait pour le
  head paramétré).

### Régression UX

Aucune. Ajout pur. L'utilisateur Word peut maintenant taper :
- `mat a b c d` → popup propose 3 alts (2×2, 1×4, 4×1)
- `mat 1, 2 ; 3, 4` → 2×2 explicite
- `mat3x4 a b c d e f g h i j k l` → 3×4 explicite (12 cells)
- `mat sin x, cos x ; tan x, cot x` → 2×2 avec expressions complexes

### Règles MC impactées

Aucune.

## Validation post-fix

```bash
# Tests Matrix
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~MatrixTemplate"
# → 21/21 verts

# Suite Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1195/1202 verts (6 préexistants rouges)

# Adapter
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts
```

## Plan Patterns — état d'avancement (Chantier 6)

- [x] **P9e** — Pattern specs en YAML + auto-discovery (commit `cf1fbad`)
- [x] **P9f** — MatrixTemplate avec 3 modes + désambig auto-layout (cet ADR)
- [ ] **P8** — Validation manuelle PAP-friendly via `/build-iss` + Word
- [ ] **P10+** — Vecteurs (notation flèche), Déterminant, Trace, Norme... peuvent réutiliser MatrixTemplate ou créer un VectorTemplate similaire en C# custom.

9 templates pilotes actifs : forall-belongs, ensemble, interval-union,
matrix (C#) + lim, sum, integral, derivative, probability (YAML).
