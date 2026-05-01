/**
 * GET /admin/api/reports/list
 *
 * Renvoie un JSON [{id, date, title, has_screenshot, size_bytes}, ...]
 * trié par date DESC. Utilisé par /admin/reports.html pour la vue master.
 *
 * Stratégie :
 *   1. LIST tous les objets sous reports/ (1 op R2 Class A, paginé).
 *   2. Pour chaque .json, fait un GET (Class B) pour extraire le titre =
 *      premières N chars de user_comment (fallback source_text).
 *   3. Détecte la présence du .png jumeau via le set des keys listées.
 *   4. Réponse mise en cache 60 sec côté CF edge pour limiter les ops R2
 *      en cas de refresh fréquent du dashboard.
 *
 * Auth : middleware /admin/_middleware.js gère le Basic Auth en amont.
 */

const TITLE_MAX_LEN = 80;
const CACHE_SECONDS = 60;

export async function onRequestGet({ env, request }) {
  if (!env.CLOUDFLARE_API_TOKEN_READ || !env.CLOUDFLARE_ACCOUNT_ID) {
    return jsonError('CLOUDFLARE_API_TOKEN_READ ou CLOUDFLARE_ACCOUNT_ID non configuré côté Pages.', 503);
  }
  if (!env.REPORTS_BUCKET) {
    return jsonError('REPORTS_BUCKET non bindé côté Pages.', 503);
  }

  // Cache HTTP au niveau edge CF
  const cacheKey = new Request('https://internal-cache/admin/reports/list', { method: 'GET' });
  const cache = caches.default;
  const force = new URL(request.url).searchParams.get('refresh') === '1';
  if (!force) {
    const hit = await cache.match(cacheKey);
    if (hit) return hit;
  }

  // 1) LIST via API REST (R2 binding ne supporte pas list avec metadata size complète)
  const objects = await listAllObjects(env);

  // Set des PNG keys connues pour tester l'existence rapide
  const pngKeys = new Set(objects.filter(o => o.key.endsWith('.png')).map(o => o.key));
  const jsonObjects = objects.filter(o => o.key.endsWith('.json'));

  // 2) Pour chaque JSON, GET via R2 binding pour extraire le titre
  const items = await Promise.all(jsonObjects.map(async obj => {
    const id = idFromKey(obj.key);
    let title = '(sans commentaire)';
    let receivedAt = obj.last_modified || null;
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

  const response = jsonOk({ count: items.length, items });
  // Cache 60s — refresh forcé via ?refresh=1
  response.headers.set('Cache-Control', `public, max-age=${CACHE_SECONDS}`);
  await cache.put(cacheKey, response.clone());
  return response;
}

async function listAllObjects(env) {
  const accountId = env.CLOUDFLARE_ACCOUNT_ID;
  const token = env.CLOUDFLARE_API_TOKEN_READ;
  const url = `https://api.cloudflare.com/client/v4/accounts/${accountId}/r2/buckets/mathcursor-reports/objects`;
  const out = [];
  let cursor = '';
  while (true) {
    const params = new URLSearchParams({ per_page: '1000', prefix: 'reports/' });
    if (cursor) params.set('cursor', cursor);
    const resp = await fetch(`${url}?${params.toString()}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!resp.ok) {
      throw new Error(`R2 list API failed: ${resp.status} ${await resp.text()}`);
    }
    const data = await resp.json();
    if (!data.success) throw new Error(`R2 list API not success: ${JSON.stringify(data.errors)}`);
    for (const o of (data.result || [])) out.push(o);
    cursor = ((data.result_info || {}).cursor) || '';
    if (!cursor) break;
  }
  return out;
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
