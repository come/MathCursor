# Feat — Formulaire de contact sur le site (réception R2 + vue admin)

**Date :** 2026-06-16
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Lié à :** `docs/contact.html`, fonction `docs/functions/api/v1/contact.js`,
admin `docs/admin/contacts.html`, pattern existant `/api/v1/report` +
`/admin/reports.html`

## Citation acté

> « est ce que tu peux faire un formulaire de contact sur le site ? pour tous
> les liens contacts ;) » — utilisateur, 2026-06-16. Décisions de cadrage (plan
> mode) : réception **R2 + admin** (Resend écarté), **page dédiée
> `/contact.html`**.

## Contexte

Tous les liens « Contact » du site étaient des `mailto:` (`come2percin@gmail.com`
côté public, `come2percin@wanadev.fr` côté privacy). Un `mailto:` suppose un
client mail configuré — friction réelle, surtout sur mobile / en navigation web.

## Décision

Un vrai formulaire de contact, vers lequel pointent tous les liens « Contact ».

- **Réception = stockage R2 + vue admin.** La fonction `POST /api/v1/contact`
  range chaque message dans le bucket `mathcursor-reports` sous le préfixe
  `contacts/<YYYY-MM-DD>/<uuid>.json` (les rapports sont sous `reports/`). Calque
  exact de `report.js` : CORS + preflight, rate-limit KV *optionnel*
  (`RATE_LIMIT_KV`, clé `rl:contact:`), gardes de taille, métadonnées `_server`
  (pays/colo/UA, **pas d'IP en clair**). Anti-bot = **honeypot** (champ caché
  `website` ; rempli → `200 {ok}` silencieux, rien stocké). Aucun email sortant.
- **Page dédiée `/contact.html`** (squelette `privacy.html` : header/footer +
  i18n FR capturé du markup, EN dans `I18N.en`). Champs : email (requis, pour
  répondre), sujet (optionnel), message (requis). Envoi `fetch` sans reload,
  message succès/erreur inline. **Fallback** `mailto:` sous le formulaire si JS /
  backend tombe.
- **Vue admin** `/admin/contacts.html` (+ `api/contacts/list.js` & `get.js`,
  calques de `reports/`) sous le Basic Auth existant. Tuile ajoutée à
  `/admin/index.html`.
- **Tous les liens « Contact »** (footers des 3 pages + démo, `inst_note` et FAQ
  de l'accueil, `contact_p` de privacy) pointent vers `/contact.html`. Le seul
  `mailto:` conservé : `bio_p3` de l'accueil (« un email direct » — point humain
  assumé) + le fallback de la page contact.

## Tradeoff & alternatives écartées

- **Envoi email (Resend / MailChannels)** : arriverait direct dans l'inbox, mais
  exige un compte tiers + clé API en secret Pages + vérif domaine. Écarté : ajoute
  une dépendance externe pour une beta à faible trafic, alors que l'infra R2+admin
  existe déjà et ne perd aucun message.
- **Formulaire `mailto:`-builder (sans backend)** : reproduit la friction qu'on
  veut supprimer (dépend d'un client mail).
- **Captcha / Turnstile** : honeypot + longueur mini suffisent pour le volume
  actuel. Turnstile = évolution future si spam.

## Conséquences

- **Fichiers** : `docs/contact.html`, `docs/functions/api/v1/contact.js`,
  `docs/functions/admin/api/contacts/{list,get}.js`, `docs/admin/contacts.html`,
  + édits `docs/style.css` (styles `.form-*`, aucun n'existait), `docs/index.html`,
  `docs/releases.html`, `docs/privacy.html` (liens + mention RGPD), `docs/demo/`
  ET sa source `web-demo/…/wwwroot/index.html` (sinon écrasé au rebuild WASM),
  `docs/admin/index.html`.
- **Données perso nouvelles** : l'email du contact est stocké (pour répondre).
  Mention ajoutée à `privacy.html`.
- **Pas de bump de version produit** : maj site seule (aucun rebuild installer),
  déploiement via `tools/cloudflare/deploy.sh site`.
- **Binding** : `REPORTS_BUCKET` déjà bindé, `CLOUDFLARE_API_TOKEN_READ` déjà
  configuré (l'admin liste le même bucket). Zéro nouveau secret.

## Validation post-livraison

`curl POST /api/v1/contact` (email+message valides) → `{ok,id}` + JSON visible
sous `contacts/` dans R2 ; message vide → 400 ; honeypot rempli → 200 sans
stockage ; `/admin/contacts.html` affiche le message ; chaque lien « Contact »
mène à `/contact.html` ; toggle FR/EN OK. À confirmer en prod après `deploy.sh
site`.
