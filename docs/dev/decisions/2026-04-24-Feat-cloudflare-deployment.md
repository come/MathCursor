# Feat — Déploiement Cloudflare Pages + R2 + Analytics Engine

**Date :** 2026-04-24
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Site public `mathcursor.pages.dev` hébergé via **Cloudflare Pages**. Les
installers (~200 MB chacun, au-dessus de la limite 25 MB de Pages) vivent
dans un bucket **R2** `mathcursor-releases`. Une **Pages Function** sur
`/download/[[filename]]` log un datapoint dans **Analytics Engine** (dataset
`mathcursor_downloads`) puis stream le fichier depuis R2 vers le client
(avec support des Range requests pour le resume de download).

## Pourquoi

- Premier besoin : **stats de download** par version / date / pays — pour
  piloter la beta (savoir combien de testeurs ont vraiment installé, quelle
  version est en cours, d'où viennent les gens).
- Alternatives écartées :
  - **GitHub Releases** : gratuit, stable, mais stats chiches (un compteur
    global par asset, pas de pays ni de time-series) et nécessite repo public
    alors que la décision open-source n'est pas tranchée (cf.
    `project_business_model` mémoire).
  - **Pages seul sans R2** : les installeurs font 206 MB, limite Pages = 25 MB
    par asset. Bloquant direct.
  - **Backblaze B2 + link** : ok techniquement, mais fragmente la stack (deux
    providers). Cloudflare gratuit et suffisant.
- Cloudflare free tier largement suffisant pour nos volumes :
  - R2 : 10 GB storage, 10M Class B ops/mois (reads). 3 installeurs × 206 MB
    = ~620 MB, à des années de saturer le storage.
  - Pages : bande passante illimitée sur Pages et sur le Worker, 100 000
    requêtes Function/jour en free (très en dessous de ce qu'on générera).
  - Analytics Engine : 10M datapoints/mois, amplement couvert.

## Architecture

```
Browser
   │
   │ HTTP GET / HTTP GET Range
   ▼
┌─────────────────────────────────────┐
│  mathcursor.pages.dev (CF Pages)    │
│  ─ index.html / releases.html       │
│  ─ functions/download/[[filename]]  ├──── writeDataPoint ──► AE dataset
└─────────────┬───────────────────────┘                       mathcursor_downloads
              │ env.RELEASES.get(key, {range})
              ▼
        R2: mathcursor-releases
              ├── MathCursor-Setup-0.1.0.exe
              ├── MathCursor-Setup-0.2.0.exe
              └── MathCursor-Setup-0.3.0.exe
```

**Alias `latest.exe`** : résolution côté Function via une constante
`LATEST_VERSION` (hardcoded dans le code de la Function, bumpée à chaque
release et redéployée). Simple et explicite. Migration possible plus tard
vers un JSON `latest.json` dans R2 si on veut mettre à jour l'alias sans
redeploy de la Function.

**Range requests** : supportés pour permettre la reprise de download côté
client (utile sur les 200 MB si connexion précaire).

## Schéma des datapoints AE

| Champ | Contenu |
|-------|---------|
| `blob1` | Nom de fichier résolu (`MathCursor-Setup-0.3.0.exe`) |
| `blob2` | Ce qu'a tapé le client (`latest.exe` ou nom versionné) |
| `blob3` | Pays (`FR`, `US`, ...) depuis `request.cf.country` |
| `blob4` | Datacenter Cloudflare (`MRS`, ...) |
| `blob5` | User-agent (tronqué à 200 chars) |
| `blob6` | Referer |
| `double1` | Taille du fichier en bytes |
| `index1` | Filename (pour filtrage rapide) |

## Conséquences

- Nouveaux fichiers dans le repo :
  - `docs/functions/download/[[filename]].js` : la Pages Function.
  - `docs/wrangler.toml` : bindings R2 + AE pour Pages.
  - `docs/releases.html` : page de téléchargement avec historique des versions.
  - `tools/cloudflare/deploy.sh` + `README.md` : scripts + doc de
    déploiement.
- Modification `docs/index.html` : lien `release/setup.exe` (cassé) remplacé
  par `/download/latest.exe` ; nouveau lien nav "Téléchargements".
- Modification `.gitignore` : bloque `*.env` pour éviter fuite de credentials.
- Credential local : `~/.mathcursor/cloudflare.env` (hors repo) avec le
  `CLOUDFLARE_API_TOKEN` et `CLOUDFLARE_ACCOUNT_ID`.
- Process release bumper →
  1. Build installer
  2. `tools/cloudflare/deploy.sh installer <version>`
  3. Bump `LATEST_VERSION` dans la Function + entrée dans `releases.html`
  4. `tools/cloudflare/deploy.sh site`

## Prérequis d'activation Cloudflare (une seule fois par compte)

Flags qui ne s'activent pas via l'API — doivent passer par le dashboard :

1. **R2** : accepter les CGU depuis `/r2/overview`.
2. **Workers & Pages** : visiter `/workers-and-pages` pour créer un
   subdomain `.workers.dev` (prérequis implicite d'Analytics Engine).
3. **Analytics Engine dataset** : créer manuellement le dataset
   `mathcursor_downloads` avec binding `DOWNLOADS` via le dashboard
   (Workers → Analytics Engine → Create Dataset). Le binding seul dans
   `wrangler.toml` ne suffit pas à la création automatique en mode Pages.

Documenté dans `tools/cloudflare/README.md`.

## Alternatives futures (si on change d'avis)

- **Open-sourcer le repo** → ajouter GitHub Releases en parallèle pour la
  visibilité publique (pas en remplacement, juste un miroir). Ce choix
  dépend de la décision business-model en attente.
- **Custom domain** (ex. `mathcursor.app` ou `.fr`) → DNS CNAME vers la
  projet Pages. Coût : ~10 €/an pour le domaine. Marginal.
- **Dashboard de stats** → un petit endpoint `/stats` qui lit AE via SQL API
  et rend un graphique Chart.js. Quand tu auras assez de trafic pour que ce
  soit intéressant.

## Validé par l'utilisateur

Choix initial de la plateforme :
> "sur la partie site web qu'on a faite, j'aimerai l'envoyer sur cloudflare
> pour pouvoir avoir des stats de download, j'ai cree mon commpte cloudflare"

Choix R2 (face à GitHub Releases) lorsque la limite 25 MB de Pages est
remontée :
> "A oui, j'ai moyen de te filer un PAT pour que tu te debrouilles ou je dois
> faire des choses ?"

(Option A = R2, acceptée. Le PAT a été fourni puis utilisé pour exécuter
le setup. À révoquer dès que ce setup session est archivé.)

Confirmation d'ajout de la page releases :
> "tu peux ajouter une page avec les releases downloadables ? et go"

## Statut

acté. Site live sur `mathcursor.pages.dev`, 3 installers uploadés, Function
déployée, AE log fonctionnel (validé par requête SQL).
