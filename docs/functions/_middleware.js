/**
 * Middleware racine : redirige tout le trafic *.pages.dev vers le domaine
 * canonique mathcursor.com (301), chemin et query string préservés.
 *
 * Pourquoi un middleware et pas `_redirects` : sur Cloudflare Pages, le
 * fichier `_redirects` matche uniquement le CHEMIN, pas le hostname — il ne
 * peut donc pas distinguer pages.dev du domaine custom. Ce middleware, lui,
 * lit le Host de la requête.
 *
 * S'exécute en tête de chaîne : couvre donc aussi /admin, /download, /api/*
 * (qui ont leurs propres Functions) avant qu'elles ne répondent.
 * Sur le domaine custom, il ne fait qu'appeler next() — coût négligeable.
 */
export async function onRequest(context) {
  const url = new URL(context.request.url);
  if (url.hostname.endsWith('.pages.dev')) {
    url.hostname = 'mathcursor.com';
    return Response.redirect(url.toString(), 301);
  }
  return context.next();
}
