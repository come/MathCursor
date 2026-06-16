# UX — Page d'accueil : recentrer sur « maths sans friction », démoter Word

**Date :** 2026-06-16
**Kind :** UX
**Température :** molle
**Statut :** acté
**Lié à :** `docs/index.html`

## Citation acté

> « on va maintenant reclarifier la page d'accueil, je trouve qu'elle a pas mal
> drifté » … « challenge un peu tout, ne met pas Word aussi haut, met que ça aide à
> taper vite des maths, quel que soit l'outil qu'on utilise » … « parler plutôt de
> flow » — utilisateur, 2026-06-16. Ampleur : **resserrer** (garder la structure).

## Contexte

`docs/index.html` avait drifté : faits périmés (tag « Bêta 0.1 », Statut daté de mai
2026), redondance lourde (mêmes 4-5 messages répétés 3-4×), et un cadrage trop
**Word-centré** alors que la valeur est indépendante de l'outil.

## Décision

Resserrer la page et recentrer le message, sans refonte visuelle.

- **Promesse** : mener avec la valeur — écrire des maths **sans friction, sans casser
  le flow, à la vitesse de frappe**. Word démoté en véhicule actuel (« disponible
  aujourd'hui dans Word ; la démo tourne dans le navigateur »). Pas de promesse
  multi-outils au présent (Mac/Web/iPad = phase 2). Pas de « cerveau » (peu vendeur).
- **Hero** : nouveau h1 / lead / badges (`Sans friction`, `100 % local`, `Sans
  compte`), CTA primaire « Tester en ligne », secondaire « Installer pour Word », tag
  « Bêta publique » (plus de n° de version à maintenir).
- **Structure 9 → 7 sections** : suppression de **Fonctionnalités** (redite de
  *Comment ça marche* + *Scope*) et **Statut** (périmé, mince, recoupe *Scope*).
  Gardées : Hero, Comment ça marche, Scope, Pour qui, Installation, Qui développe, FAQ.
- **Déduplication** : chaque message a un seul foyer — « propose/vous validez » →
  note de *Comment ça marche* ; « OMath éditable » → `scope_ok_1` ; « 100 % local » →
  badges + une FAQ (fusion confidentialité + RGPD) ; « réédition » → *Comment ça
  marche* ; « multilingue » → *Scope* + langues d'install.

## Tradeoff & alternatives écartées

- **Restructuration / réécriture complète** : écartées par l'utilisateur (« resserrer »).
- **Retirer Word totalement du hero** : non — le produit ne tourne QUE dans Word
  aujourd'hui ; on mène avec la valeur mais on reste honnête (« disponible dans Word »).

## Conséquences

- **Fichier** : `docs/index.html` (markup FR + dict `I18N.en`, mêmes clés ; clés des
  sections coupées retirées). CSS inchangé (classes existantes réutilisées).
- **Pas de bump produit** : maj site seule (`deploy.sh site`).
- Voir mémoire `project-mathcursor-positioning` pour la ligne éditoriale.

## Validation post-livraison

Servi en local : hero recentré, plus de Fonctionnalités/Statut, toggle FR/EN sans clé
cassée, liens valides ; `grep` : plus de « Bêta 0.1 » / dates périmées. À confirmer en
prod après `deploy.sh site`.
