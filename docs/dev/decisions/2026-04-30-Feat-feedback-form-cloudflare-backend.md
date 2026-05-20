# Feat — Formulaire "Signaler une erreur" pré-rempli + backend Cloudflare

**Date :** 2026-04-30
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

1. Remplacer le flow actuel "Signaler un souci" (zip + clipboard +
   instructions WhatsApp/email manuelles) par une **fenêtre WPF modale
   `FeedbackWindow`** pré-remplie avec la dernière action effectuée :
   - Saisie source (texte tapé par l'utilisateur)
   - Suggestion popup (LaTeX proposé)
   - Conversion insérée dans le OMath Word
2. **3 boutons d'action** dans la fenêtre :
   - **Annuler** — ferme sans rien faire
   - **Copier dans un mail** — payload texte dans clipboard +
     `mailto:come2percin@wanadev.fr` (chemin first-class, pour proxy
     d'entreprise et users qui veulent traçabilité mail)
   - **Envoyer** — POST direct vers backend Cloudflare (chemin principal)
3. **Bascule auto** Envoyer → Copier mail si le POST échoue (timeout,
   proxy bloque), sans perdre la saisie.
4. **Pré-remplissage** via un nouveau `LastActionSnapshot` exposé par
   `SuggestionService.GetLastAction()` (mis à jour à chaque
   `ShowPopup` + `InsertOMathAt`). Pas de queue, juste la dernière action.
5. **Backend** : Pages Function Cloudflare `docs/functions/api/v1/report.js`
   (versionné dès le départ) → R2 bucket `mathcursor-reports` (1 JSON +
   1 PNG si screenshot par report) + KV `RATE_LIMIT_KV` (5
   reports/h/IP).
6. **Confidentialité** : aucun identifiant envoyé, jamais le doc entier,
   screenshot opt-in coché par défaut mais visible, log opt-in décoché,
   page `/privacy.html` listant les engagements, retention R2 6 mois.
7. **Phasage A→D** sur ~9-12h, A=backend testable au curl, B=snapshot,
   C=fenêtre WPF + 2 actions, D=page privacy.

Spec d'implémentation détaillée : voir
[brief 2026-04-30-feedback-form-with-cloudflare-backend.md](../briefs/2026-04-30-feedback-form-with-cloudflare-backend.md).

## Pourquoi

### Symptôme observé

Le bouton "Signaler" actuel produit un zip + ouvre un dialog avec
instructions ("colle-le dans WhatsApp ou email"). Résultat : très peu
de retours utilisateurs, et ceux qui arrivent sont souvent juste le zip
sans contexte ("ça marche pas"). Trop d'étapes pour des élèves PAP /
profs pressés.

### Bénéfices attendus

- **1 clic** au lieu de 5 étapes manuelles (vs WhatsApp/email
  copy-paste)
- Le contexte est **pré-rempli** par l'add-in plutôt que devoir être
  ressaisi de tête par l'user
- Le bouton "Copier mail" garde la solution offline pour les
  contraintes proxy d'entreprise
- Backend Cloudflare = **collection structurée** des bugs (R2 list +
  download via wrangler), permettra de prioriser fixes et patterns

### Alternatives écartées

- **Worker Cloudflare dédié** plutôt que Pages Function → écartée :
  on a déjà la stack Pages, pas de raison d'ajouter un domaine + un
  déploiement.
- **GitHub Issues API** comme backend → écartée : nécessite token
  embarqué dans l'add-in (fuite) ou OAuth (complexité), et expose les
  reports publiquement.
- **Email SMTP direct** depuis l'add-in → écartée : nécessite credentials
  SMTP, antivirus bloque souvent.
- **Garder uniquement le bouton "Copier dans un mail"** (sans envoi
  direct) → écartée : casse l'objectif "1 clic".

## Statut + Citation

> peux tu me faire un brief aussi pour ce qu'il se passe quand on clique
> dans "signaler une erreur"  j'aimerai que : prérempli saisie initiale
> / wpf popup reconnu / conversion oMath + un champs libre pour
> expliquer l'erreur. et un bouton envoyer. avec un backend sur
> cloudflare pour recuperer le "bug" proprement
>
> dans la popup tu fais deux boutons: "envoyer directement" ou "copier
> coller dans un mail"
>
> ok go la dessus

— come, 2026-04-30

## Conséquences / suivi

- **Effet sur ressources Cloudflare** : création d'un R2 bucket
  `mathcursor-reports` + 1 KV namespace `RATE_LIMIT_KV` dans le compte
  CF du projet. Coût attendu : gratuit en pratique (free tiers larges).
- **Versioning endpoint** : `/api/v1/report` (versionné dès le MVP pour
  éviter dette future).
- **Page privacy** à publier sur `mathcursor.pages.dev/privacy.html`
  avant le déploiement du chemin "Envoyer" (sinon on collecte sans
  disclaimer accessible).
- **Supersedes partiellement** :
  [2026-04-23-Feat-feedback-bundle-whatsapp](2026-04-23-Feat-feedback-bundle-whatsapp.md)
  (le flow zip+clipboard reste accessible via "Copier dans un mail" si
  toggles screenshot/log activés, mais n'est plus le défaut).
