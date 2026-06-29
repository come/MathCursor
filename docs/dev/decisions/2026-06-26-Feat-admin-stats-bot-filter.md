# Feat — Filtre « masquer les bots » sur le dashboard stats admin

**Date :** 2026-06-26
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-04-24-Feat-cloudflare-deployment.md](2026-04-24-Feat-cloudflare-deployment.md), `docs/functions/admin/api/stats.js`, `docs/admin/stats.html`, `tools/cloudflare/README.md` (§Requêtes stats)

## Citation acté

> « oui je veux bien le filtre » — utilisateur, 2026-06-26
> (en réponse à l'analyse montrant que le total brut des téléchargements est
> très majoritairement du trafic de bots)

## Contexte

Le compteur de téléchargements (Analytics Engine `mathcursor_downloads`,
exposé par `/admin/api/stats` → `/admin/stats.html`) affiche le **total brut**.
Une analyse du dataset (2026-06-26) montre que sur 90 jours, ~36 % des hits sont
des bots *déclarés* — dont un scraper dominant : un seul UA `Linux x86_64 /
Chrome 116.0.0.0` figé pesant ≈25 % du trafic — plus crawlers IA (GPTBot,
ClaudeBot), `curl`, `okhttp`, `HeadlessChrome`. Le chiffre brut surévalue donc
fortement l'adoption réelle et induit en erreur pour piloter la beta.

## Décision

Ajouter un mode **« Masquer les bots »** au dashboard stats, **actif par
défaut** (la vue utile = le signal humain ; le brut reste accessible en
décochant).

- **Heuristique serveur** : une expression SQL `BOT_MATCH` partagée dans
  `stats.js`, TRUE pour une ligne bot — UA s'annonçant comme tel
  (`bot`/`crawl`/`spider`/`slurp`), clients HTTP non-navigateur
  (`curl`/`wget`/`okhttp`/`python`/`go-http`/`java`/`headless`), UA vide, et la
  signature du scraper dominant (`Linux x86_64` + `Chrome/116.0.0.0`).
- **Injection** : un fragment ` AND NOT (BOT_MATCH)` ajouté au `WHERE` des 6
  agrégats (total / versions / pays / jours / referers / 10 derniers) quand
  `?nobots=1` (défaut). `?nobots=0` rétablit le brut.
- **Comptage transparent + ventilation par type** : une requête (toujours
  exécutée) groupe les bots exclus par **type** (`UA vide` / `Scraper
  (Linux/Chrome 116)` / `Client HTTP / script` / `Crawler / bot`) → l'UI affiche
  « N bots masqués sur M » + le détail par type sous le total. La catégorie est
  un `if()` imbriqué (AE n'a ni `multiIf` ni `CASE WHEN`).
- **Cache** : la clé de cache edge (60 s) varie selon le mode, sinon le brut
  servirait le filtré ou l'inverse.
- **Liste volontairement serrée** (faux négatifs assumés > faux positifs) : on
  ne filtre **pas** les UA de navigateurs *usurpés* (vieux Chrome figés,
  mobiles éparpillés hors-cible des pics) car ils sont indiscernables d'un vrai
  utilisateur sans signal géo/ASN/débit — et le public cible (lycéens, parfois
  vieux PC) ne doit jamais être écarté à tort.

Couche cible : **backend Cloudflare Pages + dashboard admin** uniquement. Aucune
donnée nouvelle collectée (mêmes blobs), aucun impact sur le binaire, le moteur
ou les contrats. La section « Usage produit » (compteur de formules) n'est pas
concernée par le toggle.

## Tradeoff & alternatives écartées

- **Filtrer côté navigateur (JS)** : écarté — il faudrait rapatrier les lignes
  brutes ; l'agrégation se fait déjà en SQL côté AE, le filtre y a sa place.
- **Filtre toujours actif, sans toggle** : écarté — perdre l'accès au brut
  empêche de vérifier l'heuristique et de voir l'ampleur réelle des bots.
- **Heuristique large (exclure vieux Chrome / mobiles hors-FR)** : écarté pour
  le v1 — risque d'écarter de vrais utilisateurs (cible = aussi de vieux PC) ;
  un UA-only conservateur est défendable et sans faux positif.
- **Classement bot/humain via géo + ASN + cadence** : écarté — signal plus
  robuste mais lourd ; réversible si le besoin de précision grandit.

## Conséquences

- `docs/functions/admin/api/stats.js` : const `BOT_MATCH` + `BOT_CATEGORY`,
  param `nobots` (défaut on), injection dans les 6 queries, query de ventilation
  par type, clé de cache par mode, champs `nobots` + `bots_excluded` +
  `bot_types` dans la réponse.
- `docs/admin/stats.html` : case « Masquer les bots » (cochée), sous-titre
  « N bots masqués sur M » + liste du détail par type sous le Total, recharge au
  toggle.
- **Limite documentée** : l'heuristique attrape les bots déclarés, pas les UA
  navigateurs usurpés — le sous-titre ne prétend donc pas à l'exhaustivité.
- Aucune migration, aucun nouveau binding, aucun secret.

## Validation post-fix

1. `/admin/stats.html` charge avec la case cochée → total = humains, sous-titre
   « N bots masqués sur M ». Décocher → total brut, « dont N bots ».
2. Requête composée validée contre AE (fenêtre 30 j) : filtré + bots = brut
   (141 + 20 = 161) ; syntaxe acceptée (AE n'a ni `match` ni
   `positionCaseInsensitive` → `lower()` + `LIKE` uniquement).
3. `?refresh=1` bypass le cache ; les deux modes ont des entrées de cache
   distinctes.
