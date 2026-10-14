# Feat — Chaîne sur la même ligne : « f(x) » puis « =2 » fusionnent en une formule

**Date :** 2026-10-14
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-multiline-chain-eqarr-architecture](2026-06-10-Feat-multiline-chain-eqarr-architecture.md) (chaînes multilignes), commit `c1c1784` (garde « ligne autonome », sans ADR)

## Citation acté

> « Quand on est sur la meme ligne idéalement on fusionne en une seule formule ? pourquoi l'adr disait l'inverse ? » puis « attention du coup a ne pas marcher sur les platebandes du multiligne.. dans le code unifie bien les choses, il faut que ca reste propre dans l'architecture » — utilisateur, 2026-10-14

## Contexte

Retour testeur : `f(x)` converti puis `=2` converti sur la **même ligne**
« se passe mal ». POC ruban (Word réel, document en « centré en groupe ») :

- MathCursor insérait `= 2` comme équation **séparée**, collée à `F(x)`
  (garde `c1c1784` : contenu avant la zone dans le ¶ → « ligne mixte » →
  repli autonome). Cette garde visait le merge avec la ligne **du dessus**,
  elle n'a jamais décidé contre la fusion sur la même ligne : celle-ci n'était
  simplement pas codée.
- Collées, les deux OMaths sont **fusionnées par Word** en une seule
  `F(x)=2`, qui reprend l'alignement par défaut du document
  (`wdOMathJcCenterGroup`) : l'alignement gauche est perdu. Avec un espace
  entre elles, on a deux équations : d'où le « une fois sur deux ».
- Le **flow M4** (ligne suivante ouverte avec le marqueur pré-placé) se
  déclenchait aussi : `=` orphelin sur un nouveau paragraphe.

Le merge multiligne (ligne seule sous une équation) fonctionne et ne change pas.

## Décision

**Une seule décision de cible de fusion** dans `ChainController`
(`FindMergeTarget`), utilisée par le commit **et** l'aperçu popup
(`ProbeMerge` remplace `ProbeMergeAbove`), remplace `IsAloneOnItsLine` :

| Cible | Quand | Résultat |
|---|---|---|
| `SameLine` | une équation **à nous** (map, résolution confirmée), **simple** (pas un bloc), juste à gauche de la zone (blancs seuls entre) | **une** équation : LaTeX `ancien + marqueur + reste`, sténo `ancienne + " " + tapé` |
| `Above` | ligne seule + ¶ du dessus porte une équation/chaîne à nous | bloc chaîne (inchangé) |
| `None` | sinon (prose, équation pas à nous, bloc) | équation autonome avec le marqueur rendu (inchangé) |

- **Même recette** pour les deux fusions : suppression de l'ancien
  (`RemoveOldBlock`, `sel.SetRange + Delete`) puis insertion sur la zone
  tapée. `SameLine` passe par `Insert` (typage display/inline + alignement
  gauche habituels), `Above` par `InsertBlock`.
- **Aperçu** : en `SameLine`, la popup affiche la formule fusionnée
  (`F(x)=2`).
- **Flow M4** : pas après une fusion `SameLine` (on est dans une ligne, pas
  dans un bloc).

POC 2 (recette réelle, Word, 2026-10-14) : 1 OMath `F(x)=2`, display,
`jc=Left` malgré le défaut « centré en groupe » du document, pas de résidu,
un seul Ctrl+Z restaure `F(x)` + `=2` tapé. Validé : « parfait ».

## Tradeoff & alternatives écartées

- **Laisser Word fusionner puis réaligner** : le `=` orphelin reste, la map
  pointe sur une équation qui n'existe plus telle quelle (revert faux).
- **Insérer dans l'OMath existante** : chirurgie d'OMath, interdite par
  l'architecture (on recompose toujours).
- **Chemin dédié dans `ConversionController`** : deux logiques de fusion
  divergentes (exactement ce qui a produit l'empiètement actuel).

## Conséquences

- **Code touché** : `Host/Blocks/ChainController.cs` (`MergeTargetKind`,
  `FindMergeTarget`, `CommitChainLine` renvoie la cible, `ProbeMerge`),
  `Host/ConversionController.cs` (aperçu + garde du flow M4).
- **Limite** : un commit **non marqué** collé à une équation (`f(x)` puis
  `+1`) peut encore être fusionné par Word ; hors périmètre (pas de marqueur
  de chaîne).
- **Revert** d'une formule fusionnée : rend la sténo jointe `F(x) =2`.

## Validation post-fix

Dans Word (document en « centré en groupe ») :
1. `f(x)` converti, puis `=2` sur la même ligne : aperçu `f(x)=2`, une seule
   équation alignée à gauche, pas de ligne `=` en dessous, un Ctrl+Z.
2. `f(x)` converti, Entrée, `=2` : bloc chaîne à 2 lignes comme avant.
3. Prose puis `=2` (« donc =2 ») : équation autonome `= 2` comme avant.
