/**
 * Middleware racine : redirige le trafic *.pages.dev vers le domaine
 * canonique mathcursor.com (301), chemin et query string préservés.
 *
 * Pourquoi un middleware et pas `_redirects` : sur Cloudflare Pages, le
 * fichier `_redirects` matche uniquement le CHEMIN, pas le hostname — il ne
 * peut donc pas distinguer pages.dev du domaine custom. Ce middleware, lui,
 * lit le Host de la requête.
 *
 * EXCEPTION /api/* — NE PAS rediriger. Les add-ins déjà installés postent en
 * dur sur `pages.dev/api/v1/usage` et `/api/v1/report`. Un 301 sur un POST est
 * suivi par le HttpClient .NET Framework mais en transformant le POST en GET
 * et en JETANT le body → le serveur reçoit un GET sans `count`, renvoie 405,
 * et le compteur n'arrive jamais en R2. On sert donc /api/* directement sur
 * pages.dev (endpoint stable visé par le binaire). Cf. ADR
 * 2026-06-29-Fix-pages-redirect-breaks-api-post.
 *
 * S'exécute en tête de chaîne : couvre aussi /admin, /download (GET, qu'un 301
 * ne casse pas) avant qu'elles ne répondent. Sur le domaine custom, il ne fait
 * qu'appeler next() — coût négligeable.
 */
export async function onRequest(context) {
  const url = new URL(context.request.url);
  if (url.hostname.endsWith('.pages.dev') && !url.pathname.startsWith('/api/')) {
    url.hostname = 'mathcursor.com';
    return Response.redirect(url.toString(), 301);
  }
  return context.next();
}
