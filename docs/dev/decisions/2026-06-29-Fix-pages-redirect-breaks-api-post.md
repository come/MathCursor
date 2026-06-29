# Fix — Le redirect canonique *.pages.dev → mathcursor.com (301) cassait les POST /api/*

**Date :** 2026-06-29
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-18-Feat-usage-counter-telemetry.md](2026-06-18-Feat-usage-counter-telemetry.md), `docs/functions/_middleware.js`, `docs/functions/api/v1/usage.js`, `docs/functions/api/v1/contact.js`, `adapter-vsto/src/MathCursor/Host/Feedback/FeedbackSenderFactory.cs`

## Citation acté

> « oui go ! » — utilisateur, 2026-06-29
> (en réponse au diagnostic : le compteur de formules ne remontait plus depuis
> le 2026-06-25 à cause du redirect canonique qui casse les POST des add-ins
> déjà installés)

## Contexte

Le 2026-06-25, le commit `dd87f7b` a ajouté `docs/functions/_middleware.js` : un
middleware racine qui redirige **tout** le trafic `*.pages.dev` vers le domaine
canonique `mathcursor.com` en **301**, `/api/*` compris (le commentaire le
notait même comme une fonctionnalité).

Or les add-ins **déjà installés** chez les bêta-testeurs postent en **dur** sur
`https://mathcursor.pages.dev/api/v1/usage` (compteur de formules) et
`/api/v1/report` (feedback) — cf. `FeedbackSenderFactory.cs` (`DefaultUsageUrl`
/ `DefaultFeedbackUrl`). Sur un **301**, le `HttpClient` du .NET Framework suit
la redirection **mais transforme le POST en GET et abandonne le body** (comportement
historique RFC, identique à `HttpWebRequest`). Le serveur reçoit donc un GET sans
`count` → renvoie 405 (le handler `onRequestGet` d'aide) → `UsageStatsClient`
journalise `usage_flush_http_405` et **conserve** le compteur local (jamais vidé).

Conséquence : **plus aucune donnée n'atteint R2 depuis le 2026-06-25**, la courbe
« Formules converties » de `/admin/stats.html` reste plate, et l'envoi de
**feedback** est cassé par le même mécanisme. Les stats de **téléchargement**
(`/download/*`, GET) et l'**update checker** (GET) ne sont **pas** touchés : un
301 sur un GET ne perd rien.

Vérifié en live : `POST pages.dev/api/v1/usage` → `301 Location: mathcursor.com/...`
→ suivi en `GET` → `405`.

## Décision

**Exclure `/api/*` du redirect** dans `_middleware.js` : on sert les routes API
**directement sur `pages.dev`** (l'endpoint stable que vise le binaire), sans
redirection. Tout le reste (`/`, `/admin`, `/download`, pages HTML) continue de
rediriger vers `mathcursor.com`.

```js
if (url.hostname.endsWith('.pages.dev') && !url.pathname.startsWith('/api/')) {
  url.hostname = 'mathcursor.com';
  return Response.redirect(url.toString(), 301);
}
```

Couche cible : **middleware Cloudflare Pages** uniquement. Aucune donnée nouvelle,
aucun impact moteur/binaire. Le correctif **sauve les add-ins déjà déployés sans
mise à jour** — c'est le point critique : on ne peut pas patcher un binaire déjà
installé.

## Tradeoff & alternatives écartées

- **Passer le 301 en 308** (préserve méthode + body) : écarté — tous les clients
  .NET Framework installés ne suivent pas un 308 de façon fiable, et ça force un
  aller-retour réseau supplémentaire. Ne pas rediriger `/api/*` est plus sûr.
- **Corriger les URLs en dur de l'add-in vers `mathcursor.com`** : utile pour les
  **futurs** builds, mais **ne sauve pas** les installés (toujours sur pages.dev).
  À faire en complément, pas en remplacement.
- **Retirer complètement le middleware** : écarté — le redirect canonique des
  pages HTML (SEO, partage de liens) reste légitime ; seul `/api/*` posait
  problème.

## Conséquences

- `docs/functions/_middleware.js` : garde `&& !url.pathname.startsWith('/api/')`
  + commentaire expliquant le piège POST/301.
- À déployer (Cloudflare Pages) pour que l'effet soit live. Dès le déploiement,
  les add-ins installés reprennent l'envoi au prochain flush (perte de focus /
  shutdown) — **les compteurs locaux n'ayant jamais été vidés, rien n'est perdu**,
  le backlog accumulé depuis le 25-06 remonte d'un coup.
- **Suivi** : pointer les URLs en dur de l'add-in vers `mathcursor.com` au prochain
  build (évite de dépendre éternellement du non-redirect de pages.dev).

## Validation post-fix

1. `curl -sI https://mathcursor.pages.dev/api/v1/usage` → **405** direct (handler
   d'aide), plus de **301**.
2. `POST` réel sur `pages.dev/api/v1/usage` (`{count, version}`) → **200** + objet
   créé sous `usage/<date>/` en R2.
3. Les pages HTML (`/`, `/releases.html`) redirigent toujours en 301 vers
   `mathcursor.com`.
