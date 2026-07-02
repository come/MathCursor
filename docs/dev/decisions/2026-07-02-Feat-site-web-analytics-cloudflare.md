# Feat — Analytics du site vitrine via Cloudflare Web Analytics (beacon cookieless)

**Date :** 2026-07-02
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-04-24-Feat-cloudflare-deployment.md](2026-04-24-Feat-cloudflare-deployment.md) (Pages + R2 + Analytics Engine des downloads), [2026-05-01-Feat-admin-backoffice-cloudflare.md](2026-05-01-Feat-admin-backoffice-cloudflare.md) (back-office `/admin`), `docs/index.html`, `docs/amenagement.html`

## Citation acté

> « la c'est juste des stats sur le site que je souhaiterai » — utilisateur, 2026-07-02

> « on a enlevé 0 telemetrie normalement, je prefere B » — utilisateur, 2026-07-02

## Contexte

Le site expose déjà les **referers des téléchargements** (log server-side dans
Analytics Engine, vue `/admin`, ADR 2026-04-24). Mais il n'y avait pas de vue
consolidée de la **fréquentation des pages**, de l'usage de la démo (`/demo/`),
ni des referers des **visiteurs** (d'où viennent les gens qui arrivent).

L'utilisateur veut ces stats **de site**. Point clé de cadrage explicite : c'est
distinct de la promesse **produit** « add-in Word 100 % local, zéro télémétrie ».
Ces claims portent sur le **logiciel** qui tourne sur la machine, pas sur le site
marketing. Mesurer la fréquentation du site vitrine ne les contredit pas.

Découverte en cours de décision : **Cloudflare Web Analytics est déjà actif**
depuis ~2 mois (site créé **via l'intégration Cloudflare Pages**, hôtes
`mathcursor.pages.dev` / `mathcursor.com` / `www.mathcursor.com`) et **collecte
déjà** (163 pages vues / 68 visites sur 24 h au 2026-07-02). Le beacon est
**auto-injecté par Cloudflare** au niveau du projet Pages : **aucun snippet dans
le repo** (`grep cloudflareinsights|beacon` sur `docs/` = 0). Cette ADR **acte et
documente l'existant** plutôt que d'introduire une nouveauté.

## Décision

Conserver **Cloudflare Web Analytics** (beacon JS **cookieless**, privacy-first,
sans consentement requis) comme solution de stats du **site vitrine** (projet
Pages `mathcursor`, toutes les pages `docs/` dont `/demo/`), déjà en place.

Fournit : fréquentation par page (dont `/demo/`), referers des visiteurs, pays,
filtrage bots — via le dashboard Cloudflare Web Analytics (**Manage site**),
sans code à maintenir.

Mise en place effective : **auto-injection au niveau du projet Pages** (Cloudflare
insère le beacon sur toutes les réponses HTML) — pas de snippet versionné.

Le **logiciel reste à zéro télémétrie** — inchangé, aucun impact.

## Tradeoff & alternatives écartées

- **Option A — log server-side dans Analytics Engine** (étendre le middleware
  Pages, comme les downloads) : cohérent avec l'infra existante et 100 % sans
  script client, mais demande plus de code (middleware + vue `/admin`) et
  **compte les bots** (le serveur voit toutes les requêtes, y compris crawlers).
  Écartée au profit de B, turnkey et avec filtrage bots intégré.
- **Ne rien faire / dashboard Pages natif** : referers pauvres, pas de vue
  `/demo/` dédiée. Insuffisant pour le besoin.

## Conséquences

- **Copy / vie privée** : Web Analytics est **cookieless** → « aucun cookie »
  reste littéralement vrai côté site. Point de vigilance : la FAQ
  (`index.html`, `faq_1_a`) et `privacy.html` disent « aucun traceur » / « pas de
  statistiques d'usage ». Ces phrases sont **scoped logiciel** (question « Mes
  données sont-elles envoyées sur Internet ? »), mais un visiteur pourrait les
  lire comme site-wide. À surveiller ; ajuster la copy si le besoin de clarté se
  confirme (distinguer explicitement *produit* vs *site*).
- **Infra** : le retrait de « sans tracking » du footer (fait le 2026-07-02) va
  déjà dans ce sens.
- **Réversibilité** : molle — désactivable en un clic dans le dashboard Pages.

## Validation post-fix

Déjà validé par l'existant : le dashboard Web Analytics remonte 163 pages vues /
68 visites sur 24 h au 2026-07-02. Pour l'usage : **Manage site** → régler la
plage (24 h / 7 j / 30 j) → **Paths** pour la fréquentation par page (repérer
`/demo/`) et **Referrers** pour la provenance des visiteurs.
