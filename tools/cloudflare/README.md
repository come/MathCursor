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

## Publier un VSIX VS Code multiplateforme

L'extension VS Code = **UNE extension, N VSIX `--target`** (cf. ADR
2026-06-25-Feat-vscode-marketplace-publishing-model). Les binaires Rust sont
spécifiques à l'OS → chaque cible se construit sur son runner. Distribution
**depuis le site** (R2 + `/download/*`), comme le `.exe` Word, **non signée**
(palier alpha). Flux de bout en bout :

1. **Bump** la version dans `adapter-vscode/extension/package.json`.
2. **Construire** les 3 VSIX : lancer le workflow `vscode-vsix` en
   **`workflow_dispatch`** (sinon un push ne build que Windows + Linux ; le
   dispatch ajoute macOS `darwin-arm64`).
3. **Télécharger** les 3 artifacts (`vsix-win32-x64`, `vsix-linux-x64`,
   `vsix-darwin-arm64`) dans un dossier local.
4. **Uploader** dans R2 :
   ```bash
   tools/cloudflare/deploy.sh vsix <version> ~/Downloads/vsix
   ```
   (renomme chaque `.vsix` en `mathcursor-<target>-<version>.vsix` dans le bucket
   `mathcursor-releases` ; une cible absente est ignorée.)
5. **Bump** la map `LATEST_VSCODE_VSIX` dans `docs/functions/_latest.js` (les noms
   versionnés des 3 cibles).
6. **Vérifier** les 3 boutons (`latest-<target>.vsix`) dans `docs/releases.html`.
7. **Re-déployer** le site : `tools/cloudflare/deploy.sh site`.

Vérif post-deploy : `curl -sI https://mathcursor.pages.dev/download/latest-linux-x64.vsix`
→ 200 ; `latest.vsix` reste la cible `win32-x64` (rétro-compat).

## Publier l'extension LibreOffice (.oxt)

⚠️ **Le nom de fichier `.oxt` est volontairement NON versionné : toujours
`MathCursor.oxt`.** Les URI de script bundlées dans l'extension (`Addons.xcu`,
`jobs.py`) sont figées sur ce nom, et LibreOffice clé son package par le **nom de
fichier installé** (`pythonscript.py`). Un nom versionné (`MathCursor-0.1.0.oxt`)
casse le lookup → `KeyError: 'MathCursor.oxt'` à chaque clic de menu / auto-détection,
pour tous les téléchargements du site. La **version** vit dans
`libreoffice-ext/oxt/description.xml` (`<version>`), jamais dans le nom de fichier.
Cf. ADR 2026-06-29-Fix-oxt-stable-filename-distribution.

Flux :

1. **Construire** l'oxt : `python libreoffice-ext/build_oxt.py` → `libreoffice-ext/MathCursor.oxt`.
   (Le build produit toujours le bon nom ; ne le renomme jamais.)
2. **Uploader** sous le nom stable :
   ```bash
   tools/cloudflare/deploy.sh oxt
   ```
   (Écrase l'objet `mathcursor-releases/MathCursor.oxt` — pas de version dans la clé,
   donc pas de cleanup à faire ; la nouvelle release remplace simplement l'ancienne.)
3. `LATEST_OXT` dans `docs/functions/_latest.js` reste `"MathCursor.oxt"` — **ne jamais
   le versionner**. Re-déployer le site uniquement si `_latest.js` a changé par ailleurs.

Vérif post-deploy (le `Content-Disposition` n'apparaît qu'en GET, pas en HEAD) :
```bash
curl -s -r 0-0 -D - -o /dev/null https://mathcursor.com/download/latest.oxt \
  | grep -iE "HTTP/|content-disposition"
# attendu : 206 + Content-Disposition: attachment; filename="MathCursor.oxt"
```

## Architecture hébergée

| Ressource | Rôle |
|-----------|------|
| Pages project `mathcursor` | Sert le site statique sur `mathcursor.pages.dev` |
| R2 bucket `mathcursor-releases` | Stockage des `.exe` (trop gros pour Pages) |
| R2 bucket `mathcursor-reports` | Stockage des rapports "Signaler une erreur" (1 JSON + 1 PNG par bug) |
| KV namespace `RATE_LIMIT_KV` | Compteur reports/IP/heure (anti-flood) |
| Pages Function `/download/*` | Log + stream depuis R2 vers le client |
| Pages Function `/api/v1/report` | Reçoit les rapports de bug, écrit dans R2 + KV |
| Pages Function `/admin/_middleware.js` | Basic Auth gate sur `/admin/*` |
| Pages Function `/admin/api/reports/*` | Proxies R2 pour le dashboard admin online |
| Pages Function `/admin/api/stats` | Proxy Analytics Engine SQL pour le dashboard stats |
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

## Setup backend "Signaler une erreur" (one-shot)

L'endpoint `POST /api/v1/report` (cf.
`docs/functions/api/v1/report.js`) écrit dans le bucket R2
`mathcursor-reports` et utilise la KV `RATE_LIMIT_KV` pour le
rate-limit. À créer une fois (commandes wrangler) :

