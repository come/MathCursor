# UX — La home mène avec « capter l'intention », plus « sans friction »

**Date :** 2026-06-26
**Kind :** UX
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** mémoire `project-mathcursor-positioning` ; `docs/index.html` (home)

## Citation acté

> « en y reflechissant un peu mon moat c'est de "capter l'intention mathematique le plus
> rapidement possible" […] "tape comme tu penses, on va essayer de capter l'intention en
> cours de route" » — utilisateur, 2026-06-26

> « ok vous » — utilisateur, 2026-06-26 (arbitrage du registre)

## Contexte

La home (`docs/index.html`) menait avec **« Écrivez vos maths sans friction »**. C'est une
*négation* générique : n'importe quel éditeur d'équations à boutons peut la revendiquer. Elle
ne dit rien de la différence réelle.

Le moat, formulé par l'auteur : les autres outils convertissent un **input fini et propre**
(texte formaté, photo, palettes de boutons) → maths. MathCursor capte une **intention en
mouvement**, pendant qu'on tape comme on pense, sans avoir à connaître l'outil. La popup n'est
pas un aperçu : c'est un **copilote qui rassure en cours de route** (« tu es sur le bon
chemin »), guide avec les carrés manquants, tolère l'écriture relâchée. Le geste `Ctrl+Entrée`
fige proprement (dopamine), `Ctrl+Z` rejoue — un **flow**. Conséquence directe : un élève sous
PAP n'a pas un outil « à part », il a **le même outil que tout le monde** (dé-stigmatisation
par l'universalité).

Aucun de ces 3 beats (intention captée / popup-copilote / le geste-flow) n'était sur le site.

## Décision

La home est réécrite (passe complète) pour porter le moat :

- **Accroche** : H1 passe de « Écrivez vos maths / sans friction » à **« Tapez comme vous
  pensez. / On capte l'intention. »** ; « au fil de la frappe » descend dans le lead.
- **Registre** : **« vous »** sur toute la home. La proximité passe par le choix des mots et le
  rythme, pas par le tutoiement. La **démo reste en « tu »** (bac à sable, split assumé
  vitrine/terrain de jeu).
- **« Comment ça marche »** : la popup est reframée en **copilote** — montre la formule en
  train de se dessiner avec les **carrés manquants**, « vous voyez que vous êtes sur le bon
  chemin » ; étape « vous tapez » mentionne la tolérance (pas de parenthèses ni de symboles à
  chercher).
- **Nouveau bloc « Le geste »** : le flow `Ctrl+Entrée` (snap + dopamine) / `Ctrl+Z` (rejoue,
  aucune punition) / mémoire musculaire flèches+Entrée.
- **« Pour qui »** : intro de **cadrage universel** rendant la dé-stigmatisation PAP explicite
  (« le même outil que ses camarades »).

Périmètre limité à la home. Pages `prof/` et `pap-dys/` (funnels noindex) **inchangées**.

## Tradeoff & alternatives écartées

- **Garder « sans friction »** : écarté — négation générique, non ownable, ne dit pas le moat.
- **Mener frontalement avec « le seul qui lit ton intention »** (angle catégorie pur) : valable
  mais plus agressif/abstrait ; l'auteur a préféré l'angle « tape comme tu penses » (zéro
  apprentissage), plus chaleureux et concret.
- **Toute la home en « tu »** : écarté — sur la vitrine universelle (profs/parents y
  atterrissent), le tutoiement *catégorise* le produit en « jouet pour ados » et dessert la
  diffusion par recommandation des profs. Le « tu » est conservé là où il est naturel (démo).

## Conséquences

- **Code touché** : `docs/index.html` uniquement (FR dans le markup `data-i18n`, EN dans
  l'objet `I18N.en`). Clés existantes éditées (`hero_*`, `badge_friction`, `how_1_p`,
  `how_2_h/p`, `for_*`, `<title>`, meta) + nouvelles clés (`gest_*`, `for_intro`) + nouvelle
  `<section id="le-geste">`.
- **Tests** : aucun (site statique, pas de build). Vérification = rendu navigateur FR + bascule
  EN (aucune chaîne ne doit retomber en FR via le fallback).
- **API publique** : non concernée.
- **Règles MC impactées** : aucune. Décision de copy/positionnement, révisable (molle).

## Validation post-fix

Ouvrir `docs/index.html` dans le navigateur : vérifier le hero, « Comment ça marche », le
nouveau bloc « Le geste », « Pour qui » en FR ; basculer EN via le toggle et confirmer qu'aucune
nouvelle clé n'affiche du FR (= entrée `I18N.en` présente pour chacune) ; relire le registre
« vous » (pas de « tu » résiduel hors démo).
