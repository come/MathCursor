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

### Déclinaison aux autres pages (2026-10-21)

> « oui tu peux faire les autres pages maintenant stp je ne sais pas si j'aurai
> mis profs et amenagement en header par contre :) » — utilisateur.

- **En-tête commun réduit** : Démo · bouton Installer · FR/EN.
  > « dans le header y'a trop de truc => Demo + Bouton "installer" ca suffit
  > non ? toutes les pages sont linkées => guide d'install dans la section
  > installer c'est ok » — utilisateur, 2026-10-21.

  Profs, Aménagement, Téléchargements et Installation restent accessibles par
  les parcours de l'accueil, la section Installation et le pied de page.
- **Pied de page commun** (Démo, Profs, Parents & PAP, Téléchargements,
  Installation, Confidentialité, Contact), clés de traduction `ui_*` injectées
  dans chaque page.
- **Couche de compatibilité dans `site.css`** : les classes existantes des pages
  secondaires (`card`, `button`, `cta-row`, onglets d'installation, formulaire
  de contact, notes « version archivée », listes `features`) sont habillées dans
  la nouvelle charte ; alias des anciennes variables pour `demo.css` / `live.css`.
  Contenus et traductions des pages inchangés.
- **Aménagement** restructurée pour les parents : en-tête avec boutons, trois
  cartes (En classe, En examen, Sur l'ordinateur de l'élève), bandeau Parents.
- **Confidentialité** : intertitres à taille de lecture (`.prose`).
- **Démo** (`index.html`, `live.html`, source `web-demo`) migrée. Les pages
  `admin/` (privées) gardent leur style.

### Page Profs : cours tapé dans Word, avant / après (2026-10-21)

> « http://localhost:8765/prof/ => demo des encadrés pour qu'ils puissent
> sentir la richesse visuelle d'un cours ce serait cool » puis « il faudrait
> avoir la page bien présentée, avec une demonstration aussi + un tableau de
> signe dedans, et eviter du coup le tableau de signe en pleine page juste au
> dessus, pourrais tu avoir un rendu qui doit aussi plus "word", plus grande
> marges police plus petite. eventuellement le bandeau simulé au dessus ? et la
> page globale plus petite => ptetre pour que ca soit sympa, un Avant / Apres »
> — utilisateur, 2026-10-21.

- **Fenêtre Word simulée** en HTML/CSS (barre de titre, onglets, ruban
  MathCursor avec Tableau et Encadré en évidence) contenant **deux pages A4
  côte à côte** : « Avant, ce que vous tapez » (texte et notation clavier
  surlignée, boutons du ruban utilisés) et « Après, ce que vos élèves lisent ».
- La page « Après » est un **vrai rendu Word** produit avec le code de l'add-in
  (A4, marges 66 pt, corps 10 pt) : encadrés Définition, Propriété, Exemple,
  Théorème, une démonstration (dérivée de la fonction carré) et le tableau de
  variation de x² − 4x + 3. Image unique `assets/cours-derivation.png`,
  cliquable pour l'agrandir ; la variante mobile est retirée.
- Les saisies de la page « Avant » ont été vérifiées sur le moteur (conversion
  directe ou première proposition correcte).
- Le grand tableau de variation pleine largeur de « Vos outils de cours » est
  retiré : il figure maintenant dans le cours.

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