```bash
source ~/.mathcursor/cloudflare.env

# 1) Bucket R2 pour les rapports
npx wrangler r2 bucket create mathcursor-reports

# 2) KV namespace pour le rate limit
#    Note l'ID retourné, à mettre dans le binding Pages
npx wrangler kv namespace create RATE_LIMIT_KV
```

Puis dans le dashboard Cloudflare Pages → projet `mathcursor` →
Settings → Functions → ajouter les bindings (ou via wrangler CLI si
disponible pour Pages) :

| Variable | Type | Cible |
|---|---|---|
| `REPORTS_BUCKET` | R2 bucket | `mathcursor-reports` |
| `RATE_LIMIT_KV` | KV namespace | l'ID retourné par la commande ci-dessus |

Une fois bindés, redéployer (`tools/cloudflare/deploy.sh site`) puis
tester :

```bash
# Test manuel POST — doit répondre {"ok": true, "id": "..."}
curl -X POST https://mathcursor.pages.dev/api/v1/report \
  -H "Content-Type: application/json" \
  -d '{
    "version": "0.5.3",
    "ts": "2026-04-30T14:30:00Z",
    "source_text": "test source",
    "user_comment": "test depuis curl"
  }'

# Lire un report précis (syntaxe bucket/key combinée + flag --remote)
npx wrangler r2 object get mathcursor-reports/reports/2026-04-30/<id>.json --remote --pipe

# Lister les reports : pas de commande wrangler 4.x pour ça → passer par
# le dashboard https://dash.cloudflare.com → R2 → mathcursor-reports →
# Browse → naviguer dans reports/<date>/. Ou API REST Cloudflare directe.
```

Si la response renvoie `{"ok": false, "error": "backend_misconfigured"}`,
c'est que les bindings ne sont pas appliqués → vérifier dans le
dashboard et redéployer.

## Setup backoffice admin online (`/admin/*`)

Le dashboard admin (`https://mathcursor.pages.dev/admin/`) est protégé par
Basic Auth via `docs/functions/admin/_middleware.js`. Routes :
- `/admin/` — landing
- `/admin/reports.html` — master/detail des rapports « Signaler une erreur »
- `/admin/stats.html` — dashboard téléchargements (Analytics Engine)
- `/admin/api/reports/list|get|screenshot` — APIs proxy R2 (Function-side)
- `/admin/api/stats` — API proxy Analytics Engine SQL

Configuration **une fois** via dashboard CF Pages → projet `mathcursor` →
Settings → Environment variables → **Production** :

| Variable | Type | Valeur |
|---|---|---|
| `ADMIN_USER` | text | username choisi (ex: `admin`) |
| `ADMIN_PASS` | secret | mot de passe (encrypted, ne ressort pas) |
| `CLOUDFLARE_ACCOUNT_ID` | text | ton account ID (déjà dans `~/.mathcursor/cloudflare.env`) |
| `CLOUDFLARE_API_TOKEN_READ` | secret | token CF dédié read-only (cf. ci-dessous) |

**Token CF dédié** (à créer via https://dash.cloudflare.com/profile/api-tokens
→ Create Token → Custom token) :

- Permissions :
  - Account → **Workers R2 Storage** → **Read** (uniquement Read, PAS Edit)
  - Account → **Account Analytics** → **Read** (pour la future migration stats)
- Account Resources : **Include → ton compte** uniquement
- TTL : 1 an raisonnable

Principe de moindre privilège : ce token sera côté Pages côté server, donc
moins exposé que le token Edit utilisé en dev local. Mais quand même, on lui
retire Edit pour qu'un éventuel leak ne permette pas la suppression d'objets.

Une fois set, redéployer (`tools/cloudflare/deploy.sh site`) puis ouvrir :
```
https://mathcursor.pages.dev/admin/
```
Le browser pop la fenêtre login native. Login avec ADMIN_USER / ADMIN_PASS.

## Sécurité

- Ne **jamais** committer le token. `.gitignore` bloque déjà `*.env`.
- Si le token apparaît quelque part par accident (historique chat, logs),
  **revoke-le immédiatement** via https://dash.cloudflare.com/profile/api-tokens.
- Régénérer un nouveau token est rapide — pas de raison de garder un token
  compromis "au cas où".
