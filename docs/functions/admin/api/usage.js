/**
 * GET /admin/api/usage
 *
 * Agrège le compteur d'usage anonyme (formules converties) stocké en R2 sous
 * usage/<date>/<id>.json par /api/v1/usage. Renvoie le total et la somme par
 * jour sur les 30 derniers jours, pour la section « Formules converties » de
 * /admin/stats.html.
 *
 * Stratégie (même pattern que reports/list.js) :
 *   1. LIST tous les objets sous usage/ (API REST R2, paginé).
 *   2. Pour chaque .json, GET via binding R2 → additionne `count`.
 *   3. Réponse mise en cache 60 sec côté CF edge.
 *
 * Auth : middleware /admin/_middleware.js gère le Basic Auth en amont.
 *
 * Cf. ADR 2026-06-18-Feat-usage-counter-telemetry.md.
 */

const CACHE_SECONDS = 60;
const WINDOW_DAYS = 30;

export async function onRequestGet({ env, request }) {
  if (!env.CLOUDFLARE_API_TOKEN_READ || !env.CLOUDFLARE_ACCOUNT_ID) {
    return jsonError('CLOUDFLARE_API_TOKEN_READ ou CLOUDFLARE_ACCOUNT_ID non configuré côté Pages.', 503);
  }
  if (!env.REPORTS_BUCKET) {
    return jsonError('REPORTS_BUCKET non bindé côté Pages.', 503);
  }

  const cacheKey = new Request('https://internal-cache/admin/usage', { method: 'GET' });
  const cache = caches.default;
  const force = new URL(request.url).searchParams.get('refresh') === '1';
  if (!force) {
    const hit = await cache.match(cacheKey);
    if (hit) return hit;
  }

  let objects;
  try {
    objects = await listAllObjects(env);
  } catch (e) {
    return jsonError(`Listing R2 inaccessible : ${e.message}`, 502);
  }

  const jsonObjects = objects.filter(o => o.key.endsWith('.json'));

  // Somme des `count` par jour. La clé porte déjà la date (usage/<date>/<id>),
  // donc on agrège sans avoir à parser received_at, mais on lit le JSON pour
  // récupérer la valeur du compteur (et ignorer les objets illisibles).
  const byDate = new Map();
  let total = 0;
  let batches = 0;
  await Promise.all(jsonObjects.map(async obj => {
    const dateKey = dateFromKey(obj.key);
    try {
      const r2obj = await env.REPORTS_BUCKET.get(obj.key);
      if (!r2obj) return;
      const data = JSON.parse(await r2obj.text());
      const count = Number(data.count);
      if (!Number.isFinite(count) || count <= 0) return;
      total += count;
      batches += 1;
      if (dateKey) byDate.set(dateKey, (byDate.get(dateKey) || 0) + count);
    } catch { /* objet illisible : ignoré */ }
  }));

  const byDay = fillMissingDays(byDate, WINDOW_DAYS);
  const totalWindow = byDay.reduce((acc, d) => acc + d.count, 0);

  const response = jsonOk({
    generated_at: new Date().toISOString(),
    total,                 // total tous temps confondus (tous les objets R2)
    total_window: totalWindow, // somme sur la fenêtre affichée (30 j)
    batches,
    by_day: byDay,
  });
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
    const params = new URLSearchParams({ per_page: '1000', prefix: 'usage/' });
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

/** usage/2026-06-18/<uuid>.json → "2026-06-18" */
function dateFromKey(key) {
  const m = key.match(/^usage\/(\d{4}-\d{2}-\d{2})\//);
  return m ? m[1] : null;
}

/**
 * Comble les jours manquants des N derniers jours par {day, count:0} pour que
 * la courbe affiche les zéros au lieu d'être lissée. Même esprit que
 * stats.js#fillMissingDays. Tri ASC.
 */
function fillMissingDays(byDate, nDays) {
  const out = [];
  const today = new Date();
  for (let i = nDays - 1; i >= 0; i--) {
    const d = new Date(today);
    d.setUTCDate(d.getUTCDate() - i);
    const key = d.toISOString().slice(0, 10);
    out.push({ day: key + 'T00:00:00Z', count: byDate.get(key) || 0 });
  }
  return out;
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
