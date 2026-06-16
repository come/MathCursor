/**
 * Route POST /api/v1/contact
 *
 * Reçoit un message du formulaire de contact du site (docs/contact.html).
 * Même pattern que /api/v1/report : stockage R2 (REPORTS_BUCKET) sous le
 * préfixe contacts/, rate-limit KV optionnel, PAS d'IP en clair. Aucun email
 * sortant — les messages se consultent dans /admin/contacts.html.
 *
 * Bindings :
 *   - REPORTS_BUCKET (R2)  : stocke 1 JSON par message sous contacts/<date>/
 *   - RATE_LIMIT_KV  (KV)  : optionnel (commenté dans wrangler.toml en MVP)
 *
 * Cf. ADR 2026-06-16-Feat-contact-form-r2.md.
 */

const MAX_BODY_BYTES = 256 * 1024;        // 256 KB : du texte, large
const MAX_MESSAGE_LEN = 10_000;           // 10 KB de message libre
const MAX_FIELD_LEN = 200;                // email / sujet
const MAX_MSGS_PER_IP_PER_HOUR = 5;
const RL_TTL_SECONDS = 7200;              // 2h, cleanup KV auto
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

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
  // Binding R2 indispensable (sans lui on perd les messages). Échec fort.
  if (!env.REPORTS_BUCKET) {
    return jsonResponse({
      ok: false,
      error: 'backend_misconfigured',
      detail: 'REPORTS_BUCKET non bindé côté Cloudflare.',
    }, 503);
  }

  // Rate limit par IP (par tranche horaire). KV optionnel : si absent, on
  // accepte sans rate limit. Pas d'IP stockée dans le message, juste clé KV.
  const ip = request.headers.get('CF-Connecting-IP') || 'unknown';
  const hourBucket = new Date().toISOString().slice(0, 13);  // "2026-06-16T14"
  const rlKey = `rl:contact:${ip}:${hourBucket}`;
  let rlCount = 0;
  let rlActive = !!env.RATE_LIMIT_KV;
  if (rlActive) {
    try {
      rlCount = parseInt((await env.RATE_LIMIT_KV.get(rlKey)) || '0', 10) || 0;
    } catch {
      rlActive = false;  // KV down ponctuellement : on ne bloque pas l'envoi
    }
  }
  if (rlActive && rlCount >= MAX_MSGS_PER_IP_PER_HOUR) {
    return jsonResponse({
      ok: false,
      error: 'rate_limited',
      detail: `Maximum ${MAX_MSGS_PER_IP_PER_HOUR} messages par heure atteint. Réessaie plus tard.`,
    }, 429);
  }

  // Lit le body avec garde de taille.
  let raw;
  try {
    raw = await request.text();
  } catch {
    return jsonResponse({ ok: false, error: 'read_failed' }, 400);
  }
  if (raw.length > MAX_BODY_BYTES) {
    return jsonResponse({ ok: false, error: 'payload_too_large', detail: `Body > ${MAX_BODY_BYTES} bytes` }, 413);
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

  // Honeypot : un bot remplit tous les champs, y compris le champ caché
  // `website`. Si présent et non vide → on répond OK *sans rien stocker*
  // (le bot croit que c'est passé, on ne pollue pas le bucket).
  if (typeof body.website === 'string' && body.website.trim().length > 0) {
    return jsonResponse({ ok: true, id: null });
  }

  // Validation : email valide + message non vide.
  const email = typeof body.email === 'string' ? body.email.trim() : '';
  const message = typeof body.message === 'string' ? body.message.trim() : '';
  const subject = typeof body.subject === 'string' ? body.subject.trim() : '';

  if (!email || email.length > MAX_FIELD_LEN || !EMAIL_RE.test(email)) {
    return jsonResponse({ ok: false, error: 'invalid_email', detail: 'Email manquant ou invalide.' }, 400);
  }
  if (!message) {
    return jsonResponse({ ok: false, error: 'empty_message', detail: 'Message requis.' }, 400);
  }

  // Anti-flood : tronque les champs trop longs plutôt que rejeter.
  const contact = {
    email,
    subject: subject.slice(0, MAX_FIELD_LEN),
    message: message.length > MAX_MESSAGE_LEN
      ? message.slice(0, MAX_MESSAGE_LEN) + '\n\n[--- truncated by server ---]'
      : message,
  };

  // Métadonnées server-side (pas l'IP en clair).
  const id = crypto.randomUUID();
  const date = new Date().toISOString().slice(0, 10);
  contact._server = {
    id,
    received_at: new Date().toISOString(),
    cf_country: request.cf?.country || '??',
    cf_colo: request.cf?.colo || '??',
    cf_user_agent: (request.headers.get('user-agent') || '').slice(0, 200),
  };

  // Stockage du message JSON sous contacts/<date>/<id>.json
  const key = `contacts/${date}/${id}.json`;
  try {
    await env.REPORTS_BUCKET.put(key, JSON.stringify(contact, null, 2), {
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
    'MathCursor /api/v1/contact — POST JSON only. Utilise le formulaire sur /contact.html.',
    { status: 405, headers: { 'Content-Type': 'text/plain', ...CORS_HEADERS } },
  );
}
