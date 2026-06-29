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

/**
 * Heuristique « bot » — expression SQL booléenne TRUE pour une ligne de
 * crawler / scanner / outil HTTP. Calée sur l'analyse 2026-06-26 du dataset
 * (cf. ADR 2026-06-26-Feat-admin-stats-bot-filter) :
 *  - UA s'annonçant comme tel (bot/crawl/spider/slurp) ;
 *  - clients HTTP non-navigateur (curl/wget/okhttp/python/go-http/java/headless) ;
 *  - UA vide ;
 *  - le scraper dominant : un seul UA `Linux x86_64 / Chrome 116.0.0.0` figé
 *    (≈25 % du trafic, signature datacenter, pas un lycéen sur Windows).
 *
 * AE SQL n'a NI `match()` NI `positionCaseInsensitive()` — uniquement `lower()`
 * + `LIKE`. Liste volontairement serrée pour ne pas exclure de vrais profils
 * (ex. FB_IAB / mobiles légitimes ne sont PAS filtrés).
 */
const BOT_MATCH = [
  "lower(blob5) LIKE '%bot%'",
  "lower(blob5) LIKE '%crawl%'",
  "lower(blob5) LIKE '%spider%'",
  "lower(blob5) LIKE '%slurp%'",
  "lower(blob5) LIKE '%curl%'",
  "lower(blob5) LIKE '%wget%'",
  "lower(blob5) LIKE '%okhttp%'",
  "lower(blob5) LIKE '%python%'",
  "lower(blob5) LIKE '%go-http%'",
  "lower(blob5) LIKE '%java/%'",
  "lower(blob5) LIKE '%headless%'",
  "blob5 = ''",
  "(blob5 LIKE '%Linux x86_64%' AND blob5 LIKE '%Chrome/116.0.0.0%')",
].join(' OR ');

/**
 * Classe une ligne bot dans un type lisible, pour le détail « par type » du
 * dashboard. AE SQL n'a NI `multiIf` NI `CASE WHEN` → on imbrique des `if()`
 * (seule conditionnelle supportée). L'ordre compte (1ʳᵉ vraie gagne) :
 * UA vide → scraper signé → client HTTP non-navigateur → reste = crawler/bot.
 */
const BOT_CATEGORY =
  "if(blob5 = '', 'UA vide'," +
  " if(blob5 LIKE '%Linux x86_64%' AND blob5 LIKE '%Chrome/116.0.0.0%', 'Scraper (Linux/Chrome 116)'," +
  " if(lower(blob5) LIKE '%curl%' OR lower(blob5) LIKE '%wget%' OR lower(blob5) LIKE '%okhttp%'" +
  " OR lower(blob5) LIKE '%python%' OR lower(blob5) LIKE '%go-http%' OR lower(blob5) LIKE '%java/%'" +
  " OR lower(blob5) LIKE '%headless%', 'Client HTTP / script'," +
  " 'Crawler / bot')))";

