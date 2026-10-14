/**
 * GET /admin/api/reports/list
 *
 * Renvoie un JSON [{id, date, title, has_screenshot, size_bytes}, ...]
 * trié par date DESC. Utilisé par /admin/reports.html pour la vue master.
 *
 * Stratégie (même pattern que admin/api/usage.js depuis l'incident 429) :
 *   1. LIST tous les objets sous reports/ via le BINDING R2
 *      (env.REPORTS_BUCKET.list) — PAS l'API REST api.cloudflare.com qui est
 *      rate-limitée au niveau du compte (incident 2026-08-16, cf. commit
 *      cb0326a). Borne dure MAX_OBJECT_READS pour rester sous le plafond de
 *      sous-requêtes du worker.
 *   2. Pour chaque .json, GET via le binding pour extraire le titre =
 *      premières N chars de user_comment (fallback source_text).
 *   3. Détecte la présence du .png jumeau via le set des keys listées.
 *   4. Réponse mise en cache 300 sec côté CF edge pour limiter les ops R2.
 *
 * Auth : middleware /admin/_middleware.js gère le Basic Auth en amont.
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

  const cacheKey = new Request('https://internal-cache/admin/reports/list', { method: 'GET' });
  const cache = caches.default;
  const force = new URL(request.url).searchParams.get('refresh') === '1';

  // Filet global : toute exception (dont caches.default / cache.match /
  // cache.put) doit devenir un JSON 502 lisible et JAMAIS la page « Bad
  // gateway / Host: Error » opaque de Cloudflare. Cf. incident 2026-08-16.
  try {
    return await handleList({ env, cache, cacheKey, force });
  } catch (e) {
    console.error('[reports/list] interne:', e && e.stack ? e.stack : String(e));
    return jsonError(`Erreur interne reports/list : ${e && e.message ? e.message : e}`, 502);
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
    ({ objects, truncated } = await listObjects(env, 'reports/'));
  } catch (e) {
    console.error('[reports/list] listing R2 échoué:', e && e.stack ? e.stack : String(e));
    return jsonError(`Listing R2 inaccessible : ${e.message}`, 502);
  }

  // Set des PNG keys connues pour tester l'existence rapide
  const pngKeys = new Set(objects.filter(o => o.key.endsWith('.png')).map(o => o.key));
  const jsonObjects = objects.filter(o => o.key.endsWith('.json'));

  // 2) Pour chaque JSON, GET via le binding pour extraire le titre
  const items = await Promise.all(jsonObjects.map(async obj => {
    const id = idFromKey(obj.key);
    let title = '(sans commentaire)';
    let receivedAt = obj.uploaded ? obj.uploaded.toISOString() : null;
    try {
      const r2obj = await env.REPORTS_BUCKET.get(obj.key);
      if (r2obj) {
        const data = JSON.parse(await r2obj.text());
        const c = (data.user_comment || '').trim();
        if (c) {
          title = c.length > TITLE_MAX_LEN ? c.slice(0, TITLE_MAX_LEN) + '…' : c;
        } else {
          const s = (data.source_text || '').trim();
          if (s) title = '(source: ' + (s.length > 60 ? s.slice(0, 60) + '…' : s) + ')';
        }
        const serverTs = (data._server || {}).received_at;
        if (serverTs) receivedAt = serverTs;
      }
    } catch { /* report illisible : on garde le titre par défaut */ }

    const pngKey = obj.key.replace(/\.json$/, '.png');
    return {
      id,
      key: obj.key,
      title,
      date: receivedAt,
      has_screenshot: pngKeys.has(pngKey),
      size_bytes: obj.size,
    };
  }));

  // 3) Tri date DESC
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
    console.warn(`[reports/list] listing plafonné à ${MAX_OBJECT_READS} objets — liste partielle (dette : agrégation).`);
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
