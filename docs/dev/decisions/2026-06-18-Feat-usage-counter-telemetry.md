# Feat — Compteur d'usage anonyme (télémétrie légère)

**Date :** 2026-06-18
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-04-24-Feat-feedback-in-popup.md](2026-04-24-Feat-feedback-in-popup.md) (nuance sa posture « envoi uniquement sur clic »), `docs/privacy.html`, `docs/index.html`

## Citation acté

> « comme tout est gratuit je pense qu'on peut facilement justifier qu'il y'a
> une telemetrie legere et ne pas clamer sur la home qu'on est privacy first,
> sachant que je compte pas faire un truc de fou.. un compteur.. et ptetre
> desactivable dans les parametres ca suffit ? on est ok en tradeoff
> utilisateur ? c'est gratuit mais on compte les formules » — utilisateur, 2026-06-18

## Contexte

MathCursor n'a aujourd'hui **aucune** statistique d'usage produit. La seule
donnée qui quitte la machine est le rapport de la popup « Signaler une erreur »,
strictement déclenché par un clic utilisateur (cf. ADR feedback-in-popup). Pour
piloter la beta, l'auteur veut un signal basique : **combien de formules sont
converties**.

Point dur : la comm publique promet aujourd'hui l'inverse. `docs/privacy.html`
affirme « Aucune télémétrie automatique : pas d'envoi en arrière-plan, pas de
heartbeat, pas de statistiques d'usage. » (FR + EN), et le footer de la home dit
« 100 % local, sans tracking, sans compte ». Un compteur passif flushé
automatiquement est exactement ce que ces pages s'engagent à ne pas faire. La
décision assume donc un repositionnement honnête de la comm, justifié par la
gratuité de l'outil.

## Décision

Ajouter un **compteur d'usage anonyme, opt-out**, du nombre de formules
converties.

- **Compteur seulement** : un entier. Jamais de contenu (ni texte tapé, ni
  formule, ni paragraphe).
- **Aucun identifiant** : le GUID anonyme `UserIdStore` (utilisé par le feedback)
  n'est **pas** attaché au compteur. Le payload est un agrégat anonyme pur
  (`count` + `version`). Donnée non-personnelle → pas de problème de consentement
  parental pour un public mineur (fils PAP de l'auteur, élèves des profs
  beta-testeurs).
- **Pile locale** `%AppData%\MathCursor\usage.json` (`{ "pending": N }`),
  incrémentée à chaque conversion réussie (`ConversionController.CommitSelected`),
  uniquement si l'opt-out est actif.
- **Flush HTTP** anonyme sur perte de focus de Word (`WindowDeactivate`) **et**
  au `Shutdown`, vers un endpoint dédié `/api/v1/usage` (Cloudflare → R2
  `usage/{date}/{uuid}.json`, bucket `mathcursor-reports`). Anti-spam : envoi
  seulement si `pending > 0` ; sur 2xx on retranche le nombre envoyé (pas un
  reset brut, pour ne pas perdre les incréments arrivés pendant l'envoi async) ;
  sur échec on garde `pending` (retry au prochain flush).
- **Opt-out** : réglage `SendUsageStats` (défaut **true**) persisté dans
  `settings.json`, exposé via une case « Envoyer les statistiques d'usage
  anonymes » dans la fenêtre Paramètres. Coupé → rien n'est compté, stocké, ni
  envoyé.
- **Comm publique réécrite dans la même livraison** : `privacy.html` (FR+EN) et
  la home cessent de promettre « aucune télémétrie » et décrivent honnêtement le
  compteur (un nombre, aucune donnée personnelle, désactivable).

Couche cible : **adapter VSTO (L3) uniquement** côté binaire — le Core (L1) et
les contrats (L2) ne bougent pas ; la télémétrie plateforme est une affaire
d'adapter. `IUserFeedback` n'est pas touché. Plus backend Cloudflare et docs.

## Tradeoff & alternatives écartées

- **Garder « privacy first » intact + télémétrie 100 % locale (jamais envoyée)** :
  écarté car ne donne aucune visibilité centralisée à l'auteur, qui est le besoin
  produit exprimé.
- **Attacher l'identifiant anonyme (`UserIdStore`) au compteur** pour distinguer
  « utilisateurs actifs uniques » : écarté pour ce v1. Un identifiant transforme
  un agrégat non-personnel en donnée potentiellement personnelle, ce qui alourdit
  l'histoire RGPD pour un public mineur sans bénéfice proportionné à un simple
  compteur. Réversible si le besoin « actifs uniques » devient réel.
- **Opt-in explicite (désactivé par défaut)** : écarté car remonterait trop peu
  de signal en beta ; un compteur sans contenu ni identifiant est assez bénin
  pour justifier l'opt-out, à condition que la comm publique soit honnête.
- **Timer d'inactivité comme déclencheur de flush** : écarté pour le v1 (ajoute
  un `Timer` à gérer) ; `WindowDeactivate` + `Shutdown` couvrent « perte de
  focus » sans cette complexité.
