# Feat — Vecteur/point + coordonnées au clavier (`u(1, 2)` / `u (1 2)` / `A(1, 2)`)

**Date :** 2026-04-29
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Reconnaître le pattern lycée `<ident>(...)` ou `<ident> (...)` où l'identifiant
est 1 ou 2 lettres et les parens contiennent 2 ou 3 cellules avec séparateur
interne homogène (espace = layout colonne, virgule = layout ligne).

| Saisie | Sortie LaTeX |
|--------|--------------|
| `u (1 2)` ou `u(1 2)` | `\vec{u} \begin{pmatrix} 1 \\ 2 \end{pmatrix}` |
| `u (1 2 3)` | `\vec{u} \begin{pmatrix} 1 \\ 2 \\ 3 \end{pmatrix}` |
| `u(1, 2)` ou `u (1, 2)` | `\vec{u}(1, 2)` |
| `u(1, 2, 3)` | `\vec{u}(1, 2, 3)` |
| `A(1, 2)` ou `A(1,2)` | `A(1, 2)` (point — pas de `\vec`) |
| `A (1 2)` | `A \begin{pmatrix} 1 \\ 2 \end{pmatrix}` |
| `AB (3 -1)` | `\vec{AB} \begin{pmatrix} 3 \\ -1 \end{pmatrix}` |
| `AB(3, -1)` | `\vec{AB}(3, -1)` |
| `OM (x y z)` | `\vec{OM} \begin{pmatrix} x \\ y \\ z \end{pmatrix}` |
| `v(-1 3)` | `\vec{v} \begin{pmatrix} -1 \\ 3 \end{pmatrix}` |

**Décoration `\vec{...}` :**

| Identifiant | Décoration |
|-------------|------------|
| 1 minuscule (`u`, `v`, `w`, …) | `\vec{u}` |
| 2 lettres (`AB`, `OM`, `MN`, …) | `\vec{AB}` |
| 1 majuscule seule (`A`, `B`, `M`, …) | pas de `\vec` (point géométrique) |
| 1 minuscule typique fonction (`f`, `g`, `h`, `F`, `G`, `H`) | function call par défaut, alt vec via cascade |

**Désambig `f(1, 2)` (function call vs coords) :** cascade
`AlternativeGenerator` avec nouveau `RuleVectorCoordsVsCall`. Default =
function call (top-1, comportement existant), alt = `\vec{f}(1, 2)`.

## Pourquoi

### Couvre 80% du besoin lycée Terminale sans la complexité matricielle

Au lycée, vecteurs et points sont systématiquement écrits avec leurs
coordonnées. Aujourd'hui MathCursor sait décorer `vec u → \vec{u}` et
reconnaître `AB` comme deux majuscules avec cascade vec/paren/bracket, mais
ne savait PAS combiner avec des coordonnées explicites. L'élève qui tape
`u (1 2)` voulait un vecteur avec ses coordonnées, pas un appel de fonction
`u(1)` ou un identifiant nu.

C'est un sous-ensemble strict du brief matrices `2026-04-29-matrices-and-column-vectors.md`,
faisable en 1-2 jours, qui shippé indépendamment couvre l'essentiel du besoin
géométrie/algèbre linéaire de Terminale.

### Pattern à opt-in strict (anti-régression)

Le pattern chevauche `f(x)` (function call) et `(0, 1)` (intervalle). Pour
minimiser le risque de régression, la reconnaissance n'est activée QUE si
TOUS les critères sont vrais — sinon fallback pur au comportement existant :

1. Identifiant 1 ou 2 lettres immédiatement à gauche de la paren (ou avec
   un espace simple).
2. Le contenu parse en exactement 2 ou 3 cellules.
3. Le séparateur interne est homogène (que des espaces top-level OU que des
   virgules top-level — pas de mélange).
4. Pour le layout colonne : aucune cellule ne commence par un keyword scope
   non parenthésé (`sin x`, `lim …`, `frac a b`) qui consomme des args avec
   espaces.
