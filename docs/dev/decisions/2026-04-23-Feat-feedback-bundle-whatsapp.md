# Feat — Feedback via bundle zip + groupe WhatsApp

**Date :** 2026-04-23
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Pas de télémétrie réseau passive. À la place, bouton **"Signaler un souci"**
dans le ruban qui construit un zip (log tail + screenshot + contexte Word) et
le met au presse-papier. Lien direct vers le groupe WhatsApp beta-testeurs,
email de contact `come2percin@wanadev.fr` en secours.

## Pourquoi

- CLAUDE.md : "Pas de backend, pas de télémétrie réseau, pas de cloud".
- Les beta-testeurs incluent un mineur (PAP) et quelques profs — consentement
  explicite + RGPD simplifiés si l'utilisateur envoie lui-même le rapport.
- WhatsApp = canal que tout le monde a déjà ; messages vocaux = énorme pour un
  ado PAP qui peine à écrire un ticket détaillé et pour un prof pressé.
- Le bouton de ruban capture le contexte technique pile au moment du bug, sans
  demander à l'utilisateur de reproduire ou décrire.

## Conséquences

- `FeedbackBundle` : génère zip en `%Temp%`, copie au presse-papier, ouvre le
  lien du groupe WhatsApp dans le navigateur.
- Email de contact pré-câblé en dur.
- Si un jour on veut des métriques agrégées, on saura que la décision initiale
  était "pas de backend, feedback humain uniquement".

## Validé par l'utilisateur

> "oui ! avec tout pret à envoyer"

Puis, pour le groupe WhatsApp :
> "https://chat.whatsapp.com/DewkjN8rwoAJeDtAd8yAth"

## Statut

acté
