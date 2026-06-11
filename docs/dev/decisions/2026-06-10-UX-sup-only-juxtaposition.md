# UX — « x2 » = puissance seule ; l'indice se force avec `_`

**Date :** 2026-06-10
**Kind :** UX
**Température :** provisoire (à re-examiner au premier retour beta sur les suites)
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-split-distance-cost-vec.md](2026-06-10-Feat-split-distance-cost-vec.md) (philosophie popup), fixtures baseline JS (divergence assumée)

## Citation acté

> « les indices sont quand même vachement plus rares que les puissances.. ne faire que puissance.. et si l'utilisateur veut de l'indice il force _ » ; après challenge explicite (« enfin challenge moi sur ça en fait ») et exposé du contre-cas suites : « oui 1 et 2 » — utilisateur, 2026-06-10

## Contexte

La jonction collée `nom+chiffre` (« x2 ») produisait DEUX lectures (`x^2`,
`x_2`) → popup à chaque occurrence, or c'est la juxtaposition la plus
fréquente de toute frappe math. Pour le public PAP, chaque popup est une
décision cognitive.

## Décision

La règle de jonction `name+num` ne porte plus que le rôle `sup` (un champ de
la table JOIN du lexer). `x2` → `x²` en AUTO. L'indice reste pleinement
accessible en EXPLICITE : `x_2`, `u_n`, `u_1` (auto, vérifié).

**Contre-cas documenté et assumé** : le chapitre SUITES (`u1` → `u¹` auto au
lieu d'une popup proposant `u₁`). Arbitrage : la friction popup frappe à
chaque ligne de tous les chapitres, le piège ne frappe qu'aux suites, et
l'habitude `u_1` est conforme LaTeX, enseignable en une consigne beta
(« indices : tapez _ »). Réversible en un champ + régénération corpus.

## Tradeoff & alternatives écartées

- **Statu quo (popup)** : filet, mais friction permanente sur le cas n°1.
- **Sub réservé à certaines lettres (u, v, w…)** : hardcode produit dans le
  moteur générique, refusé d'office.
- **Biais de coût (sub plus cher)** : `x²` était déjà premier ; le seul vrai
  levier est popup→auto, donc autant être franc.

## Conséquences

- **Code touché** : `Lexer.cs`, un champ de la table JOIN (`Roles`).
- **Fixtures** : 9 régénérées (diff revu entrée par entrée, garde-fou
  script) : `x2`/`x23`/`x2+1`/`ab2`/`2x+2x2` passent popup→AUTO, les
  3 `f:R2->R` et `1/2x+1/2x2` perdent le bruit `_`. Corpus 359.
- **Beta** : consigne à ajouter à la doc utilisateur (« indice : _ »).
  Premier retour prof sur les suites → re-examen (température provisoire).

## Validation post-fix

- Suites complètes vertes ; mutations corpus comprises.
- Word : `x2`/`2x+2x2` s'insèrent en auto sans popup ; `u_1` → `u₁` auto.
