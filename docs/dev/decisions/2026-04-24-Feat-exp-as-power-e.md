# Feat — `exp(x)` rendu comme `e^x`

**Date :** 2026-04-24
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Le pattern `named_function_exp` (shared/symbolic.yaml) produit désormais
`e^{{{arg}}}` au lieu de `\exp({{arg}})`. Les entrées `exp(x)`, `exp(2x+1)`,
etc. rendent directement l'écriture "fonctionnelle simplifiée" attendue au
lycée.

## Pourquoi

- Notation lycée standard : les élèves et profs écrivent `e^x`, pas `\exp(x)`.
  Brief ergo "comme sur une feuille" → on s'aligne.
- `\exp(x)` reste correct mathématiquement mais moins parlant pour le public
  cible (PAP et profs de maths lycée).
- Si un utilisateur préfère `\exp`, il peut taper directement `e^x` → même
  rendu OMath.

## Conséquences

- Un seul template changé dans `shared/symbolic.yaml`.
- Gold example du pattern mis à jour : `exp(x) → e^{x}`.
- `LatexToUnicodeMath` convertit `e^{x}` → `e^(x)` déjà (via exposant
  structurel) → OMath correct.
- Pas de régression attendue : les 2 sorties (`\exp(x)` vs `e^{x}`)
  produisent un rendu visuellement différent mais mathématiquement identique.

## Validé par l'utilisateur

> "y'a aussi un sujet c'est exp(x) se note plutot e exposant x (pour
> l'exponentielle) tu peux chercker ?"

> "limiter à ² c'est le seul qui est au clavier non ?" (confirmation du plan A
> dans la foulée)

## Statut

acté
