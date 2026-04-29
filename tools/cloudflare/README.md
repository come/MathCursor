# Cloudflare — déploiement et administration

Setup des credentials, déploiement du site `mathcursor.pages.dev` et upload
d'installers dans R2.

## Setup initial (une fois)

1. Crée un API token Cloudflare avec les scopes minimaux :
   - Account → Workers R2 Storage → **Edit**
   - Account → Cloudflare Pages → **Edit**
   - Account → Workers Scripts → **Edit**
   - Account → Account Analytics → **Read**

   Limite les resources à ton compte spécifique (pas "All accounts"). Mets une
   expiration raisonnable (mois, pas années).

2. Ouvre `~/.mathcursor/cloudflare.env` et remplace la valeur de
   `CLOUDFLARE_API_TOKEN` par ton token. L'account ID (`e625a4f69...`) est
   déjà rempli.

   Ce fichier est **hors du repo git** et protégé par `.gitignore` global sur
   `*.env`. Ne le copie jamais dans le repo.

3. Vérifie que ça marche :
   ```bash
   source ~/.mathcursor/cloudflare.env
   npx wrangler whoami
   ```

## Déploiement du site

```bash
tools/cloudflare/deploy.sh site
```

Pousse tout `docs/` (HTML, CSS, Pages Functions) vers Cloudflare Pages.
Le projet est `mathcursor`, branche de production `main`.

## Upload d'un nouvel installer

```bash
tools/cloudflare/deploy.sh installer 0.4.0
```

Balance `MathCursor-Setup-0.4.0.exe` dans le bucket R2 `mathcursor-releases`.
Après l'upload :

1. Bump `LATEST_VERSION` dans `docs/functions/download/[[filename]].js`
2. Ajoute l'entrée `<article class="card">` dans `docs/releases.html`
3. `tools/cloudflare/deploy.sh site`

## Architecture hébergée

| Ressource | Rôle |
|-----------|------|
| Pages project `mathcursor` | Sert le site statique sur `mathcursor.pages.dev` |
| R2 bucket `mathcursor-releases` | Stockage des `.exe` (trop gros pour Pages) |
| Pages Function `/download/*` | Log + stream depuis R2 vers le client |
| AE dataset `mathcursor_downloads` | Métriques par download |

Détail de l'architecture : voir ADR
[`docs/dev/decisions/2026-04-24-Feat-cloudflare-deployment.md`](../../docs/dev/decisions/2026-04-24-Feat-cloudflare-deployment.md).

## Requêtes stats (Analytics Engine)

```bash
source ~/.mathcursor/cloudflare.env
curl -s -H "Authorization: Bearer $CLOUDFLARE_API_TOKEN" \
     -H "Content-Type: application/sql" \
     -X POST "https://api.cloudflare.com/client/v4/accounts/$CLOUDFLARE_ACCOUNT_ID/analytics_engine/sql" \
     -d "SELECT blob1 AS file, blob3 AS country, count() AS hits
         FROM mathcursor_downloads
         WHERE timestamp > now() - INTERVAL '7' DAY
         GROUP BY file, country
         ORDER BY hits DESC"
```

Schéma des blobs écrits par la Function :
- `blob1` : nom de fichier résolu (ex. `MathCursor-Setup-0.3.0.exe`)
- `blob2` : ce qu'a tapé le client (ex. `latest.exe` ou le nom versionné)
- `blob3` : pays (`FR`, `US`, ...)
- `blob4` : datacenter CF (colo, ex. `MRS`)
- `blob5` : user-agent (tronqué à 200 chars)
- `blob6` : referer
- `double1` : taille du fichier en bytes
- `index1` : filename (pour filtrage SQL rapide)

## Sécurité

- Ne **jamais** committer le token. `.gitignore` bloque déjà `*.env`.
- Si le token apparaît quelque part par accident (historique chat, logs),
  **revoke-le immédiatement** via https://dash.cloudflare.com/profile/api-tokens.
- Régénérer un nouveau token est rapide — pas de raison de garder un token
  compromis "au cas où".
