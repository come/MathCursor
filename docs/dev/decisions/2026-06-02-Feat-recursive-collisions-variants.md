# Feat — Collisions récursives génériques (Variants propagés à toute profondeur)

**Date :** 2026-06-02
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [`2026-05-30-Feat-beam-search-principe-5.md`](2026-05-30-Feat-beam-search-principe-5.md) (le fork top-level que ce travail généralise en profondeur)

## Citation acté

> « l'idée etait d'etre generique et permettre les recursions ! donc ca doit marcher dans le corps .. de maniere generique » — utilisateur, 2026-06-02

(Constat : `somm k=1 2 f(x)/x+1` ne propose pas la 2ᵉ lecture `\frac{f(x)}{x+1}`, car le corps de la somme est résolu en mono-chaîne.)

## Contexte

Le Principe 5 (fork) était **limité au top-level** : les corps d'anchor
(chunks, résolus par `ResolveChunk`) étaient mono-chaîne. Donc `f(x)/x+1` au
top-level collisionne, mais le MÊME `f(x)/x+1` comme corps de `somm` non
(vérifié headless : 0 collision dans le corps, 1 au top). L'utilisateur veut
la collision **générique et récursive** — à toute profondeur.

## Décision

Faire **porter à chaque Item ses lectures alternatives** et les **propager**
à l'émission, récursivement :

1. **`Item.Variants`** : liste (vide par défaut) de formes résolues
   alternatives du MÊME span source. Un `RewriteItem` peut en porter.

2. **`ResolveChunk` attache les variants** : il calcule déjà les lectures du
   chunk (structurel + fork primitif/relation) ; il retourne le meilleur Item
   en y **attachant les autres lectures** comme `Variants`. Comme `ResolveChunk`
   est récursif (chunks imbriqués), les variants remontent de n'importe quelle
   profondeur.

3. **L'émission propage les variants des slots** : quand une règle produit un
   `RewriteItem` à partir de ses slots, si un slot porte des `Variants`, on
   produit aussi les sorties correspondantes (en variant **un slot à la fois**
   → borné, et suffisant car chaque niveau a en général un point de collision ;
   la combinatoire multi-slots simultanée est plafonnée par le beam). Le résultat
   est attaché comme `Variants` de l'Item produit.

4. **Top-level** : on déplie les `Variants` des Items finaux (en plus du fork
   d'ordre top-level existant) → `RewriteResult.Alternatives`.

**Best inchangé** : la lecture top reste la résolution déterministe
leftmost-longest → les tops golden sont préservés par construction. LaTeX
sérialisé en dernière étape (adapter) — les Variants sont des structures.

## Tradeoff & alternatives écartées

- **Fork limité au top-level** (état précédent) : rejeté — l'utilisateur veut
  la récursion générique, pas un cas top-level spécial.
- **Réécrire le matcher en backtracking multi-match** (TryMatchAnchor renvoie
  N matches selon les lectures de chunk) : rejeté — très invasif et risqué.
  L'approche par Variants attachés réutilise le matcher tel quel et propage au
  niveau Item, ce qui est à la fois plus simple et naturellement récursif.
- **Produit cartésien complet multi-slots** : borné (vary-un-slot + plafond
  beam) pour éviter l'explosion ; couvre les cas réels (un point de collision
  par niveau, propagé en profondeur).

## Conséquences

- **Code touché** : `Rewriting/Item.cs` (`Variants`), `Rewriting/RewriteEngine.cs`
  (production avec propagation de variants, `ResolveChunk` attache, dépliage
  top-level), éventuellement helper d'émission centralisé.
- **Comportement** : `somm k=1 2 f(x)/x+1` → top `…\frac{f(x)}{x}+1` +
  collision `…\frac{f(x)}{x+1}`. `1/sum k 1 n 1/k+1` → collision propagée.
  Top-level `f(x)/x+1` inchangé.
- **Tests** : 166 golden + adapter préservés (best déterministe) ; nouveaux
  locks e2e sur collisions dans corps + imbriquées.

## Validation post-fix

`somm k=1 2 f(x)/x+1` → 2 lectures (top + 1 collision corps). Imbrication
`1/sum … 1/k+1` → collision remonte. Suites vertes, pas d'explosion.
