# Fix — Repli par précédence : fin du pliage gauche aveugle sur chaîne longue

**Date :** 2026-06-12
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** garde-fou `Segment.MaxChain` (perf), ADR
[2026-06-10-Feat-slurp-coherence](2026-06-10-Feat-slurp-coherence.md) (les
lectures inter-termes que le repli dégrade)

## Citation acté

> « ok pour implementation, test bien apres ! » — utilisateur, 2026-06-12,
> après diagnostic partagé (« g(x)=2x+2x2+3x3+x4 y'a un soucis c'est pas
> reconnu »).

## Contexte

`g(x)=2x+2x2+3x3+x4` sortait en **auto** sur une lecture aberrante
`g(x)=(((((2×x+2)×x)²+3)×x)³+x)⁴`, candidat UNIQUE — alors que la version à
3 termes donnait un popup sain. Diagnostic (sonde sur la forêt complète) :

- `ChainLen` compte TOUS les infixes de profondeur 0, y compris les
  jointures serrées (`·` implicite, `·sup`) : l'entrée sans espaces passe de
  7 opérateurs (3 termes) à 9 (4 termes) ≥ `MaxChain = 8` ;
- au-delà du seuil, le repli `Fold` pliait À GAUCHE, opérateur par opérateur,
  **toutes précédences confondues** → le monstre, seul candidat, donc auto ;
- dans la forêt complète (sonde), la lecture naturelle coûte 1,00 et sort
  première ; le monstre n'y existe même pas.

## Décision

**Repli par précédence** (`ForestEngine.FoldSmart`) à la place du pliage
aveugle, aux trois sites de repli :

1. couper le segment aux infixes de profondeur 0 de **looseness maximale**
   présents (`Segment.SplitLoosest` — relations > additifs > multiplicatifs >
   joins serrés) ;
2. assembler chaque part par le pipeline NORMAL (récursif — les niveaux
   décroissent strictement, terminaison garantie ; les parts sont courtes,
   forêts complètes) ;
3. recombiner sous les caps existants (`Recombine` : fenêtrage par part +
   plafond Catalan/CombineCap) si la chaîne d'ops tient sous `MaxChain` ;
   sinon **pli gauche PAR NIVEAU** (associatif à l'affichage : `a+b+c` plat,
   `x=y=z` plat — inoffensif) avec le meilleur candidat par part ;
4. l'ancien `Fold` reste le repli ULTIME (part vide, rien d'assemblable).

La note « résultat simplifié » reste posée sur tous les chemins de repli.

## Tradeoff & alternatives écartées

- **Relever `MaxChain`** : repousse le mur sans le supprimer, et la forêt
  complète croît exponentiellement (Cap par cellule ne borne pas le produit).
- **Ne pas compter les joins serrés dans `ChainLen`** : les joins implicites
  participent réellement à l'ambiguïté (slurp) — les exclure ré-ouvre
  l'explosion qu'on borne.
- **Dégradation assumée du nouveau repli** : les lectures qui chevauchent un
  opérateur de coupe (slurp de fraction à travers un `+` de tête, ex.
  `f(x)=1/x+1+1/x+2+1/x+3+1/x+4` → plus de variante `\frac{1}{x+1}…`) sont
  perdues EN MODE REPLI uniquement — avec note. Avant le fix, ce même mode
  rendait une bouillie.

## Conséquences

- **Code touché** : `engine/src/MathCursor.Engine/ForestEngine.cs`
  (`FoldSmart`, 3 sites de repli basculés), `Segment.cs` (`SplitLoosest`).
- **Tests** : fixture `g(x)=2x+2x2+3x3+x4` → popup [naturelle, variante
  (2x)²] + note (corpus 385). Les fixtures de repli existantes
  (`1+2+…+10`, `1 +2 +…+9`, `(a +b +…+i)`) rendent à l'IDENTIQUE par
  construction (pli gauche mono-niveau = affichage plat) — suite verte sans
  régénération. Perf mesurée : ≤ 70 ms sur les cas longs.
- **API publique** : aucune.

## Validation post-fix

`g(x)=2x+2x2+3x3+x4` → popup, lecture naturelle en tête ; rejoué à chaque
build par les pipelines fixtures + mutations de tolérance.
