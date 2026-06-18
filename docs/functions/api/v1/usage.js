/**
 * Route POST /api/v1/usage
 *
 * Reçoit le compteur d'usage anonyme de l'add-in : le nombre de formules
 * converties accumulé localement depuis le dernier envoi. Aucun identifiant,
 * aucun contenu — juste un entier et la version de l'add-in.
 *
 * Même pattern que /api/v1/contact : stockage R2 (REPORTS_BUCKET) sous le
 * préfixe usage/, rate-limit KV optionnel, PAS d'IP en clair.
 *
 * Bindings :
 *   - REPORTS_BUCKET (R2)  : stocke 1 JSON par batch sous usage/<date>/
 *   - RATE_LIMIT_KV  (KV)  : optionnel (commenté dans wrangler.toml en MVP)
 *
 * Cf. ADR 2026-06-18-Feat-usage-counter-telemetry.md.
 */

const MAX_BODY_BYTES = 4 * 1024;          // 4 KB : un entier + une version, large
const MAX_COUNT = 100_000;                // garde-fou contre un payload absurde
const MAX_VERSION_LEN = 40;
const MAX_BATCHES_PER_IP_PER_HOUR = 60;   // un add-in flushe à chaque perte de focus
const RL_TTL_SECONDS = 7200;              // 2h, cleanup KV auto

const CORS_HEADERS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type',
  'Access-Control-Max-Age': '86400',
};

/** Réponse JSON helper avec CORS. */
function jsonResponse(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...CORS_HEADERS },
  });
}

/** Préflight CORS. */
export async function onRequestOptions() {
  return new Response(null, { status: 204, headers: CORS_HEADERS });
}

export async function onRequestPost({ request, env }) {
  // Binding R2 indispensable (sans lui on perd le compteur). Échec fort.
  if (!env.REPORTS_BUCKET) {
    return jsonResponse({
      ok: false,
      error: 'backend_misconfigured',
      detail: 'REPORTS_BUCKET non bindé côté Cloudflare.',
    }, 503);
  }

  // Rate limit par IP (par tranche horaire). KV optionnel : si absent, on
  // accepte sans rate limit. Pas d'IP stockée dans le batch, juste la clé KV.
  const ip = request.headers.get('CF-Connecting-IP') || 'unknown';
  const hourBucket = new Date().toISOString().slice(0, 13);  // "2026-06-18T14"
  const rlKey = `rl:usage:${ip}:${hourBucket}`;
  let rlCount = 0;
  let rlActive = !!env.RATE_LIMIT_KV;
  if (rlActive) {
    try {
      rlCount = parseInt((await env.RATE_LIMIT_KV.get(rlKey)) || '0', 10) || 0;
    } catch {
      rlActive = false;  // KV down ponctuellement : on ne bloque pas l'envoi
    }
  }
  if (rlActive && rlCount >= MAX_BATCHES_PER_IP_PER_HOUR) {
    return jsonResponse({ ok: false, error: 'rate_limited' }, 429);
  }

  // Lit le body avec garde de taille.
  let raw;
  try {
    raw = await request.text();
  } catch {
    return jsonResponse({ ok: false, error: 'read_failed' }, 400);
  }
  if (raw.length > MAX_BODY_BYTES) {
    return jsonResponse({ ok: false, error: 'payload_too_large' }, 413);
  }

  // Parse JSON
  let body;
  try {
    body = JSON.parse(raw);
  } catch {
    return jsonResponse({ ok: false, error: 'invalid_json' }, 400);
  }
  if (!body || typeof body !== 'object') {
    return jsonResponse({ ok: false, error: 'invalid_payload' }, 400);
  }

  // Validation : count = entier strictement positif et borné.
  const count = Number(body.count);
  if (!Number.isInteger(count) || count <= 0 || count > MAX_COUNT) {
    return jsonResponse({ ok: false, error: 'invalid_count' }, 400);
  }
  const version = (typeof body.version === 'string' ? body.version : '')
    .slice(0, MAX_VERSION_LEN);

  // Batch anonyme : aucun identifiant, aucun contenu.
  const id = crypto.randomUUID();
  const date = new Date().toISOString().slice(0, 10);
  const batch = {
    count,
    version,
    _server: {
      id,
      received_at: new Date().toISOString(),
      cf_country: request.cf?.country || '??',
      cf_colo: request.cf?.colo || '??',
    },
  };

  // Stockage sous usage/<date>/<id>.json
  const key = `usage/${date}/${id}.json`;
  try {
    await env.REPORTS_BUCKET.put(key, JSON.stringify(batch, null, 2), {
      httpMetadata: { contentType: 'application/json' },
    });
  } catch (err) {
    return jsonResponse({ ok: false, error: 'storage_failed', detail: String(err).slice(0, 500) }, 500);
  }

  // Increment rate limit counter (après stockage réussi). Skip si KV absent.
  if (rlActive) {
    try {
      await env.RATE_LIMIT_KV.put(rlKey, String(rlCount + 1), { expirationTtl: RL_TTL_SECONDS });
    } catch { /* best effort */ }
  }

  return jsonResponse({ ok: true, id });
}

/** GET = aide minimale en cas d'accès humain par erreur. */
export async function onRequestGet() {
  return new Response(
    'MathCursor /api/v1/usage — POST JSON only ({ "count": <int>, "version": "x.y.z" }).',
    { status: 405, headers: { 'Content-Type': 'text/plain', ...CORS_HEADERS } },
  );
}
