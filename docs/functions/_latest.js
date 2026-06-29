/**
 * Source unique de la dernière version stable publiée.
 *
 * Importée par :
 *   - download/[[filename]].js  (résolution de l'alias latest.exe)
 *   - api/v1/version.js         (endpoint consommé par l'add-in pour la pastille MAJ)
 *
 * Préfixe `_` → fichier NON routé par Cloudflare Pages (juste un module partagé).
 * Bumpé par /deploy-prod à chaque release (cf. ADR 2026-06-18-Feat-ribbon-update-badge).
 */
export const LATEST_VERSION = "0.11.2";

/**
 * Alphas multi-éditeur publiés sur R2 (bucket `mathcursor-releases`), servis par
 * download/[[filename]].js. Indépendants de LATEST_VERSION (add-in Word seul).
 *
 * VSIX VS Code : UNE extension, N VSIX *platform-specific* (cf. ADR 2026-06-25-Feat-
 * vscode-marketplace-publishing-model). On expose une MAP cible → fichier versionné,
 * servie via les alias `latest-<target>.vsix` (+ `latest.vsix` = win32-x64, rétro-compat).
 * Pour publier une nouvelle version : bump les noms ici (cf. ADR 2026-06-29-Feat-vscode-
 * vsix-multiplatform-site-distribution).
 */
export const LATEST_VSCODE_VSIX = {
  "win32-x64": "mathcursor-win32-x64-0.1.0.vsix",
  "linux-x64": "mathcursor-linux-x64-0.1.0.vsix",
  "darwin-arm64": "mathcursor-darwin-arm64-0.1.0.vsix",
};
/** Cible servie par l'alias générique `latest.vsix` (rétro-compat liens existants). */
export const DEFAULT_VSIX_TARGET = "win32-x64";

/**
 * Extension LibreOffice (.oxt). NOM DE FICHIER STABLE, NON versionné : les URI de
 * script bundlées dans l'oxt (Addons.xcu, jobs.py) sont figées sur « MathCursor.oxt »,
 * et LibreOffice clé son package par le nom de fichier installé — un nom versionné
 * (ex. MathCursor-0.1.0.oxt) casse le lookup (KeyError pythonscript).
 * La version vit dans description.xml, jamais dans le nom de fichier.
 * Cf. ADR 2026-06-29-Fix-oxt-stable-filename-distribution.
 */
export const LATEST_OXT = "MathCursor.oxt";
