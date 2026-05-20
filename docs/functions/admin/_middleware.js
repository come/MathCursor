/**
 * Middleware Basic Auth pour TOUTES les routes /admin/* (HTML + API).
 *
 * Configuration côté Cloudflare Pages → Settings → Environment variables :
 *   - ADMIN_USER : username (ex: "admin")
 *   - ADMIN_PASS : password (mot de passe choisi)
 *
 * Si l'une des 2 vars manque côté Pages, le middleware refuse TOUT
 * (échec fort). On préfère un 503 explicite plutôt qu'un dashboard
 * accessible sans auth par accident de configuration.
 *
 * Cf. ADR 2026-05-01-Feat-admin-backoffice-cloudflare.md.
 */

const REALM = 'MathCursor Admin';

export async function onRequest(context) {
  const { request, env, next } = context;

  // Sécurité : si les vars d'auth ne sont pas set, on bloque tout.
  if (!env.ADMIN_USER || !env.ADMIN_PASS) {
    return new Response(
      'Admin auth not configured. Set ADMIN_USER and ADMIN_PASS in Pages env vars.',
      { status: 503, headers: { 'Content-Type': 'text/plain' } },
    );
  }

  const authHeader = request.headers.get('Authorization') || '';
  if (!authHeader.startsWith('Basic ')) {
    return unauthorized();
  }

  // Décodage du `Basic <base64(user:pass)>`. atob est dispo dans Workers.
  let decoded;
  try {
    decoded = atob(authHeader.slice(6).trim());
  } catch {
    return unauthorized();
  }
  const sepIndex = decoded.indexOf(':');
  if (sepIndex < 0) return unauthorized();
  const user = decoded.slice(0, sepIndex);
  const pass = decoded.slice(sepIndex + 1);

  // Comparaison constant-time pour limiter les attaques par timing.
  if (!safeEquals(user, env.ADMIN_USER) || !safeEquals(pass, env.ADMIN_PASS)) {
    return unauthorized();
  }

  return next();
}

function unauthorized() {
  return new Response('Unauthorized', {
    status: 401,
    headers: {
      'WWW-Authenticate': `Basic realm="${REALM}", charset="UTF-8"`,
      'Content-Type': 'text/plain',
    },
  });
}

/**
 * Comparaison string en temps constant. JS n'a pas crypto.timingSafeEqual
 * en Workers, on implémente nous-mêmes : XOR cumulatif sur tous les bytes.
 */
function safeEquals(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string') return false;
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}
