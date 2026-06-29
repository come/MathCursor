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
 * download/[[filename]].js via les alias `latest.vsix` / `latest.oxt`.
 * Indépendants de LATEST_VERSION (qui ne concerne que l'add-in Word).
 */
export const LATEST_VSCODE_VSIX = "mathcursor-win32-x64-0.1.0.vsix";
export const LATEST_OXT = "MathCursor-0.1.0.oxt";
