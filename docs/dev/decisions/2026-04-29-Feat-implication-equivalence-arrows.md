# Feat — Détection `=>` / `<=>` / `<==` et conversion en flèches math

**Date :** 2026-04-29
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Reconnaître les flèches d'implication / équivalence au clavier et les
convertir en macros LaTeX standards :

| Source | Rendu LaTeX |
|--------|-------------|
| `=>` / `==>` / `⇒` | `\Rightarrow` |
| `<=>` / `<==>` / `⇔` | `\Leftrightarrow` |
| `<==` / `⇐` | `\Leftarrow` |

Les variantes ASCII et Unicode mappent toutes vers les 3 macros standards.

### Mécanique de tokenisation greedy par coût négatif

Le lexer émet TOUTES les variantes multi-char qui matchent à une position.
Pour garantir que `<=>` (3 chars) gagne sur `<=` + `>` (2+1 chars, même coût
total 0) au tri Dijkstra, le **coût des multi-char ops devient négatif
proportionnel à leur longueur** (`-length`). Concrètement :
- `<==>` cost = -4 (1 arête)
- `<=>` cost = -3 (1 arête)
- `<=` cost = -2 (1 arête)
- mono `<`, `=`, `>` cost = 0

Donc le path multi-char le plus long minimise toujours le coût total. Pas
besoin de manipuler l'ordre dans `MultiCharOps`.

## Pourquoi

Les élèves lycée tapent naturellement `=>` pour ⇒ et `<=>` pour ⇔. Ce sont
des notations math standards, pareilles à `<=` (≤) ou `>=` (≥) déjà
supportés.

### Pourquoi pas un nœud AST nouveau

Les flèches d'implication sont des **relations binaires** (comme `=`, `<`,
`≤`). Elles s'insèrent naturellement dans la grammaire `ParseRelation`
existante via `IsRelOp`. Pas de structure nouvelle, juste un type d'op
supplémentaire dans `Bin`.

### Pourquoi le coût négatif greedy

Sans coût négatif, les tokens multi-char ont coût 0 comme les mono — le
Dijkstra à coût égal a un comportement indéterministe. Empiriquement, ça
marchait pour `<=` parce qu'aucune autre interprétation à coût 0 n'existait
sur 2 chars. Mais avec l'ajout de `<=>` (3 chars), le path `<=` + `>`
(2 arêtes, coût 0) est en concurrence avec `<=>` (1 arête, coût 0). Le coût
négatif tranche définitivement en faveur du plus long.

Bénéfice annexe : `(-` (alias `\in` introduit hier) gagne maintenant sur `(`
+ `-`, `//` (parallèle) sur `/` + `/`, etc. Tous les multi-char ops sont
préférés systématiquement.

## Conséquences

### Code (couche 1 — core)

- **Vocabulary.cs** : 5 nouvelles entrées dans `MultiCharOps` :
  `<==>`, `<=>`, `==>`, `<==`, plus les 3 Unicode `⇒`, `⇔`, `⇐`.
- **Lexer.cs** : poids des multi-char ops passe de `0` à `-op.Length`.
- **Parser.cs** `IsRelOp` : ajoute les 8 nouveaux ops comme relations binaires.
- **LatexRenderer.cs** `RenderBin` : 3 nouveaux cases pour les flèches,
  routent toutes les variantes ASCII/Unicode vers les 3 macros standards.

### Tests

- 9 tests nouveaux dans `LatexRendererTests` : conversions, anti-régression
  `<=`/`>=`/`->`.
- 1 test legacy mis à jour : `Multichar_op_preferred_over_two_singles`
  asserte maintenant `cost = -2` au lieu de `0` (effet du nouveau schéma).

### Anti-régression validée

- `<=` continue à rendre `\leq`
- `>=` continue à rendre `\geq`
- `->` continue à fonctionner pour les limites (`lim x -> 0`)
- `(-` continue à rendre ` \in `
- `//` continue à rendre `//` (parallèle)

## Validé par l'utilisateur

Brief complet :
[`docs/dev/briefs/2026-04-29-implication-equivalence-arrows.md`](../briefs/2026-04-29-implication-equivalence-arrows.md)

Direction (sélection des briefs à attaquer) :
> "iterative et implication et merge"

## Statut

acté
