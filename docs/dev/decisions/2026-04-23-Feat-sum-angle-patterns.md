# Feat — Patterns sum/product/integral séparateur espace + angle ABC

**Date :** 2026-04-23
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

1. Ajout de `sum_space_separated`, `product_space_separated`,
   `integral_space_separated` qui acceptent les bornes séparées par espaces,
   **sans** `=` ni `to`/`a` (comme la limite).
2. Ajout de `unit_angle` (`\widehat{ABC}`) pour les identifiants exactement
   3 lettres majuscules, avec un nouveau slot type `IDENT_UPPER_TRIPLE`.

## Pourquoi

- L'utilisateur s'attendait à ce que `Sum k 1 n f(x)` marche comme `lim x 0 f(x)`
  marche déjà. La limite a `TENDS_TO?` optionnel ; la somme exigeait `=` et
  `to`/`a`. Incohérence remontée en test utilisateur.
- Pour ABC : les seules options proposées étaient littéral + 2 variantes
  vectorielles. Dans un contexte geometric `ABC = ...`, l'angle
  (`\widehat{ABC}`) est sémantiquement plus probable que le vecteur. Il fallait
  le proposer comme choix.

## Conséquences

- 5 nouveaux gold examples auto-testés via `PatternEngineGoldTests`.
- Nouveau slot `IDENT_UPPER_TRIPLE` strict sur longueur = 3 (les paires "AB" ne
  le déclenchent pas, les quadruples "ABCD" non plus).
- Les partiels existants ne sont pas impactés.

## Validé par l'utilisateur

Pour la somme :
> "y'a pas une reconnaissance sur la somme avec des espace, comme la limite ?"

Pour l'angle :
> "il faudrait l'angle ABC en choix.. les vecteurs n'ont pas de sens ici"

Validation implicite des tests :
> "ajoute les tests"

## Statut

acté. Tests : 901/901 verts.
