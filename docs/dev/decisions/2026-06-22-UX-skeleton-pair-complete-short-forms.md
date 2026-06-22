# UX — Paire de squelettes généralisée aux formes courtes COMPLÈTES (court présélectionné)

**Date :** 2026-06-22
**Kind :** UX
**Température :** molle
**Statut :** acté
**Supersedes :** — (étend [2026-06-12-UX-nary-skeleton-pair-preselection.md](2026-06-12-UX-nary-skeleton-pair-preselection.md), qui reste acté pour le cas squelette incomplet)
**Lié à :** [2026-06-12-UX-nary-skeleton-pair-preselection.md](2026-06-12-UX-nary-skeleton-pair-preselection.md), [2026-06-11-Feat-nary-arity-variants.md](2026-06-11-Feat-nary-arity-variants.md)

## Citation acté

> « quand on est dans le flux de frappe sur la somme ou l'integrale, int 0 n […] la version longue est retirée de la popup […]. Le fait qu'une formule soit entierement reconnue annule les autres » — utilisateur, 2026-06-22

> « le sum k f(k) doit aussi rendre les deux formes […] toute forme courte et longue (si un meme mot clé peut avoir plusieurs nombre d'arguments) on doit utiliser la meme regle […] ton flag dans les symbole me parait en trop » — utilisateur, 2026-06-22

> « plutôt uniforme mais inverser les choix, court en premier… parce que intégrale de f(1) vers f(10) c'est pas complètement délirant non plus, donc il vaut mieux être générique et laisser l'utilisateur trancher » — utilisateur, 2026-06-22

## Contexte

L'ADR 2026-06-12 ne déclenche `PairSkeletons` que si le **meilleur** parse est un n-aire **à trous** (frappe incomplète). Une forme courte **complète** (zéro trou) écrasait donc l'autre lecture : `int 0 n` → seul `∫ 0 dn` (indéfinie, entièrement reconnue), la définie `∫₀ⁿ □ d□` (coût 6 > `PopupGap=2`) était jetée. Même phénomène pour `sum k f(k)`, `lim u_n`, etc.

Première tentative (flag `pairOnNumericHead` ciblé int + heuristique « intégrande numérique ») **rejetée par l'auteur** : trop spécifique, pas générique. Décision : règle uniforme, sans flag.

## Décision

`PairSkeletons` devient générique pour **tout n-aire à variantes** :

- Le frère proposé est **toujours un squelette** (à trous). Selon le meilleur parse :
  - **INCOMPLET** (a des trous) : frère d'**autre arité** à trous — inchangé (ADR 2026-06-12), forme **longue présélectionnée** (sélection de gabarit).
  - **COMPLET** (forme courte sans trous) : frère **plus long** à trous, qui réinterprète la forme tapée en remplissant des slots supplémentaires → décision **popup**, forme **COURTE présélectionnée** (= ce qui est tapé, la moins chère), longue en alternative.
- **Pas d'heuristique, pas de flag** : on ne juge pas si la longue est « absurde » (`∫_{f(1)}^{f(10)}` n'est pas délirant) — on montre les deux, l'utilisateur tranche.

Règle de PRÉSENTATION : `Score.cs` inchangé.

Exemples (vérifiés) : `int 0 n` → popup [`∫0 dn`, `∫₀ⁿ □ d□`] ; `sum k f(k)` → [`∑_k f(k)`, `∑_{k=f(k)}^□ □`] ; `lim u_n` → [`\lim u_n`, `\lim_{u_n→□} □`] ; `int f(x) x` → [`∫f(x)dx`, `∫_{f(x)}^x □ d□`] ; `iint f x y` → [`∬ f dx dy`, `∬_f^x y d□d□`]. Inchangés : `int 0 n f(x) x`, `sum k 1 n f(k)` (formes canoniques complètes, pas de frère plus long) ; `int`/`sum`/`sum k`/`lim`/`iint`/`iiint` seuls (squelettes incomplets, longue d'abord) ; `iint 0 n`/`iiint 0 n` (2 unités < arité courte → encore incomplets → longue d'abord).

## Tradeoff & alternatives écartées

- **Conséquence assumée** : beaucoup de formes courtes complètes qui s'inséraient en **auto** (`sum k f(k)`, `lim u_n`, `prod i a_i`, `int f(x) x`, `iint f x y`, `sum k n`, `sum n 1/n`…) deviennent des **popups 2 candidats**. Accepté au nom de la cohérence (« même règle partout ») — Entrée présélectionne toujours la forme tapée, donc le flux reste correct ; ↓ propose l'expansion.
- **Flag par symbole + heuristique numérique** (1re version) : rejetée — ad hoc, non générique.
- **Forme longue présélectionnée pour les formes complètes** : rejetée — présélectionnerait `∑_{k=f(k)}^□` (corps réinterprété en borne) sur `sum k f(k)`, contresens. Court d'abord = sûr.
- **Pénalité Score** : déplace du coût → collatéral fixtures. Rejetée (règle de présentation).

## Conséquences

- **Code** : `ForestEngine.cs` (`PairSkeletons` : recherche de frère unifiée + ordre selon complétude). `symbols.json`/`Vocabulary.cs` : suppression du flag `pairOnNumericHead` (jamais committé). `Score.cs` : zéro.
- **Tests** : `fixtures.json` — +3 (`int 0 n`, `iint 0 n`, `iiint 0 n`), `sum 0 n` ajouté, et 9 fixtures auto→popup (`sum k f(k)`, `sum n 1/n`, `prod i a_i`, `int f(x) x`, `iint f x y`, `iiint f x y z`, `lim u_n`, `sum k n`, `produit i a_i`). Corpus 454, suite + mutations de tolérance vertes.