5. Pour le layout ligne (virgule) : si l'identifiant est typique fonction
   (`f`, `g`, `h`, `F`, `G`, `H`), on ne reconnaît PAS comme coords (le
   default reste function call ; coords en alt cascade).

Si **un seul** critère manque → aucun nœud `VectorCoordinates` n'est créé,
on retombe sur `FunctionCall` / `Group` / `Interval` comme avant.

## Conséquences

### Code (couche 1 — core)

- **`Lattice/Ast/AstNodes.cs`** : nouveau nœud `VectorCoordinates` avec
  `Name`, `Values`, `Layout` (`"column"` | `"row"`), `IsPoint`.
- **`Lattice/Parser.cs`** :
  - Constructeur : ajout d'un tableau parallèle `_hasSpaceBefore[i]` pour
    récupérer l'info "espace présente avant ce token" après le filtrage des
    Spaces (info nécessaire pour distinguer layout colonne).
  - `TryParseVectorCoordinates()` : tentative spéculative en tête de
    `ParsePrimary`. Restaure `_i` sur échec.
  - Helpers `FindMatchingClose`, `SplitCells`, `ParseSubrange` (parse récursif
    d'une sous-séquence en parser fils sur slice).
  - Cas spécial `(-` (alias `\in` du lexer) : ré-injecté comme `(` + unary
    minus pour permettre `v(-1 3)`.
- **`Lattice/LatexRenderer.cs`** : nouveau case `VectorCoordinates` rendu
  selon les 4 combinaisons col/row × vec/point.
- **`Lattice/AlternativeGenerator.cs`** : nouvelle règle
  `RuleVectorCoordsVsCall` qui scanne la source pour les patterns
  `<f|g|h|F|G|H>(<n>, <n>[, <n>])` et propose l'alt `\vec{f}(...)` en plus
  du default function call. Priorité 2 (entre canonical-set et two-uppercase).

### Tests

61 tests xUnit dans `core-csharp/tests/MathCursor.Core.Tests/Lattice/VectorCoordinatesTests.cs` :

- §5.1 (cas positifs colonne) : 9 cas (`u (1 2)`, `u(1 2)`, `v(-1 3)`,
  `u (1 2 3)`, `OM (x y z)`, `AB (3 -1)`, `u (a+1 b-2)`, `u (2x+1 3y-2)`,
  `u (cos(t) sin(t))`).
- §5.2 (cas positifs ligne) : 9 cas (`u(1,2)`, `u(1, 2)`, `u (1, 2)`,
  `A(1, 2)`, `A(1,2)`, `M(x, y, z)`, `AB(3, -1)`, `u(2x+1, 3y-2)`, `A (1 2)`).
- §5.3 (anti-régression) : ~30 cas garantissant que `f(x)`, `cos(x)`,
  intervalles, `vec` keyword, AB cascade, number-tight `x2`, holes
  `frac`/`frac a`, quantificateurs, FuncDef continuent à fonctionner
  exactement comme avant.
- §5.4 (cascade `f(1, 2)`) : default function call + alt `\vec{f}(1, 2)`,
  `g(x, y)` idem, `u(1, 2)` reste sans cette ambig (u typique vec).
- Cas borderline : mélange séparateurs (`u(1, 2 3)`) → fallback ; 1 valeur
  (`u(1)`) → fallback ; 4+ valeurs → fallback.

Suite complète : 287 tests Lattice (avant 226), 599 tests core total
(avant 538). Aucune régression.

### Hors scope V1

- ❌ Matrices complètes `(1 2 ; 3 4)` — c'est le brief parent
  `2026-04-29-matrices-and-column-vectors.md`.
- ❌ Cardinalité 1 (`u(1)` ambigu avec function call à 1 arg).
- ❌ Cardinalité 4+ (rare au lycée).
- ❌ Cellules colonne avec keyword scope non parenthésé (`u (sin x cos x)`)
  — l'utilisateur doit parenthéser : `u (sin(x) cos(x))`.
- ❌ Décoration vec sur majuscule seule (A, B, M…) — convention française :
  point, pas vecteur.

## Validé par l'utilisateur

> "ok on tente ca"

## Statut

acté
