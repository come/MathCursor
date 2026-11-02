# Feat — Signer l'installeur Word avec un certificat Certum Open Source

**Date :** 2026-11-02
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Citation acté

> « ok maintenant j'aimerai qu'on fasse la certification pour passer les antivirus, j'ai bcp
> d'eleve qui me disent des trucs de ce genre : "MathCursor n'est pas fréquemment téléchargé" »
> — utilisateur, 2026-11-02

> « ok ca a l'air plus lourd que certum du coup.. » puis « yes » (préparer l'ADR et le build
> pendant la validation d'identité) — utilisateur, 2026-11-02

## Contexte

- Les élèves voient l'avertissement SmartScreen (Edge et Windows) « n'est pas fréquemment
  téléchargé » sur `MathCursor-Setup-X.exe`. L'installeur n'est pas signé : la réputation
  SmartScreen est attachée au fichier, donc **chaque version repart de zéro**.
- Les manifestes VSTO sont signés par un certificat **auto-signé**
  (`MathCursor_TemporaryKey.pfx`) que l'installeur importe dans `TrustedPublisher`.
- Signer avec un certificat d'une autorité reconnue attache la réputation à **l'éditeur**, qui
  se garde d'une version à l'autre. Depuis 2024, aucun certificat (même EV) ne supprime
  l'avertissement dès le premier jour : la réputation se construit avec les téléchargements.

## Décision

- **Certificat Certum « Open Source Code Signing », version cloud (SimplySign)**, au nom de
  Côme de Percin (publisher affiché : « Open Source Developer, Côme de Percin »). Pas de clé
  USB : SimplySign Desktop expose le certificat dans le magasin Windows le temps de la
  session ; on signe avec `signtool` par **empreinte** (`/sha1`), horodatage Certum
  (`http://time.certum.pl`, SHA-256), donc la signature reste valide après expiration.
- **Ce qui est signé** (build local, `build.ps1`) :
  1. `MathCursor.dll` et les DLL first-party (`MathCursor.Engine`, `.Serialization`,
     `.HostContract`), **avant** la génération des manifestes VSTO (le manifeste contient
     leur empreinte) ;
  2. les manifestes VSTO (`MathCursor.dll.manifest`, `MathCursor.vsto`) avec le certificat
     Certum au lieu du certificat auto-signé ;
  3. l'installeur Inno Setup et son désinstalleur (directive `SignTool`,
     `SignedUninstaller=yes`).
- L'installeur continue d'importer la **partie publique** du certificat dans
  `TrustedPublisher` (pas `Root`), pour que VSTO charge l'add-in sans invite.
- **Sans certificat présent, le build reste identique à aujourd'hui** (signature désactivée,
  certificat auto-signé), pour ne pas bloquer une release.
- Les binaires Rust (LibreOffice, VS Code) suivront avec `scripts/sign-binaries.ps1
  -Thumbprint`, dans un second temps.

## Alternatives écartées

- **SignPath Foundation** (gratuit) : build obligatoire en CI GitHub Actions (outils VSTO
  absents des runners), politique de signature qui impose de ne rien envoyer sur le réseau
  sans demande explicite (le compteur d'usage opt-out, ADR 2026-06-18, imposerait un écran
  d'installation dédié voire un passage en opt-in), approbation discrétionnaire. Jugé trop
  lourd. Avantage perdu : la réputation déjà acquise par le certificat de la fondation.
- **Azure Artifact Signing** (ex Trusted Signing, ~10 $/mois) : réservé aux particuliers des
  États-Unis et du Canada ; en France, seulement pour une entreprise.
- **OV commercial** (200 € et plus par an) ou **EV** : plus cher, pas de gain de réputation
  immédiat depuis 2024.

## Conséquences

- Coût : 49 € (« Open Source Code Signing in the Cloud », boutique Certum 2026-11-02), renouvellement annuel.
- Chaque release demande une session SimplySign ouverte sur le PC de build (code de l'appli
  mobile) : la signature ne s'automatise pas en CI.
- `docs/dev/SIGNING.md` est corrigé (Azure n'est pas ouvert aux particuliers en France, l'EV
  ne donne plus de réputation instantanée).
- En attendant le certificat : réponse type aux élèves (« Conserver », « Informations
  complémentaires → Exécuter quand même ») et soumission de l'installeur à Microsoft comme
  faux positif SmartScreen (à faire par l'utilisateur, avec son compte).
