# Feat — Feedback in-popup "Signaler une erreur"

**Date :** 2026-04-24
**Kind :** Feat
**Température :** molle (le scaffold `HttpFeedbackSender` est lui **provisoire** tant que le backend n'est pas déployé)
**Statut :** acté

## Décision

Lien "Signaler une erreur" ajouté en **dernière ligne** de la popup de
suggestions. Click → dialog **modal** vis-à-vis de Word avec :
- Texte NER détecté + formule sélectionnée (pré-remplis, read-only).
- Textarea "Ce qui ne va pas" (focus initial).
- Email optionnel.
- Logs techniques repliables (tail 16 KB).
- Boutons Annuler / Envoyer.

Envoi via abstraction `IFeedbackSender` avec 2 implémentations
(`ClipboardFeedbackSender` et `HttpFeedbackSender`), choisies automatiquement
au runtime via `FeedbackSenderFactory` selon env var ou fichier config.

## Pourquoi

- Le bouton "Signaler un souci" du ruban reste global (zip complet pour bug
  grave ou doc). Le lien in-popup est **contextuel** : "cette suggestion
  précise est fausse", beaucoup plus actionnable pour itérer sur l'engine.
- Pas de backend requis dès jour 1 : `ClipboardFeedbackSender` met le JSON
  dans le presse-papier, l'utilisateur colle dans WhatsApp/email. Marche.
- Scaffold HTTP prêt : le jour où un backend est déployé, une variable d'env
  `MATHCURSOR_FEEDBACK_URL` ou un fichier `%AppData%\MathCursor\feedback.url`
  bascule l'envoi en automatique.
- `UserId` anonyme (GUID persistant dans `%AppData%\MathCursor\user.id`) pour
  corréler les feedbacks d'un même testeur sans info identifiante.
- `SessionId` = GUID généré au startup, tenu en mémoire, disparaît avec Word.

## Contrat API (à implémenter côté backend)

```
POST {endpoint}
Content-Type: application/json
Body : {
  version, timestamp, user_id, session_id,
  ner_text, recognized_formula,
  user_message, user_email,
  log_tail, word_version, os_version
}
Response 2xx = succès
```

## Conséquences

- 7 nouveaux fichiers dans `Host/Feedback/` :
  `FeedbackReport.cs`, `IFeedbackSender.cs`, `FeedbackJson.cs`,
  `UserIdStore.cs`, `ClipboardFeedbackSender.cs`, `HttpFeedbackSender.cs`,
  `FeedbackSenderFactory.cs`.
- `UI/FeedbackDialog.cs` : dialog WPF en code (pas de XAML).
- Lien ajouté dans `SuggestionPopupWindow` (événement `ReportRequested`).
- `SuggestionService` branche le handler, construit le rapport pré-rempli,
  ouvre le dialog.
- Reference NuGet : `System.Net.Http` pour HttpClient.

## Validé par l'utilisateur

Brief initial :
> "tu peux me planifier une feature de feedback pour les beta tests: en bas
> de la popup derniere ligne pas trop grand. "signaler une erreur" qui ouvre
> une popup avec textbox: le texte NER en cours. si jamais la formule
> reconnue. et une textbox/textarea avec un message + les logs en bas de la
> TB un email facultatif, un bouton envoyer. se brancherr de maniere ouverte
> pour l'envoi. j'imagine une petite api mais je ferai apres"

Précision sur la version :
> "et version du plugin"

Réponses explicites aux 3 questions d'arbitrage :
> "1 modal word / 2. un seul lien en bas de la popup qui concerne cette
> popup. / 3 UserId identifier ca peut etre pas mal (tres generic)"

## Statut

acté, phase 1+2 codées (UI + scaffold HTTP). Phase 3 (déploiement backend)
à planifier séparément.
