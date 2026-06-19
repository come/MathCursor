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
export const LATEST_VERSION = "0.11.1";
