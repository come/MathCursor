# UX — Refonte visuelle du site, « prof / parent proof »

**Date :** 2026-10-20
**Kind :** UX
**Température :** molle (accueil validé et mis en production le 2026-10-20 ; déclinaison des autres pages à suivre)
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-10-20-Meta-site-content-refresh](2026-10-20-Meta-site-content-refresh.md) (contenu à jour, refonte alors écartée), mémoire « positionnement produit / site » (promesse = maths sans friction / flow)

## Citation acté

> « on peut tenter une refonte visuelle complete ? en local pour l'instant ?
> pour rendre le truc plus chouette et plus prof/parent proof » — utilisateur,
> 2026-10-20 ; choix : « Net et rassurant », « Profs et parents à égalité »,
> « Accueil d'abord, puis le reste ».

## Contexte

L'identité actuelle (papier quadrillé, bords « dessinés à la main », polices
tech) est sympathique mais fait « projet de développeur » pour un prof ou un
parent qui découvre l'outil. Les éléments rassurants (gratuit, logiciel libre,
sans compte, documents qui restent sur l'ordinateur, équations Word standard,
fait par un parent d'élève sous PAP) sont noyés dans le texte ; un seul visuel
(vidéo) ; du jargon (OMath, sténo).

## Décision

### Direction visuelle « net et rassurant »

Logo et bleu du logo conservés ; fond blanc, cartes à ombre légère (fin du
quadrillage et des bords dessinés), une typo de texte très lisible, plus d'air,
et des **visuels concrets** : vraies captures Word (rendu Word → PDF → PNG,
comme le tableau de variation) et formules rendues dans la page.

### Accueil restructuré (profs et parents à égalité)

1. En-tête : promesse, vidéo, deux boutons (essayer en ligne / installer pour
   Word), **bandeau de confiance** (gratuit · logiciel libre · sans compte ·
   vos documents restent chez vous).
2. **Avant / après** : ce qu'on tape, ce qui s'affiche.
3. Comment ça marche, en trois temps.
4. **Ce que MathCursor fait dans Word** : formules, tableaux de signe /
   variation, physique-chimie, mise en page de cours.
5. **Trois parcours** : prof, élève, parent (PAP, dys).
6. **Confiance** : données, contrôles (pause par document), documents lisibles
   sans MathCursor, logiciel libre, hors ligne.
7. Installation en trois étapes, qui développe, FAQ.

### Méthode

Branche `site/refonte`, nouvelle feuille de style `docs/site.css` pour
l'accueil seulement (les autres pages gardent `style.css` tant qu'elles ne sont
pas déclinées) ; relecture sur captures ordinateur + mobile ; itérations ;
puis déclinaison (Profs, Aménagement, puis Téléchargements, Installation,
Confidentialité, Contact) et retrait de l'ancienne feuille. Aucun déploiement
avant validation.

Règles de rédaction inchangées : promesse « maths sans friction / flow », pas
de tiret cadratin, pas de « cerveau », jamais « aucune télémétrie » / « 100 %
local », vouvoiement (accueil, profs, aménagement), aucune affirmation non
vérifiée sur les examens ou des outils tiers.

## Tradeoff & alternatives écartées

- **Garder le côté cahier en plus soigné** : identité conservée mais le
  « projet de dev » reste perceptible pour le public visé.
- **Chaleureux / scolaire** (couleurs par public, icônes) : plus marketing,
  risque de faire gadget auprès des profs.
- **Tout le site d'un coup** : relecture plus lourde, pas d'itération sur la
  direction avant de la généraliser.

## Conséquences

- **Fichiers** : `docs/site.css` (nouveau), `docs/index.html` (réécrit),
  visuels dans `docs/assets/`. Les autres pages suivront.
- La température passe à **molle** quand la maquette de l'accueil est validée.
