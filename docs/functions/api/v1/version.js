/**
 * Route GET /api/v1/version
 *
 * Renvoie la dernière version stable publiée : { "latest": "x.y.z" }.
 * Consommé par l'add-in (UpdateChecker) pour afficher l'indicateur « MAJ dispo »
 * sur l'onglet ruban. Aucune donnée reçue, aucun log analytics (≠ /download,
 * qu'on ne veut pas polluer). Cf. ADR 2026-06-18-Feat-ribbon-update-badge.
 */

import { LATEST_VERSION } from "../../_latest.js";

const CORS_HEADERS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type",
  "Access-Control-Max-Age": "86400",
};

export async function onRequestOptions() {
  return new Response(null, { status: 204, headers: CORS_HEADERS });
}

export async function onRequestGet() {
  return new Response(JSON.stringify({ latest: LATEST_VERSION }), {
    status: 200,
    headers: {
      "Content-Type": "application/json",
      // Cache court côté edge : la version change rarement, mais on ne veut pas
      // qu'un add-in voie une vieille valeur trop longtemps après une release.
      "Cache-Control": "public, max-age=300",
      ...CORS_HEADERS,
    },
  });
}
