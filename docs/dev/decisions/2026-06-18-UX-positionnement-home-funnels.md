# UX — Positionnement : home capability-neutral + funnels prof / pap-dys séparés

**Date :** 2026-06-18
**Kind :** UX
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-16-UX-homepage-reposition.md](2026-06-16-UX-homepage-reposition.md) (poursuit le recentrage « sans friction »), [2026-06-18-Feat-usage-counter-telemetry.md](2026-06-18-Feat-usage-counter-telemetry.md) (implémente la réécriture comm honnête qu'il exigeait), mémoire `project-mathcursor-positioning`

## Citation acté

> « j'aimerai rechallenger le site toujours EN/FR ! » + brief de positionnement, puis,
> sur le périmètre : « Tout d'un coup », H1 « Écris les maths comme elles te viennent »,
> et sur le pilier confidentialité : « honnête et cadré, après on n'est pas obligé de le
> dire au centre de la page :D » — utilisateur, 2026-06-18 (plan approuvé en plan mode)

## Contexte

Un brief de positionnement (origine claude.ai) pose une **règle non négociable** :
séparation stricte des deux audiences (profs ↔ familles à accommodation PAP/dys), pour
éviter la stigmatisation mutuelle. Or la home `docs/index.html` viole frontalement cette
règle :

- **PAP/dys mentionné 4×** : `<meta description>`, carte « Lycéens » (`for_2_p`), carte
  « Parents » (`for_3_p`), bio auteur (`bio_p1`) ;
- la section **« Pour qui »** mélange prof + élève + parent dans un même fil de lecture ;
- la home **mène avec la vitesse** (« vitesse de frappe » dans le lead + meta), alors que
  le brief veut la vitesse *prouvée dans la démo*, jamais plaidée (effet « champion de
  Rubik's cube » qui aliène).

Second point dur, indépendant du brief : l'ADR du jour
[usage-counter-telemetry](2026-06-18-Feat-usage-counter-telemetry.md) a acté un **compteur
d'usage anonyme opt-out**, et exigeait une réécriture honnête de la home + `privacy.html`
(encore intactes : elles promettent toujours « 100 % local / sans tracking / ni de
télémétrie »). Le brief, lui, demandait un pilier « rien ne quitte ton ordinateur » — ce
qui serait désormais **faux**. La décision tranche pour l'honnêteté.

## Décision

**1. Home `docs/index.html` rendue capability-neutral, au tutoiement.**
- H1 « Écris les maths comme elles te viennent », sous-titre recentré sur la libération
  cognitive (« tape comme ça vient, on propose, tu valides »), **sans « vitesse »**.
- **CTA primaire unique** « Essayer dans le navigateur » → `demo/` ; « Installer pour
  Word » démoté en secondaire.
- **4 piliers** : Écris relâché · Du vrai Word (pas une image) · Gratuit et respectueux ·
  Vois par toi-même (CTA démo répété).
- **Section « Pour qui » supprimée** (vecteur du mélange d'audiences) ; ses messages
  migrent vers les funnels.
- **Aucune mention PAP/dys/handicap** nulle part sur la home (meta, copy, bio).

**2. Deux funnels dédiés, jamais reliés visuellement à la home ni entre eux** (§2 du brief) :
- `docs/prof/index.html` — vouvoiement « entre pairs », angle gain de temps prof, zéro
  stigmate ; CTA démo + demande d'avis.
- `docs/pap-dys/index.html` — vouvoiement (parents + soignants), cadre **compensation**
  (charge cognitive / mémoire de travail / fatigue graphomotrice) ; `noindex`, **non liée
  depuis aucune nav** (atteinte uniquement par les campagnes mail).
- Accès aux funnels = liens directs dans les mails ciblés (hors périmètre de cette passe).

**3. Comm confidentialité honnête (solde l'ADR télémétrie).**
- Pilier « Gratuit et respectueux » : gratuit, sans pub, sans compte ; les documents et la
  frappe ne quittent pas l'ordinateur ; **mention légère** du compteur anonyme désactivable
  (pas au centre de la page — détail complet en FAQ + `privacy.html`).
- Badges/footer : retirer « 100 % local » et « sans tracking » (contredits par le
  compteur), garder « sans compte » + « sans pub ».
- `privacy.html` (FR+EN) réécrit : décrit honnêtement le compteur (un entier, sans contenu
  ni identifiant, opt-out).

**4. Claims §7 non vérifiés laissés hors ligne** : compat NVDA de la sortie OMath + compat
ruban Cartable Fantastique **omises** de `/pap-dys` (commentaire `TODO §7` pour réintégration
après vérification).

**5. Nom produit** : « MathCursor » gardé en dur (renommage = find/replace global ultérieur,
gate domaine/branding). Mails (§8) hors périmètre.

## Tradeoff & alternatives écartées

- **Garder une seule home tout-public avec « Pour qui »** : rejeté — c'est précisément ce
  qui stigmatise (un prof n'envoie pas une page « pour dys » à sa classe ; une famille ne se
  reconnaît pas dans une page « pour profs »). La séparation est la valeur défendue.
- **Mettre la vitesse dans le H1** (« à la vitesse où tu les penses ») : rejeté — la vitesse
  annoncée aliène ; elle se prouve dans la démo. CTA démo unique = le levier.
- **Garder « rien ne quitte ton ordinateur » / « 100 % local »** : rejeté — faux depuis le
  compteur d'usage, et contredit la position prise le jour même par l'utilisateur.
- **Lier les funnels depuis la home** (nav ou footer) : rejeté — viole le cloisonnement §2.
- **Page `/pap-dys` entièrement hors ligne jusqu'à vérif** : rejeté au profit d'une mise en
  ligne sans les claims non vérifiés (le §7 interdit les *affirmations*, pas la page).

## Conséquences

- **Fichiers** : `docs/index.html` (réécriture contenu + dict `I18N.en`), nouveaux
  `docs/prof/index.html` et `docs/pap-dys/index.html`, `docs/privacy.html` (FR+EN réécrit).
  `docs/style.css` a priori inchangé (vocabulaire de classes existant suffisant).
- **SEO** : `/pap-dys` en `noindex,nofollow` ; non référencée par la nav interne.
- **Cloisonnement** : aucun lien `/prof`↔`/pap-dys`, aucun lien funnel→home grand public au
  sens « même fil ».
- **Comm** : la posture passe de « privacy first » à « gratuit, données minimales, compteur
  anonyme désactivable » — cohérente entre home, FAQ, `privacy.html` et binaire.
- **Tests** : aucun (site statique).
- **Règles MC impactées** : aucune.

## Validation post-fix

1. `grep -i "PAP\|dys\|accommodation\|handicap" docs/index.html` → **0** résultat.
2. H1 + sous-titre + meta de la home sans « vitesse / rapide / speed / fast ».
3. Aucun lien `/prof` ni `/pap-dys` dans `index.html` ; aucun lien croisé entre funnels.
4. `/pap-dys` : balise `noindex` présente, 2 claims §7 absents (commentaire TODO en place).
5. Bascule FR↔EN sur les 4 pages : chaque `data-i18n` a son entrée EN.
6. Home / FAQ / `privacy.html` : aucune phrase ne contredit le compteur d'usage.
