/**
 * GET /admin/api/contacts/list
 *
 * Renvoie un JSON [{id, key, title, email, subject, date, size_bytes}, ...]
 * trié par date DESC. Utilisé par /admin/contacts.html pour la vue master.
 *
 * Calque de /admin/api/reports/list.js : LIST du bucket mathcursor-reports via
 * l'API REST R2, mais avec le préfixe `contacts/`. Titre = début du message
 * (fallback sujet). Auth : middleware /admin/_middleware.js (Basic Auth).
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

  const cacheKey = new Request('https://internal-cache/admin/contacts/list', { method: 'GET' });
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

  const items = await Promise.all(jsonObjects.map(async obj => {
    const id = idFromKey(obj.key);
    let title = '(message vide)';
    let email = '';
    let subject = '';
    let receivedAt = obj.last_modified || null;
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

  const response = jsonOk({ count: items.length, items });
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
    const params = new URLSearchParams({ per_page: '1000', prefix: 'contacts/' });
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
