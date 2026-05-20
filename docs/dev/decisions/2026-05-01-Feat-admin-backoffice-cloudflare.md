# Feat — Backoffice admin (reports + stats) en ligne sur Cloudflare avec Basic Auth

**Date :** 2026-05-01
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

1. Migrer le dashboard "reports" (et plus tard "stats") en ligne sur
   Cloudflare Pages, sous un path `/admin/*` protégé par **Basic Auth**.
2. Architecture :
   - **Pages Function `_middleware.js`** sous `docs/functions/admin/` :
     check Basic Auth, refuse 401 + `WWW-Authenticate` si manquant.
   - **API proxy Functions** sous `docs/functions/admin/api/` qui appellent
     les API CF (R2, Analytics Engine) avec un token serveur — le browser
     ne voit jamais le token.
   - **Pages HTML statiques** sous `docs/admin/` qui fetch ces APIs en
     live (plus de regénération locale).
3. **UX reports** : liste master/detail.
   - À gauche/haut : liste des rapports triés par date desc, chaque ligne
     = "titre (= début du commentaire user) + date relative + indicateur
     screenshot".
   - Clic → panel détail avec les 3 champs formules + commentaire +
     screenshot inline + log collapsible (comme le dashboard local
     actuel).
4. **Secrets côté Pages** (à set 1 fois via dashboard CF) :
   - `ADMIN_USER` + `ADMIN_PASS` (basic auth)
   - `CLOUDFLARE_API_TOKEN_READ` (token séparé, scope `R2:Read` +
     `Analytics:Read` uniquement — pas d'Edit, principe least-privilege)
   - `CLOUDFLARE_ACCOUNT_ID`
5. **CLI Python local conservé** (`tools/cloudflare/reports.py`) : utile
   pour `delete` (RGPD) et debug. Le online + le local coexistent.

## Pourquoi

### Bénéfices

- **Toujours à jour** : pas de "regénère le HTML local" à faire avant de
  consulter.
- **Accessible mobile / autre machine** : un lien suffit, login natif
  browser.
- **Pas de secret CF distribué** : le token reste côté Pages Function,
  pas dans un fichier local synchronisé entre machines.
- **Master/detail propre** sur reports : l'UX "stack de cards" du
  dashboard local devient ingérable au-delà de ~30 rapports.

### Alternatives écartées

- **Cloudflare Access (Zero Trust SSO)** : plus propre (pas de
  password à partager), mais setup compte Zero Trust + policies = lourd
  pour un MVP perso. À reconsidérer si on ouvre l'admin à plus de
  testeurs.
- **Garder uniquement le CLI Python** : marche pour moi mais pas pour
  des collègues/profs beta-testeurs sans environment setup.
- **Hosting sur un VPS perso** : surcoût + maintenance, le free tier CF
  couvre largement le besoin.

### Free tier — vérifié

Volume estimé largement sous les limites free :
- Pages Functions (Workers free) : 100k req/jour, on est ~50/jour
- R2 ops : 1M writes/mois (LIST inclus), 10M reads/mois → ~500 ops/mois
- Analytics Engine : 10k SQL queries/jour → ~50/jour
- Cloudflare Access (si adopté plus tard) : free ≤ 50 users

## Statut + Citation

> on a moyen de faire que le backoffice de stats soit sur cloudflare
> sous un htaccess ou un login/mdp simple ? et du coup la mise à jour
> se fasse par la ? stats/reports
>
> oui je valide, et idealement le dashboard report j'aimerai une liste
> des reports avec un titre et une date (triée par date decroissante)
> et quand je clique je vois le truc

— come, 2026-05-01

## Conséquences / suivi

- Phasage : (1) auth + reports list/detail, (2) screenshot stream,
  (3) migration stats.
- Token CF dédié à créer (read-only). Pas réutiliser le token "Edit"
  global du projet (risque + principle of least privilege).
- À documenter : guide setup secrets dans `tools/cloudflare/README.md`.
- Suppression d'un report depuis le online → garder en CLI Python
  uniquement (RGPD, action destructive, pas d'UX prevention complète
  en HTML de toute façon).
