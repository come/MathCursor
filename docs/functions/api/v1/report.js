/**
 * Route POST /api/v1/report
 *
 * Reçoit un rapport "signaler une erreur" depuis l'add-in MathCursor (cf.
 * brief 2026-04-30-feedback-form-with-cloudflare-backend.md).
 *
 * Bindings attendus (à configurer côté Cloudflare Pages settings) :
 *   - REPORTS_BUCKET (R2)  : stocke 1 JSON par rapport + 1 PNG si screenshot
 *   - RATE_LIMIT_KV  (KV)  : compteur reports/IP/heure
 *
 * Si un binding manque, l'endpoint répond 503 avec un message clair :
 *   on préfère échouer fort plutôt que silencieusement perdre des reports.
 *
 * Versionné dès le MVP (`/api/v1/report`) pour pouvoir évoluer le contrat
 * sans casser les vieux clients add-in.
 */

const MAX_REPORTS_PER_IP_PER_HOUR = 5;
const MAX_BODY_BYTES = 5 * 1024 * 1024;       // 5 MB (incl. screenshot b64)
const MAX_USER_COMMENT_LEN = 10_000;          // 10 KB de texte libre suffit
const RL_TTL_SECONDS = 7200;                  // 2h, KV cleanup auto

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

/** Préflight CORS — utile si on appelle depuis la web demo plus tard. */
export async function onRequestOptions() {
  return new Response(null, { status: 204, headers: CORS_HEADERS });
}

export async function onRequestPost({ request, env }) {
  // Binding R2 indispensable (sans lui on perd les rapports). Échec fort.
  if (!env.REPORTS_BUCKET) {
    return jsonResponse({
      ok: false,
      error: 'backend_misconfigured',
      detail: 'REPORTS_BUCKET non bindé côté Cloudflare.',
    }, 503);
  }

  // Rate limit par IP (par tranche horaire). KV optionnel : si absent, on
  // accepte sans rate limit (MVP, à activer quand le token CF aura le
  // scope Workers KV Storage:Edit). Pas d'IP stockée dans le report,
  // juste utilisée comme clé KV éphémère.
  const ip = request.headers.get('CF-Connecting-IP') || 'unknown';
  const hourBucket = new Date().toISOString().slice(0, 13); // "2026-04-30T14"
  const rlKey = `rl:${ip}:${hourBucket}`;
  let rlCount = 0;
  let rlActive = !!env.RATE_LIMIT_KV;
  if (rlActive) {
    try {
      rlCount = parseInt((await env.RATE_LIMIT_KV.get(rlKey)) || '0', 10) || 0;
    } catch {
      // KV down ponctuellement : on ne bloque pas l'envoi
      rlActive = false;
    }
  }
  if (rlActive && rlCount >= MAX_REPORTS_PER_IP_PER_HOUR) {
    return jsonResponse({
      ok: false,
      error: 'rate_limited',
      detail: `Maximum ${MAX_REPORTS_PER_IP_PER_HOUR} rapports par heure atteint. Réessaye plus tard.`,
    }, 429);
  }

  // Lit le body avec garde de taille (Cloudflare clamp à ~100MB côté CF, mais
  // on serre nous-mêmes pour éviter de payer R2 pour des tentatives énormes).
  let body;
  try {
    body = await request.text();
  } catch {
    return jsonResponse({ ok: false, error: 'read_failed' }, 400);
  }
  if (body.length > MAX_BODY_BYTES) {
    return jsonResponse({
      ok: false,
      error: 'payload_too_large',
      detail: `Body > ${MAX_BODY_BYTES} bytes`,
    }, 413);
  }

  // Parse JSON
  let report;
  try {
    report = JSON.parse(body);
  } catch {
    return jsonResponse({ ok: false, error: 'invalid_json' }, 400);
  }
  if (!report || typeof report !== 'object') {
    return jsonResponse({ ok: false, error: 'invalid_payload' }, 400);
  }

  // Validation minimale : on veut au moins un commentaire OU un source_text.
  // Permet de filtrer les pings vides / scans automatisés.
  const hasComment =
    typeof report.user_comment === 'string' &&
    report.user_comment.trim().length > 0;
  const hasSource =
    typeof report.source_text === 'string' &&
    report.source_text.trim().length > 0;
  if (!hasComment && !hasSource) {
    return jsonResponse({
      ok: false,
      error: 'empty_report',
      detail: 'user_comment ou source_text requis (au moins un).',
    }, 400);
  }

  // Anti-flood basique sur le commentaire libre
  if (hasComment && report.user_comment.length > MAX_USER_COMMENT_LEN) {
    report.user_comment = report.user_comment.slice(0, MAX_USER_COMMENT_LEN)
      + '\n\n[--- truncated by server ---]';
  }

  // Métadonnées server-side pour debug / dédup futur. Pas l'IP en clair.
  const id = crypto.randomUUID();
  const date = new Date().toISOString().slice(0, 10);
  const now = new Date().toISOString();
  report._server = {
    id,
    received_at: now,
    cf_country: request.cf?.country || '??',
    cf_colo: request.cf?.colo || '??',
    cf_user_agent: (request.headers.get('user-agent') || '').slice(0, 200),
  };

  // Si un screenshot est joint en base64 : extrait en fichier R2 séparé pour
  // éviter de bourrer le JSON et accélérer la consultation côté admin.
  if (report.screenshot_b64) {
    try {
      const png = base64ToBytes(report.screenshot_b64);
      const pngKey = `reports/${date}/${id}.png`;
      await env.REPORTS_BUCKET.put(pngKey, png, {
        httpMetadata: { contentType: 'image/png' },
      });
      delete report.screenshot_b64;
      report.screenshot_url = pngKey;
    } catch (err) {
      // Si le b64 est corrompu, on ignore le screenshot mais on garde le report
      delete report.screenshot_b64;
      report._screenshot_error = String(err).slice(0, 500);
    }
  }

  // Idem si un log_tail très volumineux est joint (rare mais possible)
  if (typeof report.log_tail === 'string' && report.log_tail.length > 128 * 1024) {
    report.log_tail = report.log_tail.slice(-128 * 1024); // garde la queue
    report._log_truncated = true;
  }

  // Stockage du report JSON
  const jsonKey = `reports/${date}/${id}.json`;
  try {
    await env.REPORTS_BUCKET.put(jsonKey, JSON.stringify(report, null, 2), {
      httpMetadata: { contentType: 'application/json' },
    });
  } catch (err) {
    return jsonResponse({
      ok: false,
      error: 'storage_failed',
      detail: String(err).slice(0, 500),
    }, 500);
  }

  // Increment rate limit counter (après stockage réussi). Skip si KV absent.
  if (rlActive) {
    try {
      await env.RATE_LIMIT_KV.put(rlKey, String(rlCount + 1), {
        expirationTtl: RL_TTL_SECONDS,
      });
    } catch { /* best effort */ }
  }

  return jsonResponse({ ok: true, id, screenshot_url: report.screenshot_url || null });
}

/** Base64 → Uint8Array (pas d'`atob` direct sur des binaires utf8-style). */
function base64ToBytes(b64) {
  // Strip data URL prefix éventuel (ex: "data:image/png;base64,...")
  const cleaned = b64.includes(',') ? b64.split(',', 2)[1] : b64;
  const bin = atob(cleaned);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

/** GET = aide minimale en cas d'accès humain par erreur. */
export async function onRequestGet() {
  return new Response(
    'MathCursor /api/v1/report — POST JSON only. See docs.',
    { status: 405, headers: { 'Content-Type': 'text/plain', ...CORS_HEADERS } },
  );
}
