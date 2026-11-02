# Signature des binaires (code signing)

Process figé pour signer les binaires natifs embarqués (`analyze`, `mc-ner`,
`mc-popup`) avant une **diffusion publique** (Marketplace VS Code, .oxt distribué).

## Quand signer ?

- **Beta** (le PAP + quelques profs, install manuelle) → **on NE signe pas**.
  Au 1er lancement, SmartScreen propose « Informations complémentaires →
  Exécuter quand même » (un clic). Coût : 0 €.
- **Diffusion publique** → **signer**, surtout à cause de **`mc-popup`** : il pose
  un hook clavier global (`WH_KEYBOARD_LL`), signature typique d'un keylogger →
  un exe **non signé** est un faux positif antivirus fréquent (quarantaine →
  popup cassée chez une partie des utilisateurs). Signé = « éditeur connu ».

Le **VSIX** est signé automatiquement par le Marketplace (signature de dépôt) ;
seuls les **exes embarqués** ont besoin de la signature Authenticode ci-dessous.

## Quel certificat ?

Choix acté : **Certum Open Source Code Signing, version cloud (SimplySign)**, au
nom de l'auteur (ADR [2026-11-02-Feat-code-signing-certum](decisions/2026-11-02-Feat-code-signing-certum.md)).
Le dépôt GitHub est public et sous GPLv3, ce qui ouvre le tarif open source.

| Option | Coût | Token ? | Notes |
|---|---|---|---|
| **Certum Open Source, cloud** (retenu) | 49 €/an | non (SimplySign) | particulier, dépôt public ; session SimplySign ouverte pour signer |
| SignPath Foundation | gratuit | non | build en CI obligatoire, publisher = la fondation, règle « rien sur le réseau sans demande » |
| Azure Artifact Signing (ex Trusted Signing) | ~10 $/mois | non | particuliers **US/Canada seulement** ; en France, entreprise uniquement |
| OV classique (Sectigo/SSL.com via revendeur) | ~200 €/an | USB ou cloud | — |
| EV | ~300–500 €/an | USB ou cloud | plus de réputation SmartScreen instantanée depuis 2024 |

**Réputation SmartScreen** : sans signature, elle est attachée au fichier et
repart de zéro à chaque version. Signé, elle s'attache à l'éditeur et se garde
d'une version à l'autre, mais se construit avec les téléchargements (aucun
certificat ne la donne dès le premier jour).

**Horodatage = signature valable après expiration du certificat** : toujours
signer avec `/tr http://time.certum.pl /td sha256`.

## Signer l'add-in Word (installeur)

Ouvrir une session SimplySign Desktop (code de l'appli mobile), relever
l'empreinte du certificat dans `certmgr.msc` (Personnel), puis :

```
powershell -ExecutionPolicy Bypass -File adapter-vsto/installer/build.ps1 -SignThumbprint <empreinte>
```

Sans `-SignThumbprint`, le build reste non signé (certificat auto-signé pour les
manifestes VSTO), comme avant.

## Comment signer (flux unifié VSCode + LibreOffice)

On signe **à la source** (`rust/target/release/`) ; les builds recopient ensuite
les binaires **signés** dans les paquets — donc **aucune** modif des scripts de
build.

```
1. cargo build --release                         # (rust/) -> exes NON signés
2. scripts/sign-binaries.ps1 -Azure -MetadataPath scripts/trusted-signing.json
   #   ou : -Thumbprint <empreinte d'un cert installé>
   #   ou : -PfxPath cert.pfx -PfxPassword (Read-Host -AsSecureString)
3. node adapter-vscode/extension/build.mjs       # propage les exes signés -> out/
   python adapter-libreoffice/build_oxt.py       # propage -> bin/<tag>/ du .oxt
4. (VSCode)      vsce package --target <win32-x64|…>   # 1 VSIX par plateforme
   (LibreOffice) MathCursor.oxt déjà produit à l'étape 3
```

En pratique au palier alpha (**non signé**), les VSIX multiplateforme sont
construits par la CI `vscode-vsix` (un runner par OS) puis distribués depuis le
site : `tools/cloudflare/deploy.sh vsix <version> <dossier-artifacts>` (cf.
`tools/cloudflare/README.md` §Publier un VSIX). La signature ci-dessous s'insère
avant l'empaquetage le jour où l'on passe en diffusion large.

Azure Trusted Signing : prérequis `dotnet tool install --global sign` + `az login`,
et un fichier `scripts/trusted-signing.json` (endpoint + compte + profil de cert)
— **non committé** (propre à ton tenant). Modèle :

```json
{
  "Endpoint": "https://<region>.codesigning.azure.net",
  "CodeSigningAccountName": "<ton-compte>",
  "CertificateProfileName": "<ton-profil>"
}
```

## Vérifier

```
signtool verify /pa rust\target\release\mc-popup.exe
```

(`scripts/sign-binaries.ps1` le fait déjà en fin de run pour les modes
thumbprint/PFX.)