export async function onRequestGet({ env, request }) {
  if (!env.CLOUDFLARE_API_TOKEN_READ || !env.CLOUDFLARE_ACCOUNT_ID) {
    return jsonError('CLOUDFLARE_API_TOKEN_READ ou CLOUDFLARE_ACCOUNT_ID non configuré côté Pages.', 503);
  }

  const url = new URL(request.url);
  // Masquage des bots — actif par défaut (la vue utile = le signal humain).
  // `?nobots=0` rétablit le brut.
  const nobots = url.searchParams.get('nobots') !== '0';
  // Fragment injecté dans le WHERE de chaque agrégat. Vide si on montre le brut.
  const bf = nobots ? `\n AND NOT (${BOT_MATCH})` : '';

  // Le cache doit varier selon le mode, sinon le brut servirait le filtré (ou
  // l'inverse) tant que l'entrée 60 s n'a pas expiré.
  const cacheKey = new Request(`https://internal-cache/admin/stats?nobots=${nobots ? 1 : 0}`, { method: 'GET' });
  const cache = caches.default;
  const force = url.searchParams.get('refresh') === '1';
  if (!force) {
    const hit = await cache.match(cacheKey);
    if (hit) return hit;
  }

  // Lance les queries en parallèle. try/catch obligatoire : sans lui, un
  // runSql qui throw (token expiré, AE down) remonte en exception worker
  // → page d'erreur Cloudflare 1101 illisible côté dashboard.
  let total, versions, countries, days, referers, recent, botTypes;
  try {
    [total, versions, countries, days, referers, recent, botTypes] = await Promise.all([
    runSql(env, `SELECT count() AS total FROM mathcursor_downloads
                 WHERE timestamp > NOW() - INTERVAL '30' DAY${bf}`),
    runSql(env, `SELECT blob1 AS file, count() AS downloads
                 FROM mathcursor_downloads
                 WHERE timestamp > NOW() - INTERVAL '30' DAY${bf}
                 GROUP BY file ORDER BY downloads DESC LIMIT 20`),
    runSql(env, `SELECT blob3 AS country, count() AS downloads
                 FROM mathcursor_downloads
                 WHERE timestamp > NOW() - INTERVAL '30' DAY${bf}
                 GROUP BY country ORDER BY downloads DESC LIMIT 30`),
    runSql(env, `SELECT toStartOfInterval(timestamp, INTERVAL '1' DAY) AS day,
                        count() AS downloads
                 FROM mathcursor_downloads
                 WHERE timestamp > NOW() - INTERVAL '30' DAY${bf}
                 GROUP BY day ORDER BY day`),
    runSql(env, `SELECT blob6 AS referer, count() AS hits
                 FROM mathcursor_downloads
                 WHERE blob6 != '' AND timestamp > NOW() - INTERVAL '30' DAY${bf}
                 GROUP BY referer ORDER BY hits DESC LIMIT 20`),
    // Derniers événements bruts (pas d'agrégation) — pour voir qui a DL quoi
    runSql(env, `SELECT timestamp, blob1 AS file, blob3 AS country,
                        blob4 AS colo, blob5 AS user_agent
                 FROM mathcursor_downloads
                 WHERE timestamp > NOW() - INTERVAL '30' DAY${bf}
                 ORDER BY timestamp DESC LIMIT 10`),
    // Bots écartés sur la fenêtre, ventilés par type — toujours calculé, pour
    // afficher « N bots masqués » + le détail quel que soit le mode courant.
    runSql(env, `SELECT ${BOT_CATEGORY} AS type, count() AS hits
                 FROM mathcursor_downloads
                 WHERE timestamp > NOW() - INTERVAL '30' DAY AND (${BOT_MATCH})
                 GROUP BY type ORDER BY hits DESC`),
    ]);
  } catch (e) {
    return jsonError(`Analytics Engine inaccessible : ${e.message}`, 502);
  }

  // Fill : `days` ne contient que les jours avec ≥1 DL. Sans ça, Chart.js
  // lisse entre 2 points distants en sautant les 0 → trompeur. On insère
  // une entrée {day, downloads:0} pour chaque jour manquant des 30
  // derniers, en se calant sur le format `toStartOfInterval` (ISO 8601
  // début de jour UTC).
  const filledDays = fillMissingDays(days, 30);

  const response = jsonOk({
    generated_at: new Date().toISOString(),
    nobots,
    bots_excluded: (botTypes || []).reduce((acc, r) => acc + (Number(r.hits) || 0), 0),
    bot_types: botTypes || [],
    total, versions, countries, days: filledDays, referers, recent,
  });
  response.headers.set('Cache-Control', `public, max-age=${CACHE_SECONDS}`);
  await cache.put(cacheKey, response.clone());
  return response;
}

/**
 * Comble les jours manquants des N derniers jours par {day, downloads:0}
 * pour que la courbe affiche les zéros au lieu d'être lissée entre les
 * jours non-zéro. Tri ASC final (= comme la query d'origine).
 *
 * AE retourne `day` au format ISO `2026-05-01T00:00:00Z` (start of day UTC
 * via toStartOfInterval). On compare par préfixe `YYYY-MM-DD`.
 */
function fillMissingDays(rows, nDays) {
  const byDate = new Map();
  for (const r of (rows || [])) {
    const key = (r.day || '').slice(0, 10);
    if (key) byDate.set(key, Number(r.downloads) || 0);
  }
  const out = [];
  const today = new Date();
  // On part de today - (nDays - 1) jours pour inclure exactement nDays
  for (let i = nDays - 1; i >= 0; i--) {
    const d = new Date(today);
    d.setUTCDate(d.getUTCDate() - i);
    const key = d.toISOString().slice(0, 10);
    out.push({
      day: key + 'T00:00:00Z',
      downloads: byDate.get(key) || 0,
    });
  }
  return out;
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
