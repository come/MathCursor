# Fix — Fusion des zones NER fragmentées (popup muette sur « (a b c d ; e (sum x 0 1 »)

**Date :** 2026-06-12
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-ner-auto-detection-debounce.md](2026-06-10-Feat-ner-auto-detection-debounce.md) (le pipeline étendu ici)

## Citation acté

> « tu peux checker pourquoi il monte pas la popup : (a b c d ; e (sum x 0 1 » — utilisateur, 2026-06-12

## Contexte

Le moteur lit `(a b c d ; e (sum x 0 1` sans broncher (matrice 2×4 auto, squelette sum en cellule). Mais le NER fragmente la formule en DEUX zones adjacentes — `[0,10] "(a b c d ;"` conf 0,95 et `[11,23] "e (sum x 0 1"` conf 1,00 — séparées par un seul espace. `PickNearestZone` prend la zone au caret, dont le morceau `e (sum x 0 1` est imparsable seul → moteur erreur → popup masquée en silence. Même topo sur `(a b c d ; e (sum`.

## Décision

**Fusion blancs-seulement + repli** :
1. `ZoneRefiner.MergeWhitespaceAdjacent` (pur, testé) : à partir de la zone au caret, absorbe les zones voisines dont le gap est UNIQUEMENT des blancs (≤ 3 chars), en chaîne, des deux côtés. Un gap contenant de la prose (« x=1 **et** y=2 ») ne fusionne jamais.
2. `AutoDetectController.RunDetection` : tente la zone FUSIONNÉE d'abord ; si le moteur la refuse (`TryProposeAuto` → false), repli sur la zone seule. Deux formules réellement distinctes séparées d'un espace retombent donc sur le comportement antérieur.

## Tradeoff & alternatives écartées

- **Ré-entraîner le NER pour ne plus fragmenter** : lourd, non garanti (la fragmentation dépend du contexte), et le runtime doit de toute façon être robuste à des zones imparfaites. La fusion est complémentaire d'un futur corpus v9 si le motif revient.
- **Fusionner sans repli** : aurait cassé les cas « deux formules voisines » (`x=1 y=2` au caret droit) où chaque zone vit sa vie. Le repli préserve l'existant.

## Conséquences

- **Code** : `ZoneRefiner.MergeWhitespaceAdjacent` + boucle de tentatives dans `AutoDetectController` (la proposition devient « premier essai qui parse gagne »). Moteur : zéro changement.
- **Tests** : +4 dans `ZoneRefinerTests` (fusion 1-espace, refus prose, chaînage, maxGap). Adapter 281/281, build VSTO OK. Sonde chaîne-complète (NER v6 réel) : `(a b c d ; e (sum x 0 1` → POPUP auto matrice 2×4 ; `on a x=1 et y=2` → popup `y=2` inchangé.

## Validation post-fix

Word : taper `(a b c d ; e (sum x 0 1` → la popup monte (matrice 2×4, sum en cellule) ; `on a x=1 et y=2` → popup sur `y=2` seulement.
