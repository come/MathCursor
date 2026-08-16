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

const CACHE_SECONDS = 300;   // 5 min : réduit la fréquence de recalcul (cf. incident 429)
const WINDOW_DAYS = 30;
// Plafond de lectures d'objets par calcul, pour rester sous la limite de
// sous-requêtes du worker (1000/invocation). Au-delà, on plafonne et on
// signale `truncated` (le total est alors sous-évalué — le vrai fix = agrégation,
// cf. dette notée pour la migration Analytics Engine).
const MAX_OBJECT_READS = 800;

export async function onRequestGet({ env, request }) {
  // Depuis l'incident 429 (2026-08-16) on n'utilise PLUS l'API REST
  // api.cloudflare.com (rate-limitée au niveau du compte) : seul le binding R2
  // REPORTS_BUCKET est requis.
  if (!env.REPORTS_BUCKET) {
    return jsonError('REPORTS_BUCKET non bindé côté Pages.', 503);
  }

  const cacheKey = new Request('https://internal-cache/admin/usage', { method: 'GET' });
  const cache = caches.default;
  const force = new URL(request.url).searchParams.get('refresh') === '1';

  // Filet global : toute exception (dont `caches.default` / cache.match /
  // cache.put, hors du try du listing) doit devenir un JSON 502 lisible et
  // JAMAIS la page « Bad gateway / Host: Error » opaque de Cloudflare.
  // Cf. incident 2026-08-16. Voir jumeau dans admin/api/stats.js.
  try {
    return await handleUsage({ env, cache, cacheKey, force });
  } catch (e) {
    console.error('[usage] interne:', e && e.stack ? e.stack : String(e));
    return jsonError(`Erreur interne usage : ${e && e.message ? e.message : e}`, 502);
  }
}

async function handleUsage({ env, cache, cacheKey, force }) {
  // Lecture cache non-fatale.
  if (!force) {
    let hit = null;
    try { hit = await cache.match(cacheKey); } catch { /* cache indispo : on recalcule */ }
    if (hit) return hit;
  }

  // Listing via le BINDING R2 (env.REPORTS_BUCKET.list), PAS l'API REST
  // api.cloudflare.com : le binding n'est pas soumis à la rate-limit compte qui
  // a causé l'incident 429 du 2026-08-16. On ne parcourt que les préfixes des
  // 30 derniers jours (usage/<date>/) — la fenêtre affichée — et on borne le
  // nombre de lectures d'objets à MAX_OBJECT_READS pour rester sous le plafond
  // de sous-requêtes du worker.
  const windowDates = lastNDates(WINDOW_DAYS);   // ["YYYY-MM-DD", ...] ASC
  const byDate = new Map();
  let batches = 0;
  let truncated = false;

  try {
    // 1) Collecte des clés de la fenêtre (borne dure MAX_OBJECT_READS).
    const keysByDate = [];
    for (const date of windowDates) {
      let cursor;
      do {
        const listing = await env.REPORTS_BUCKET.list({ prefix: `usage/${date}/`, cursor, limit: 1000 });
        for (const obj of listing.objects) {
          if (!obj.key.endsWith('.json')) continue;
          if (keysByDate.length >= MAX_OBJECT_READS) { truncated = true; break; }
          keysByDate.push({ key: obj.key, date });
        }
        cursor = (!truncated && listing.truncated) ? listing.cursor : undefined;
      } while (cursor);
      if (truncated) break;
    }

    // 2) Lecture concurrente des `count` (objets illisibles ignorés).
    await Promise.all(keysByDate.map(async ({ key, date }) => {
      try {
        const r2obj = await env.REPORTS_BUCKET.get(key);
        if (!r2obj) return;
        const data = JSON.parse(await r2obj.text());
        const count = Number(data.count);
        if (!Number.isFinite(count) || count <= 0) return;
        batches += 1;
        byDate.set(date, (byDate.get(date) || 0) + count);
      } catch { /* objet illisible : ignoré */ }
    }));
  } catch (e) {
    console.error('[usage] agrégation R2 échouée:', e && e.stack ? e.stack : String(e));
    return jsonError(`Agrégation R2 échouée : ${e.message}`, 502);
  }
  if (truncated) {
    console.warn(`[usage] lecture plafonnée à ${MAX_OBJECT_READS} objets sur la fenêtre — total sous-évalué (dette : agrégation).`);
  }

  const byDay = fillMissingDays(byDate, WINDOW_DAYS);
  const totalWindow = byDay.reduce((acc, d) => acc + d.count, 0);

  const response = jsonOk({
    generated_at: new Date().toISOString(),
    total_window: totalWindow, // somme sur la fenêtre affichée (30 j)
    batches,
    truncated,                 // true si plafonné (total sous-évalué)
    by_day: byDay,
  });
  response.headers.set('Cache-Control', `public, max-age=${CACHE_SECONDS}`);
  // Écriture cache non-fatale (best-effort).
  try { await cache.put(cacheKey, response.clone()); } catch { /* cache best-effort */ }
  return response;
}

/** Les N derniers jours au format "YYYY-MM-DD" (UTC), tri ASC. Cadré sur la
 * même logique que fillMissingDays pour que préfixes listés et jours affichés
 * coïncident. */
function lastNDates(nDays) {
  const out = [];
  const today = new Date();
  for (let i = nDays - 1; i >= 0; i--) {
    const d = new Date(today);
    d.setUTCDate(d.getUTCDate() - i);
    out.push(d.toISOString().slice(0, 10));
  }
  return out;
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
