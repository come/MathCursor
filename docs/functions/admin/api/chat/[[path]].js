/**
 * Routes admin du chat : /admin/api/chat/{sync,reply,archive,away}.
 *
 * Protégées par le Basic Auth de functions/admin/_middleware.js (comme tout
 * /admin/*). Logique dans ../../../_chat.js.
 * Cf. ADR 2026-11-02-Feat-site-live-chat.
 */
import { adminRoutes, HttpError, json, respond, routeKey, storeFor } from '../../../_chat.js';

export async function onRequest(context) {
  const { request, env } = context;
  const origin = request.headers.get('Origin');
  if (origin && origin !== new URL(request.url).origin) return json({ error: 'Origine non autorisée' }, 403);
  return respond(async () => {
    const route = adminRoutes[routeKey(request, '/admin/api/chat')];
    if (!route) throw new HttpError(404, 'Introuvable');
    return route({ request, env, store: storeFor(env) });
  });
}
