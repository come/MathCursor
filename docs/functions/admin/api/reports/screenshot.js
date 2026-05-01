/**
 * GET /admin/api/reports/screenshot?id=<UUID>
 *
 * Stream le PNG associé au rapport. Sert d'image src directement dans la
 * page admin (auth conservée par le browser via le challenge Basic Auth
 * déjà passé).
 *
 * 404 si le rapport n'a pas de screenshot joint.
 */

export async function onRequestGet({ request, env }) {
  if (!env.REPORTS_BUCKET) {
    return new Response('REPORTS_BUCKET non bindé.', { status: 503 });
  }
  const id = new URL(request.url).searchParams.get('id');
  if (!id || !/^[a-f0-9-]{8,40}$/i.test(id)) {
    return new Response('id manquant ou invalide.', { status: 400 });
  }

  const key = await findPngKey(env, id);
  if (!key) return new Response('No screenshot.', { status: 404 });

  const obj = await env.REPORTS_BUCKET.get(key);
  if (!obj) return new Response('No screenshot (race).', { status: 404 });

  return new Response(obj.body, {
    status: 200,
    headers: {
      'Content-Type': 'image/png',
      'Cache-Control': 'public, max-age=300',
    },
  });
}

async function findPngKey(env, id) {
  let cursor = undefined;
  const target = `${id}.png`;
  while (true) {
    const res = await env.REPORTS_BUCKET.list({ prefix: 'reports/', limit: 1000, cursor });
    for (const o of (res.objects || [])) {
      if (o.key.endsWith(target)) return o.key;
    }
    if (!res.truncated) return null;
    cursor = res.cursor;
  }
}
