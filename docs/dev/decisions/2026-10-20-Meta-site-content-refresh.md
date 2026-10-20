# Meta — Mise à jour du contenu du site après la 0.12.0

**Date :** 2026-10-20
**Kind :** Meta
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-18-Feat-usage-counter-telemetry](2026-06-18-Feat-usage-counter-telemetry.md) (compteur d'usage : la comm ne doit plus dire « 100 % local »), [2026-10-16-Feat-sign-variation-table-insert](2026-10-16-Feat-sign-variation-table-insert.md), [2026-10-19-Feat-study-table-cell-roles-contextual-popup](2026-10-19-Feat-study-table-cell-roles-contextual-popup.md)

## Citation acté

> « et juste est ce que avec totues les dernieres nouveautés, on se dit pas
> qu'on met à jour le site ? pour que ca soit plus chouette et que ca decrive
> toutes les possibilités ? » — utilisateur, 2026-10-20 ; choix : « Livrer
> d'abord, puis le site » et « Contenu + petites retouches ».

## Contexte

Hors page Téléchargements et démo, le site est resté figé fin juin 2026 alors
que le produit a avancé (0.11.6 → 0.12.0). État des lieux :

- feuille de route de l'accueil fausse : la chimie y est « en cours » (livrée
  en 0.11.8), les dérivées partielles « à venir » (livrées en 0.11.6) ;
- nouveautés seulement dans le journal des versions : physique-chimie, notation
  scientifique, grec / symboles Unicode, indice / exposant de Word, chaînes sur
  une ligne, Échap = pause ;
- jamais mentionnés : encadrés colorés, colonnes, choix de la police math ;
- `amenagement.html` affirme « 100 % local : aucune connexion Internet, rien ne
  sort de l'ordinateur » : faux depuis le compteur d'usage (et la vérification
  de mise à jour) ; badge et pieds de page « 100 % local » ambigus ;
- hero de l'accueil sans VS Code ; démo sans exemple de physique-chimie.

## Décision

Passe **contenu + petites retouches**, dans la structure et le style actuels
(pas de refonte), après la livraison de la 0.12.0 :

1. **Accueil** : section « Nouveau : tableaux de signe et de variation » avec
   une capture d'un vrai tableau (rendu Word → PDF → PNG,
   `assets/tableau-variation.png`) ; « Ce que MathCursor met en forme » enrichi
   (Unicode, notation scientifique, indice / exposant Word, chaînes, dérivées
   partielles, mode physique-chimie, mise en page Word : encadrés, colonnes,
   police math) ; feuille de route à jour ; FAQ : Échap met la détection en
   pause, Ctrl+Espace la relance ; VS Code dans le hero ; profs : tableaux et
   encadrés.
2. **Page profs** : tableaux de signe / variation, encadrés, police math,
   physique-chimie.
3. **« 100 % local »** : remplacé par une formulation exacte (« vos documents
   restent sur votre ordinateur », renvoi vers la page Confidentialité) dans
   l'aménagement, le badge et les pieds de page. Règle CLAUDE.md : ne jamais
   affirmer « aucune télémétrie ».
4. **Démo** : exemples de physique-chimie (`H2O`, `Fe2+`, `v0`) et de notation
   scientifique, dans la source `web-demo` (la démo publiée est régénérée par
   `/deploy-prod`).

**Chimie (correction à la relecture, « et la chimie ? »)** : le mode
Physique-chimie ne couvre pas toute la ligne « Chimie » de l'ancienne feuille de
route. Vérifié sur le moteur : équations de réaction avec `->` (`2H2 + O2 ->
2H2O`, `Fe3+ + 3e- -> Fe`) correctes en premier choix → mises en avant (accueil,
profs, démo) ; équilibre `<=>` mal lu (≤ □ >) et nombres d'oxydation non gérés
→ la feuille de route garde « Chimie : équilibres (⇌) et nombres d'oxydation ».

Règles de rédaction : promesse « maths sans friction / flow » ; pas de tiret
cadratin dans les nouveaux textes ; pas de « cerveau » ; vouvoiement sur
l'accueil, la page profs et l'aménagement, tutoiement sur la démo (conventions
existantes).

## Tradeoff & alternatives écartées

- **Refonte visuelle de l'accueil** : plus longue, à valider sur maquette ;
  le besoin immédiat est un contenu juste et complet.
- **Annoncer les tableaux avant leur livraison** : écarté, on livre d'abord
  (0.12.0) pour un message cohérent.
- **Galerie avec les vidéos inutilisées de `assets/`** (égalités, équivalences,
  systèmes, popup auto) : leur contenu n'a pas été revu depuis juin ; reporté.

## Conséquences

- **Fichiers** : `docs/index.html`, `docs/prof/index.html`,
  `docs/amenagement.html`, pieds de page de `privacy.html` / `contact.html`,
  `docs/assets/tableau-variation.png`, exemples de la démo (`web-demo`).
- **Validation** : relecture des pages en FR et EN, puis déploiement du site
  (`tools/cloudflare/deploy.sh site`) sur accord.
