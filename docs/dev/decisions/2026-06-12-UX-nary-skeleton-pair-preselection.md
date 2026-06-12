# UX — Préselection n-aires : les deux squelettes (long + court) en popup

**Date :** 2026-06-12
**Kind :** UX
**Température :** molle
**Statut :** acté
**Supersedes :** — (amende la règle no-hole de [2026-06-11-Feat-nary-arity-variants.md](2026-06-11-Feat-nary-arity-variants.md), qui reste acté pour le reste)
**Lié à :** [2026-06-11-Feat-nary-arity-variants.md](2026-06-11-Feat-nary-arity-variants.md)

## Citation acté

> « au niveau des preselection (ou on met les carré tant que la variable est pas tapée..) est ce qu'on peut faire une regle ce serait de mettre les deux candidats (version courte et version longue) quand elles existent ? sum et integrale par ex » — utilisateur, 2026-06-12
> Choix au cadrage : forme **longue présélectionnée** (premier candidat), règle sur **tous les n-aires à variante** (lim inclus). Plan approuvé.

## Contexte

La règle no-hole (ADR nary-arity-variants) interdisait aux variantes courtes le comblement par trous : `sum` seul ne proposait que le squelette complet `∑_□^□ □` en auto — la forme courte `∑_□ □` n'était jamais offerte pendant la phase préselection. Raison d'origine : au coût par trou (HOLE_COST=3), le squelette court (6) écrase le long (12), hors fenêtre popup (PopupGap=2) — arbitrage silencieux au mauvais profit.

## Décision

Le no-hole devient « **pas d'arbitrage silencieux entre squelettes** » :

1. Les variantes courtes se complètent par des trous comme l'arité canonique (la frontière de frappe `j >= _end` et les guards restent).
2. Le guard `NameAtom` (différentiels des intégrales) accepte les trous — un arg pas-encore-tapé n'est pas invalide.
3. **Règle de paire au niveau décision** (`ForestEngine.Finish`) : si le meilleur candidat est un n-aire à trous directs dont l'entrée a des variantes, le squelette frère (autre arité, à trous, même tête) est cherché dans la forêt ; s'il existe, la décision est forcée en **popup** avec la paire en tête, **forme longue d'abord** (présélection = comportement actuel, Entrée ne change pas d'habitude). Pas de frère (guards, pas assez d'unités) → comportement inchangé.

Comportements : `sum`/`int`/`lim`/`iint`/`iiint` seuls et `sum k` → popup [long, court] ; `sum k 1`, `int 0 1`, `sum k 1 n`, `lim x ±inf` → auto long inchangé ; formes courtes complètes (`sum k f(k)`, `lim u_n`) → auto court inchangé.

## Tradeoff & alternatives écartées

- **Forfait de trous par n-aire dans Score** (2×HOLE_COST quel que soit le nombre) : mettait les squelettes ex æquo mais faisait remonter `(\lim x)+∞` dans la fenêtre de `lim x +inf` (squelette 3→6) — fixture/tuto cassés. Rejeté par l'analyse chiffrée.
- **Coût de trou normalisé par arité** : ne produit pas d'ex æquo aux états intermédiaires (`sum k` restait auto court). Rejeté.
- La paire au niveau décision laisse **Score intact** (zéro déplacement de coût, zéro collatéral sur les 384 fixtures hors les 3 visées) — c'est une règle de PRÉSENTATION, pas de lecture.

## Conséquences

- **Code touché** : `Parser.cs` (variantes avec trous, suppression du param `allowHoles`), `Vocabulary.cs` (`NameAtom` accepte `Hole`), `ForestEngine.cs` (`Finish` : paire + réordonnancement long-d'abord). `Score.cs` : zéro changement. Adapter : zéro changement (popup N candidats et commit de squelettes existants).
- **Tests** : fixtures `sum`/`int`/`lim` auto→popup (2 cands), +3 (`iint`, `iiint`, `sum k`) → corpus 387. `TutorialSpecTests` vert sans modif (aucune consigne à mot-clé nu).
- **Transitoire assumé** : `iint 0` → popup [long, `∬ 0 d□d□`], longue présélectionnée.

## Validation post-fix

1. Suite moteur : avant retouche fixtures, SEULES `sum`/`int`/`lim` cassent ; après, corpus 387 vert + tuto vert.
2. Suite adapter (popup coverage WpfMath) verte.
3. Word : taper `sum` → popup 2 candidats, longue présélectionnée, ↓ + Entrée insère `∑_□ □`.
