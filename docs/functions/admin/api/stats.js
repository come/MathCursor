/**
 * GET /admin/api/stats
 *
 * Proxy vers Analytics Engine SQL — renvoie les 5 datasets utilisés par
 * /admin/stats.html en un seul appel : total / versions / countries /
 * days / referers (30 derniers jours).
 *
 * Cache 60 sec côté CF edge (refresh forcé via ?refresh=1).
 *
 * Auth : middleware /admin/_middleware.js gère le Basic Auth en amont.
 *
 * Schéma des blobs (cf. docs/functions/download/[[filename]].js et
 * tools/cloudflare/README.md §Requêtes stats) :
 *   blob1 = filename résolu, blob2 = ce qu'a tapé le client (latest.exe...),
 *   blob3 = pays CF, blob4 = colo CF, blob5 = user-agent, blob6 = referer,
 *   double1 = taille fichier, index1 = filename.
 */

const CACHE_SECONDS = 60;

export async function onRequestGet({ env, request }) {
  if (!env.CLOUDFLARE_API_TOKEN_READ || !env.CLOUDFLARE_ACCOUNT_ID) {
    return jsonError('CLOUDFLARE_API_TOKEN_READ ou CLOUDFLARE_ACCOUNT_ID non configuré côté Pages.', 503);
  }

  const cacheKey = new Request('https://internal-cache/admin/stats', { method: 'GET' });
  const cache = caches.default;
  const force = new URL(request.url).searchParams.get('refresh') === '1';
  if (!force) {
    const hit = await cache.match(cacheKey);
    if (hit) return hit;
  }

  // Lance les 5 queries en parallèle
  const [total, versions, countries, days, referers] = await Promise.all([
    runSql(env, `SELECT count() AS total FROM mathcursor_downloads
                 WHERE timestamp > NOW() - INTERVAL '30' DAY`),
    runSql(env, `SELECT blob1 AS file, count() AS downloads
                 FROM mathcursor_downloads
                 WHERE timestamp > NOW() - INTERVAL '30' DAY
                 GROUP BY file ORDER BY downloads DESC LIMIT 20`),
    runSql(env, `SELECT blob3 AS country, count() AS downloads
                 FROM mathcursor_downloads
                 WHERE timestamp > NOW() - INTERVAL '30' DAY
                 GROUP BY country ORDER BY downloads DESC LIMIT 30`),
    runSql(env, `SELECT toStartOfInterval(timestamp, INTERVAL '1' DAY) AS day,
                        count() AS downloads
                 FROM mathcursor_downloads
                 WHERE timestamp > NOW() - INTERVAL '30' DAY
                 GROUP BY day ORDER BY day`),
    runSql(env, `SELECT blob6 AS referer, count() AS hits
                 FROM mathcursor_downloads
                 WHERE blob6 != '' AND timestamp > NOW() - INTERVAL '30' DAY
                 GROUP BY referer ORDER BY hits DESC LIMIT 20`),
  ]);

  const response = jsonOk({
    generated_at: new Date().toISOString(),
    total, versions, countries, days, referers,
  });
  response.headers.set('Cache-Control', `public, max-age=${CACHE_SECONDS}`);
  await cache.put(cacheKey, response.clone());
  return response;
}

/**
 * Exécute une requête SQL contre Analytics Engine. Réponse parsée :
 * AE renvoie un format JSON propriétaire `{meta, data, rows}` (data = lignes
 * en tableau d'objets). On retourne directement `data`.
 */
async function runSql(env, sql) {
  const url = `https://api.cloudflare.com/client/v4/accounts/${env.CLOUDFLARE_ACCOUNT_ID}/analytics_engine/sql`;
  const resp = await fetch(url, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${env.CLOUDFLARE_API_TOKEN_READ}`,
      'Content-Type': 'application/sql',
    },
    body: sql,
  });
  if (!resp.ok) {
    const text = await resp.text();
    throw new Error(`AE SQL failed: ${resp.status} ${text.slice(0, 300)}`);
  }
  const data = await resp.json();
  return data.data || [];
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
