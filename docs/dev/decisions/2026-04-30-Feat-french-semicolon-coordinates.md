# Feat — Séparateur `;` pour coordonnées en notation française

**Date :** 2026-04-30
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Pour le rendu des coordonnées d'un point ou d'un vecteur en layout=row,
utiliser le **point-virgule** (`;`) comme séparateur de sortie au lieu de
la virgule (`,`), conformément à la convention française.

Concerne le nœud AST `VectorCoordinates` avec `Layout="row"`. Les
coordonnées colonne (layout=column, rendu `\begin{pmatrix}…\end{pmatrix}`)
ne sont pas concernées.

| Source | Avant | Après |
|--------|-------|-------|
| `u(1, 2)` | `\vec{u}(1, 2)` | `\vec{u}(1 ; 2)` |
| `A(1, 2)` | `A(1, 2)` | `A(1 ; 2)` |
| `M(x, y, z)` | `M(x, y, z)` | `M(x ; y ; z)` |
| `\vec{u}` colonne | `\vec{u} \begin{pmatrix} 1 \\\\ 2 \end{pmatrix}` | inchangé |

L'**input** continue d'accepter la virgule comme séparateur (intuition
clavier de l'élève : `A(1, 2)`). Seul le **rendu** bascule en `;` français.

## Pourquoi

En notation mathématique française, la virgule joue déjà le rôle de
séparateur décimal (`1,5` = un et demi). Utiliser la virgule aussi pour
séparer les coordonnées d'un point crée une ambiguïté visuelle pour
l'élève :

- `A(1, 5; 2, 5)` non-ambigu — point de coordonnées `(1,5 ; 2,5)`
- `A(1, 5)` ambigu — point `(1; 5)` ou singleton `1,5` ?

Le système français résout ça en **réservant la virgule au décimal** et en
utilisant le point-virgule comme séparateur de coordonnées. C'est la
convention enseignée au lycée et celle que les profs et élèves attendent
de voir dans leurs documents.

L'input clavier continue d'accepter la virgule pour rester ergonomique
(la frappe `,` est plus naturelle que `;` au fil de l'eau, et le user a
explicitement demandé "une séparation… plus adaptée" pour le **rendu**,
pas pour l'input).

## Conséquences

### Code (couche 1 — core)

- **`LatexRenderer.RenderVectorCoordinates`** (ligne ~163-164) : modifier
  le séparateur du layout=row de `", "` à `" ; "` (avec espaces). Une
  ligne, sans changement de logique.
- Aucun changement dans le parser : la virgule en input continue d'être
  reconnue comme séparateur ligne par `SplitCells`.
- Aucun changement dans `LatexToUnicodeMath` : le `;` LaTeX est passé tel
  quel à Word qui l'affiche correctement.

### Tests

- `VectorCoordinatesTests` §5.2 (10 tests) : adapter les expected pour
  utiliser `;` au lieu de `,`. Les inputs restent inchangés (virgule).
- Tests cascade `RuleVectorCoordsVsCall` : adapter les alts qui contiennent
  la coords ligne.

### Hors scope V1

- ❌ Accepter le point-virgule en input. Si l'utilisateur tape `u(1; 2)`,
  le parser actuel ne le reconnaît pas (split top-level se fait sur `,`,
  pas `;`). À envisager si l'usage le demande.
- ❌ Changer le séparateur des intervalles. `[0, 1]` reste `[0,1]`
  conformément à la notation MathCursor existante (les intervalles ont
  leur propre stratégie héritée de `Interval`).

## Validé par l'utilisateur

> "Pour les coordonnées de points, une séparation des coordonnées par un
> point virgule serait plus adaptée dans le système français."

Validation du plan global :

> "ok, tout propre stp / P2 refactor supersedes"

## Statut

acté
