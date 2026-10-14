/**
 * GET /admin/api/contacts/list
 *
 * Renvoie un JSON [{id, key, title, email, subject, date, size_bytes}, ...]
 * trié par date DESC. Utilisé par /admin/contacts.html pour la vue master.
 *
 * Calque de /admin/api/reports/list.js : LIST du bucket mathcursor-reports via
 * le BINDING R2 (env.REPORTS_BUCKET.list), préfixe `contacts/`. PAS l'API REST
 * api.cloudflare.com qui est rate-limitée au niveau du compte (incident
 * 2026-08-16, cf. commit cb0326a). Titre = début du message (fallback sujet).
 * Auth : middleware /admin/_middleware.js (Basic Auth).
 */

const TITLE_MAX_LEN = 80;
const CACHE_SECONDS = 300;   // 5 min (cf. incident 429)
// Plafond de lectures d'objets par calcul, pour rester sous la limite de
// sous-requêtes du worker (1000/invocation). Au-delà, on plafonne et on
// signale `truncated` (la liste est alors partielle — dette : agrégation).
const MAX_OBJECT_READS = 800;

export async function onRequestGet({ env, request }) {
  // Depuis l'incident 429 (2026-08-16) on n'utilise PLUS l'API REST
  // api.cloudflare.com (rate-limitée au niveau du compte) : seul le binding R2
  // REPORTS_BUCKET est requis.
  if (!env.REPORTS_BUCKET) {
    return jsonError('REPORTS_BUCKET non bindé côté Pages.', 503);
  }

  const cacheKey = new Request('https://internal-cache/admin/contacts/list', { method: 'GET' });
  const cache = caches.default;
  const force = new URL(request.url).searchParams.get('refresh') === '1';

  // Filet global : toute exception (dont caches.default / cache.match /
  // cache.put) doit devenir un JSON 502 lisible et JAMAIS la page « Bad
  // gateway / Host: Error » opaque de Cloudflare. Cf. incident 2026-08-16.
  try {
    return await handleList({ env, cache, cacheKey, force });
  } catch (e) {
    console.error('[contacts/list] interne:', e && e.stack ? e.stack : String(e));
    return jsonError(`Erreur interne contacts/list : ${e && e.message ? e.message : e}`, 502);
  }
}

async function handleList({ env, cache, cacheKey, force }) {
  // Lecture cache non-fatale.
  if (!force) {
    let hit = null;
    try { hit = await cache.match(cacheKey); } catch { /* cache indispo : on recalcule */ }
    if (hit) return hit;
  }

  // 1) LIST via le BINDING R2 (pas l'API REST rate-limitée). Borne dure.
  let objects;
  let truncated = false;
  try {
    ({ objects, truncated } = await listObjects(env, 'contacts/'));
  } catch (e) {
    console.error('[contacts/list] listing R2 échoué:', e && e.stack ? e.stack : String(e));
    return jsonError(`Listing R2 inaccessible : ${e.message}`, 502);
  }

  const jsonObjects = objects.filter(o => o.key.endsWith('.json'));

  const items = await Promise.all(jsonObjects.map(async obj => {
    const id = idFromKey(obj.key);
    let title = '(message vide)';
    let email = '';
    let subject = '';
    let receivedAt = obj.uploaded ? obj.uploaded.toISOString() : null;
    try {
      const r2obj = await env.REPORTS_BUCKET.get(obj.key);
      if (r2obj) {
        const data = JSON.parse(await r2obj.text());
        email = (data.email || '').trim();
        subject = (data.subject || '').trim();
        const m = (data.message || '').trim();
        const base = subject || m;
        if (base) title = base.length > TITLE_MAX_LEN ? base.slice(0, TITLE_MAX_LEN) + '…' : base;
        const serverTs = (data._server || {}).received_at;
        if (serverTs) receivedAt = serverTs;
      }
    } catch { /* message illisible : titre par défaut */ }

    return {
      id,
      key: obj.key,
      title,
      email,
      subject,
      date: receivedAt,
      size_bytes: obj.size,
    };
  }));

  items.sort((a, b) => (b.date || '').localeCompare(a.date || ''));

  const response = jsonOk({ count: items.length, truncated, items });
  response.headers.set('Cache-Control', `public, max-age=${CACHE_SECONDS}`);
  // Écriture cache non-fatale (best-effort).
  try { await cache.put(cacheKey, response.clone()); } catch { /* cache best-effort */ }
  return response;
}

/**
 * LIST paginé via le binding R2 (env.REPORTS_BUCKET.list), borné à
 * MAX_OBJECT_READS objets. Retourne { objects, truncated }.
 */
async function listObjects(env, prefix) {
  const out = [];
  let truncated = false;
  let cursor;
  do {
    const listing = await env.REPORTS_BUCKET.list({ prefix, cursor, limit: 1000 });
    for (const obj of listing.objects) {
      if (out.length >= MAX_OBJECT_READS) { truncated = true; break; }
      out.push(obj);
    }
    cursor = (!truncated && listing.truncated) ? listing.cursor : undefined;
  } while (cursor);
  if (truncated) {
    console.warn(`[contacts/list] listing plafonné à ${MAX_OBJECT_READS} objets — liste partielle (dette : agrégation).`);
  }
  return { objects: out, truncated };
}

function idFromKey(key) {
  return key.replace(/^.*\//, '').replace(/\.json$/, '');
}

function jsonOk(body) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

function jsonError(msg, status = 500) {
  return new Response(JSON.stringify({ error: msg }), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}
