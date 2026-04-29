# Fix — Cert importé uniquement dans TrustedPublisher (pas Root)

**Date :** 2026-04-24
**Kind :** Fix
**Température :** molle
**Statut :** acté

## Décision

L'installer importe le certificat auto-signé **uniquement** dans
`Cert:\CurrentUser\TrustedPublisher`. On retire l'import dans
`Cert:\CurrentUser\Root` qui déclenchait un dialog Windows non-skippable.

## Pourquoi

- Retour terrain immédiat sur la 0.3.1 : le test d'install montrait un
  popup Windows « Vous allez installer un certificat d'une autorité de
  certification... Voulez-vous installer ce certificat ? ». Casse
  l'expérience « un click » visée par la 0.3.1.
- Ce popup est **forcé par Windows** pour toute écriture dans le store
  `Root` d'un utilisateur — aucun flag `certutil` (même `-f`) ne
  l'éteint. Seule la suppression de l'étape Root l'évite.
- Pour un add-in VSTO, `TrustedPublisher` est **suffisant** : le trust
  y est direct (on trust explicitement ce cert-là comme publisher
  légitime), pas besoin que la chaîne remonte à une CA connue. Le
  script PS historique importait les deux par précaution belt-and-
  suspenders, pas parce que c'était nécessaire.

## Conséquences

- `adapter-vsto/installer/MathCursor.iss` : une seule entrée `[Run]`
  (TrustedPublisher) + une seule entrée `[UninstallRun]` équivalente.
- Version bump 0.3.1 → **0.3.2** (patch, on continue le fix UX install).
- `releases.html` : entrée 0.3.2 ajoutée en tête, 0.3.1 archivée.
- Installer 0.3.1 supprimé de R2 (trop court pour diffusion), 0.3.2
  remplace `latest.exe`.

## Alternatives considérées

- **Garder Root et afficher un texte explicatif** avant l'install pour
  préparer l'utilisateur au popup. Rejeté : même avec explication, un
  popup Windows avec SHA1 en hex demande à un lycéen / prof de faire
  un acte de foi. Mauvais signal pour la confiance produit.
- **Acheter un cert CA publique** (Sectigo ~180 €/an) : zéro popup,
  zéro import. Rejeté pour l'instant — on n'est pas à l'échelle qui
  justifie ce coût récurrent. À reconsidérer quand on passera en
  public ou qu'on aura 100+ testeurs.

## Risque résiduel

Si un jour une version de Word vérifie strictement la chaîne de
certification plutôt que le direct-trust `TrustedPublisher`, l'add-in
pourrait ne plus charger. Non observé à date sur Word 2016-2021 ni
365. Si ça remonte, on bascule sur le plan B (CA publique).

## Validé par l'utilisateur

Retour du bug :
> Screenshot du popup « Avertissement de sécurité / Vous allez installer
> un certificat d'une autorité de certification qui dit représenter :
> MathCursor »

Validation du fix proposé (« Plan A ») :
> "on teste plan A, push maintenant"

## Statut

acté, 0.3.2 live sur `mathcursor.pages.dev/download/latest.exe`. Test
terrain en cours côté utilisateur.
