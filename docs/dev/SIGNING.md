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

Projet **privé / individu** → certificat **commercial** (le tarif « open source »
de Certum exige un dépôt public, donc exclu ici).

| Option | Coût | Token ? | Notes |
|---|---|---|---|
| **Azure Trusted Signing** (recommandé) | ~10 $/mois | non (cloud) | individu OK, intégré `signtool`/tool `sign` |
| OV classique (Sectigo/SSL.com via revendeur) | ~200 $/an | USB | — |
| EV | ~300–500 $/an | USB | réputation SmartScreen **instantanée** |

**Horodatage = signature valable à vie** : on peut ne payer Azure que le **mois
où l'on signe** une release (puis arrêter). Garder le compte (~10 $/mois continu)
évite la re-validation d'identité + préserve la réputation SmartScreen entre
releases ; le coup-par-coup la fait redémarrer.

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
   python libreoffice-ext/build_oxt.py           # propage -> bin/<tag>/ du .oxt
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
