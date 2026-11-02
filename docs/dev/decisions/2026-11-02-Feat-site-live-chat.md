# Feat — Chat en direct sur le site MathCursor (réponse depuis /admin, alerte e-mail)

**Date :** 2026-11-02
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-01-Feat-admin-backoffice-cloudflare](2026-05-01-Feat-admin-backoffice-cloudflare.md) (admin Basic Auth), [2026-06-16-Feat-contact-form-r2](2026-06-16-Feat-contact-form-r2.md) (formulaire de contact)

## Citation acté

> « tu peux checker ce qu'on a fait ici sur le chatbot, j'aimerai faire aussi un
> chatbot pour mathcursor (independant du coup) mais sur le meme systeme, une
> page d'admin ou je vais repondre si besoin » — utilisateur, 2026-11-02.

> Réponses de cadrage, 2026-11-02 : type = « Chat humain » (pas d'IA) ;
> alerte = « E-mail ».

## Contexte

Le système de chat des démos WanadevDigital (`D:\DemosClient\hub`) : widget en
Shadow DOM installé en une ligne, relais (Worker + D1) qui stocke conversations
et messages, console où l'équipe répond, présence « en ligne / absent », réponse
automatique unique qui demande des coordonnées quand personne n'est là. Il sert
aussi la mesure d'audience des démos.

MathCursor veut le même chat, **indépendant** (son compte Cloudflare, sa base,
son admin), pour une seule personne qui répond (l'auteur), qui n'a pas l'admin
ouverte en permanence.

## Décision

Porter le relais **dans les fonctions Pages du site** (pas de Worker relais
séparé : le site a déjà ses fonctions et son `/admin`) :

- **Base** : D1 `mathcursor-chat` (binding `CHAT_DB`), tables `conversations`,
  `messages`, `presence` (une seule ligne : l'auteur).
- **API visiteur** `functions/api/v1/chat/[[path]].js` : `status`, `send`,
  `contact`, `poll`. Même origine que la page (pas de CORS). Session visiteur
  aléatoire, stockée en empreinte SHA-256 ; un visiteur ne lit que sa
  conversation. Limite de débit en mémoire.
- **API admin** `functions/admin/api/chat/[[path]].js` : `sync` (présence +
  liste + fil, marquage lu), `reply`, `archive`, `away`. Protégée par le Basic
  Auth existant de `/admin/*` ; pas de jeton ni de multi-prénoms (une personne).
- **Logique partagée** `functions/_chat.js` (non routé).
- **Admin** `admin/chat.html` : liste des conversations (non lus), fil, réponse,
  bouton Disponible / Absent ; lien depuis `admin/index.html`.
- **Widget** `chat-widget.js` (JS sans build, Shadow DOM, FR/EN selon la langue
  du site), installé sur l'accueil, Profs, Parents & PAP, Installation. Pilotage
  `window.MCChat.open()/close()/toggle()`.
- **Alerte e-mail** : petit Worker `mathcursor-notify` (binding `send_email`,
  Email Routing de `mathcursor.com`, destination vérifiée = adresse de
  l'auteur), appelé par liaison de service `NOTIFY` depuis l'API visiteur quand
  l'auteur n'est pas en ligne. Au plus un e-mail par conversation toutes les
  15 min. Si la liaison manque, le chat marche sans alerte.

**Différences assumées avec le hub** :
- **E-mail seulement, jamais de téléphone** : le public est en grande partie
  mineur.
- **Aucune mesure d'audience** (le site a déjà ses propres stats).
- **Conservation 12 mois** : conversations purgées 12 mois après leur dernier
  message (purge lors des synchronisations admin).
- Pas de Worker relais sans adresse publique : le chat vit dans le projet Pages.
- Page Confidentialité mise à jour (messages et e-mail conservés pour répondre,
  durée, pas de tiers).

## Écartés

- **IA qui répond d'abord** : messages souvent de mineurs envoyés à un tiers,
  clé API payante, garde-fous ; refusé au cadrage.
- **Réutiliser `wd-console` / `wd-relay`** : mélange avec WanadevDigital, refusé
  (« indépendant »).
- **Notification ntfy / rien** : e-mail choisi au cadrage.
- **Fournisseur d'e-mail tiers (Resend…)** : clé API et vérification DNS en plus ;
  Email Routing est gratuit et le domaine est déjà chez Cloudflare (aucun MX
  existant, rien à casser).

## Actions manuelles (auteur)

- Créer la base : `npx wrangler d1 create mathcursor-chat` (id dans
  `docs/wrangler.toml`).
- Activer Email Routing sur `mathcursor.com` et vérifier l'adresse destinataire
  (clic dans l'e-mail de Cloudflare).
- Déployer `mathcursor-notify`, puis le site.

### Addendum 2026-11-02 : envoyer la conversation au visiteur

> « dans l'admin si on repond, et que la personne n'est plus sur le chat, tu peux
> faire un bouton pour envoyer une copie de la conversation par email ? », puis
> « option B », puis « Finalement l'option A » — utilisateur, 2026-11-02.

Email Routing n'écrit qu'aux adresses vérifiées (l'auteur) : impossible d'écrire au
visiteur depuis le Worker. Retenu : bouton **« Envoyer la conversation par e-mail »**
(remplace « Répondre par e-mail ») qui ouvre la messagerie de l'auteur (`mailto:`)
adressée à l'e-mail laissé, avec toute la conversation (visiteur + auteur, sans
réponse auto ni ligne système) dans la langue du visiteur ; plus **« Copier la
conversation »** si la messagerie tronque un long `mailto:`. Écartés : Cloudflare
Email Service (offre Workers Paid requise) et Resend (sous-traitant de plus),
proposés puis refusés.
