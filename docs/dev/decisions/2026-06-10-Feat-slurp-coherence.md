# Feat — Cohérence de SLURP : la symétrie = « le même choix de débordement partout »

**Date :** 2026-06-10
**Kind :** Feat
**Température :** molle (constantes ±1 / remise min ajustables ; concept ferme)
**Statut :** acté
**Supersedes :** [2026-06-10-Fix-sibling-echo-symmetry.md](2026-06-10-Fix-sibling-echo-symmetry.md), [2026-06-10-Fix-sibling-echo-twins.md](2026-06-10-Fix-sibling-echo-twins.md)

## Citation acté

> « en fait après l'algo de symétrie, 1/x+x2 - 1/x-x2+x3 devrait être traité gratuitement pareil.. c'est plus une notion de 1/slurped ou non ? » puis « ok bah go alors » — utilisateur, 2026-06-10

## Contexte

Les régimes géométriques `SiblingEcho` (préfixe de signature, jumelles à
masque égal) étaient des approximations : le contre-exemple utilisateur
`1/x+x2 - 1/x-x2+x3` (longueurs différentes, pas de préfixe) les mettait en
échec — popup pleine d'hybrides. L'invariant réel : chaque fraction qui EN A
LA PLACE fait un CHOIX (déborder/slurp ou s'arrêter), et la symétrie perçue
= le même choix répété.

## Décision

`SlurpCoherence` remplace `SiblingEcho` (qui est SUPPRIMÉ, avec `SigMask`/
`RootKey`) :

- **Parser** : flag `Node.Choice` sur les infixes construits avec >1 token à
  droite (il y avait la place de déborder).
- **Sites de choix** dans une lecture : fraction `Choice` à opérande droite
  composite = SLURP ; fraction minimale enfant gauche d'un infixe NON-espacé
  = MIN (un infixe espacé est une coupe de segment : pas de choix, pas de
  vote — `1/2 + 1/2x` préservé).
- **Comparables = mêmes LITTÉRAUX de tête** (numérateur + 1re feuille du
  dénominateur : l'œil voit deux « 1/x… »). Découverte clé du chantier,
  mesurée : un mode GLOBAL anonymisé pénalise les expressions mixtes
  légitimes (`3/4*cos(x)2 + 3x+1/2x+1` partait en auto sur une lecture
  aberrante — v1/v2 rejetées par le corpus).
- **Par paire comparable** : modes mélangés → +1 (écarte les hybrides) ;
  slurp répété à signatures distinctes → remise du dupliqué `−min(Base)`
  (les identiques sont déjà remboursées par GlobalCoherence ; `max`
  sur-corrige, mesuré).

## Effets verrouillés

- `1/x+x2 - 1/x-x2+x3` → `[tout-minimal, \frac{1}{x+x^{2}}-\frac{1}{x-x^{2}+x^{3}}, …]`
  sans hybrides en tête (fixture étalon).
- `1/x+x2 * 1/x-x2` → les deux paires symétriques, zéro hybride.
- `3/4*cos(x)2 + 3x+1/2x+1` : INTACT à l'identique (le garde-fou des v1/v2).
- 3 fixtures régénérées, assumées : `1/2x + 1/2x2` (2 candidats de plus en
  fenêtre, tops inchangés) ; `2x + 2x2` et `1/2 + 1/2x` passent AUTO→POPUP
  (alternatives honnêtes `(2x)^{2}` / `\frac{1}{2}x` — c'étaient des autos
  hérités du bonus fixe de l'écho ; à RE-DISCUTER si la friction gêne).

## Tradeoff & alternatives écartées

- **Mode global par famille (v1/v2)** : rejeté par la mesure (2 itérations,
  régressions documentées ci-dessus).
- **Garder l'écho géométrique en plus** : double comptage des remises,
  deux concepts pour une seule idée.
- **Étendre aux scripts/préfixes (« (2x)² », « cos x+1 »)** : familles de
  slurp distinctes, à instruire séparément si l'usage le demande.

## Conséquences

- **Code** : `Score.cs` (SlurpCoherence ~50 l., −SiblingEcho/SigMask/RootKey),
  `Parser.cs` (+2 l. Choice), `Node.cs` (+1 champ). Solde ≈ 0.
- **Fixtures** : 363 (3 régénérées revues + 1 étalon).
- Les deux ADRs écho passent `retracté / Superseded by` (historique conservé).

## Validation post-fix

- Suites complètes vertes ; `lim`, `f :R2->R`, `x+1 = x-1`, `1/x+1 +1/x+2`
  re-sondés intacts.
- Word : taper le cas étalon → popup symétrique sans hybrides.
