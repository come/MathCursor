# Fix — Précédence des relations (`=`, `<`, `>`…) : niveau proposition, le plus lâche

**Date :** 2026-06-02
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [`2026-05-30-Feat-beam-search-principe-5.md`](2026-05-30-Feat-beam-search-principe-5.md) (le fork rend les lectures parasites visibles), [`2026-05-28-Refactor-rewriting-engine-v2-clean.md`](2026-05-28-Refactor-rewriting-engine-v2-clean.md) (Principe 2 catégories)

## Citation acté

> « le = devrait etre pris en compte en séparateur de propositions non ? sauf si matché sum k=1 etc » puis choix « 1 » (catégorie `Relation`) — utilisateur, 2026-06-02

(Observé en Word : `f(x) = 1/x+1` proposait `\frac{f(x)=1}{x}+1` — le `=` absorbé dans un numérateur de fraction.)

## Contexte

Toutes les règles de `relations.yml` (`=`, `<`, `>`, `<=>`, `=>`, `in`…)
ont `produces: expr` et priorité 50 — **la même phase que l'arithmétique**
(`/`, `+`). Conséquence (vérifiée headless sur `f(x) = 1/x+1`) :

```
top  = \frac{f(x)=1}{x}+1      ← FAUX : rel-eq, étant le match le plus à
                                 gauche, est appliqué AVANT la fraction →
                                 f(x)=1 devient le numérateur.
coll = f(x)=\frac{1}{x+1}       ← correct, mais noyé
coll = \frac{f(x)=1}{x+1}       ← faux
coll = f(x)=\frac{1}{x}+1       ← correct, mais noyé
```

Une relation est une **proposition** : `=` doit lier **le plus lâche** (en
dernier), jamais être absorbé par un opérateur arithmétique. La précédence
manque. (Le `=` d'un `sum k=1` n'est PAS concerné : il est consommé par le
slot `=?` de l'anchor `sum`, pas par `rel-eq`.)

## Décision

Encoder la précédence dans les **catégories** + l'**ordre des phases** :

1. **Catégorie `Relation`** : `relations.yml` passe en `produces: relation`.
   Une relation n'est PLUS un `expr`.

2. **Catégorie `Statement` = `Expr ∪ Relation`** : `Subsumes(Statement, x)`
   accepte une relation, une expr, et tout ce qu'`Expr` accepte. `Expr` reste
   inchangé (n'accepte PAS `Relation`).

3. **Les opérandes arithmétiques exigent `expr`** (déjà le cas par défaut des
   slots) → une fraction/somme ne peut plus prendre une relation comme
   opérande. `\frac{f(x)=1}{x}` devient **impossible par typage**.

4. **Phase relations = la plus lâche** : un bucket `_relationRules`
   (= `Produces == Relation`) appliqué **après** l'arithmétique, dans la
   résolution déterministe ET dans le fork. Les opérandes de `=` sont donc des
   expressions déjà résolues (single-item) → `f(x) = <expr résolu>`.

5. **Corps de quantificateur = `statement`** : `forall {var} {set} {body:statement}`
   et `exists …` acceptent un corps relation (`forall n N n>0`) comme une expr
   (`forall x R P(x)`). Seuls ces deux en ont besoin.

## Tradeoff & alternatives écartées

- **Flag `loose: true` sans catégorie** (variante légère) : rejeté par
  l'utilisateur — ne corrige que l'ordre de phase ; garde `relation = expr`,
  donc une relation resterait *techniquement* absorbable hors ordre. La
  catégorie empêche l'absorption **par typage** (défense en profondeur) et
  colle au modèle « proposition ».
- **Précédence par poids numériques sur un graphe d'opérateurs** : sur-
  ingénierie pour le besoin ; les phases ordonnées suffisent.

## Conséquences

- **Code touché** : `Rewriting/Category.cs` (enum + Subsumes + Parse),
  `Rewriting/RewriteEngine.cs` (bucket `_relationRules` + phase après
  primitives, best + fork), `data/concepts/relations.yml` (produces relation),
  `data/concepts/logique.yml` (corps forall/exists → statement).
- **Comportement** : `f(x) = 1/x+1` → top `f(x) = \frac{1}{x}+1` + collision
  `f(x) = \frac{1}{x+1}` ; parasites `\frac{f(x)=1}{…}` supprimés.
  `forall n N n>0`, `a <=> b`, `x in R` préservés.
- **Tests** : golden + adapter verts ; nouveau lock e2e sur `f(x)=…`.

## Validation post-fix

`f(x) = 1/x+1` → 2 lectures correctes (top + 1 collision), 0 parasite.
`forall n N n>0` → `\forall n \in \mathbb{N}, n>0`. Suites vertes.
