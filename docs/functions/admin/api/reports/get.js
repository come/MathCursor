/**
 * GET /admin/api/reports/get?id=<UUID>
 *
 * Renvoie le JSON brut du rapport. Lecture directe via le binding R2
 * (REPORTS_BUCKET), pas besoin du token API REST.
 *
 * Auth : middleware /admin/_middleware.js gère le Basic Auth.
 */

export async function onRequestGet({ request, env }) {
  if (!env.REPORTS_BUCKET) {
    return new Response(JSON.stringify({ error: 'REPORTS_BUCKET non bindé.' }), {
      status: 503, headers: { 'Content-Type': 'application/json' },
    });
  }
  const id = new URL(request.url).searchParams.get('id');
  if (!id || !/^[a-f0-9-]{8,40}$/i.test(id)) {
    return new Response(JSON.stringify({ error: 'id manquant ou invalide.' }), {
      status: 400, headers: { 'Content-Type': 'application/json' },
    });
  }

  // Recherche la key par scan du préfixe reports/. Plus rapide qu'un LIST
  // complet : on scanne par date (les UUIDs sont dans reports/YYYY-MM-DD/).
  // Pour MVP on scanne reports/ entièrement, c'est cheap (~quelques KB).
  const key = await findKey(env, id);
  if (!key) {
    return new Response(JSON.stringify({ error: 'Report introuvable.' }), {
      status: 404, headers: { 'Content-Type': 'application/json' },
    });
  }

  const obj = await env.REPORTS_BUCKET.get(key);
  if (!obj) {
    return new Response(JSON.stringify({ error: 'Report introuvable (race).' }), {
      status: 404, headers: { 'Content-Type': 'application/json' },
    });
  }
  // On enrichit avec _key pour debug côté UI
  let body;
  try {
    const data = JSON.parse(await obj.text());
    data._key = key;
    body = JSON.stringify(data);
  } catch {
    body = await obj.text();
  }
  return new Response(body, {
    status: 200,
    headers: {
      'Content-Type': 'application/json',
      'Cache-Control': 'public, max-age=300',
    },
  });
}

async function findKey(env, id) {
  // Scan via R2 binding list (stream the keys, stop at first match)
  let cursor = undefined;
  const target = `${id}.json`;
  while (true) {
    const res = await env.REPORTS_BUCKET.list({ prefix: 'reports/', limit: 1000, cursor });
    for (const o of (res.objects || [])) {
      if (o.key.endsWith(target)) return o.key;
    }
    if (!res.truncated) return null;
    cursor = res.cursor;
  }
}