- **Réutiliser `HttpFeedbackSender`** : écarté car couplé à
  `FeedbackReport`/`FeedbackJson` et à l'endpoint `/report`. On reprend le
  *pattern* (HttpClient partagé, TLS 1.2, timeout) dans un `UsageStatsClient`
  dédié au payload minimal.
- **Page admin `usage.html` dédiée** : écarté au profit d'une section dans la
  `stats.html` existante (réutilise le layout + Chart.js déjà en place).

## Conséquences

- **Code adapter (L3)** :
  - Nouveaux `Host/Usage/UsageCounter.cs` et `Host/Usage/UsageStatsClient.cs`.
  - `Host/ConversionController.cs` : incrément au succès de `CommitSelected`.
  - `ThisAddIn.cs` : abonnement `WindowDeactivate` + flush au `Shutdown`.
  - `Host/Settings/AppSettings.cs` + `SettingsStore.cs` : champ/clé
    `send_usage_stats` (clé absente → true).
  - `UI/SettingsWindow.cs` : case opt-out.
  - csproj VSTO old-style : déclaration manuelle des 2 nouveaux `.cs`.
- **Backend Cloudflare** : `functions/api/v1/usage.js` (POST→R2, calqué sur
  `contact.js`), `functions/admin/api/usage.js` (agrégation R2), section
  « Formules converties (30 j) » dans `docs/admin/stats.html`.
- **Comm publique** : `docs/privacy.html` (FR+EN) et `docs/index.html` réécrits ;
  au passage, correction de l'inexactitude pré-existante « aucun identifiant
  utilisateur » (le rapport feedback envoie déjà `user_id` via `UserIdStore` — le
  compteur, lui, reste sans identifiant).
- **Tests** : `UsageCounter` (increment / getPending / clearPending partiel /
  fichier corrompu) + round-trip `send_usage_stats` dans `SettingsStore` (incl.
  clé absente → true), projet pure-compute `adapter-vsto/tests/`.
- **API publique** : aucune signature de contrat L2 modifiée.
- **Règles MC impactées** : aucune règle analyzer. Posture privacy du projet
  nuancée (de « zéro télémétrie » à « compteur anonyme opt-out »).

## Validation post-fix

1. Convertir N formules → `usage.json` `pending = N`. Alt-tab → POST observé dans
   les logs + `pending` remis à 0. Couper le réglage → conversion → aucun fichier
   ni envoi.
2. `curl -X POST .../api/v1/usage -d '{"count":3,"version":"x"}'` → 200 + objet
   R2 sous `usage/<date>/`. `/admin/stats.html` → section « Formules converties »
   alimentée.
3. Relire `privacy.html` (FR/EN) et la home : aucune promesse ne contredit le
   binaire.
