/**
 * Routes visiteur du chat : /api/v1/chat/{status,send,contact,poll}.
 *
 * Même origine que le site (le widget est servi par mathcursor.com) : une
 * requête d'une autre origine est refusée. Logique dans ../../../_chat.js.
 * Cf. ADR 2026-11-02-Feat-site-live-chat.
 */
import { HttpError, json, respond, routeKey, storeFor, visitorRoutes } from '../../../_chat.js';

export async function onRequest(context) {
  const { request, env } = context;
  const origin = request.headers.get('Origin');
  if (origin && origin !== new URL(request.url).origin) return json({ error: 'Origine non autorisée' }, 403);
  return respond(async () => {
    const route = visitorRoutes[routeKey(request, '/api/v1/chat')];
    if (!route) throw new HttpError(404, 'Introuvable');
    return route({
      request,
      env,
      store: storeFor(env),
      ip: request.headers.get('CF-Connecting-IP') || 'local',
      country: request.cf?.country || null,
      waitUntil: (p) => context.waitUntil(p),
    });
  });
}
