# UX — L'installer importe le certificat lui-même (plus de PowerShell)

**Date :** 2026-04-24
**Kind :** UX
**Température :** molle
**Statut :** acté

## Décision

L'installer Inno Setup embarque `mathcursor.cer` et l'importe automatiquement
dans `Cert:\CurrentUser\Root` + `Cert:\CurrentUser\TrustedPublisher` via
`certutil.exe` lancé depuis la section `[Run]`. L'étape utilisateur
« ouvrir PowerShell + coller une commande » est **supprimée** du parcours
d'installation.

Le guide d'installation passe de 3 étapes à **2** : (1) télécharger l'exe,
(2) lancer → c'est fini.

## Pourquoi

- Friction énorme pour les beta-testeurs, particulièrement l'élève PAP et les
  profs : ouvrir PowerShell et coller une commande `iwr ... | iex` n'est pas
  intuitif, et peut même déclencher une alerte Windows SmartScreen.
- Aucune raison technique de séparer l'étape — `certutil -user -addstore`
  ne nécessite pas d'admin, fait exactement la même chose qu'`Import-Certificate`
  côté PowerShell.
- Bénéfice aussi pour la crédibilité : un installeur qui fait tout en un click
  paraît plus professionnel.

## Conséquences

- `adapter-vsto/installer/MathCursor.iss` :
  - `[Files]` : ajout de `mathcursor.cer` dans `{tmp}` avec `deleteafterinstall`.
  - `[Run]` : deux entrées `certutil.exe -user -addstore` (Root puis
    TrustedPublisher) avec `runhidden` pour ne pas flasher de fenêtre noire.
- `adapter-vsto/installer/build.ps1` : copie du `.cer` depuis `docs/` vers
  `installer/payload/` avant la compilation Inno Setup.
- `docs/install-cert.ps1` : conservé en fallback (public, toujours téléchargeable)
  mais le site ne l'affiche plus dans le guide principal.
- Version bump patch 0.3.0 → **0.3.1** (amélioration d'install sans changement
  d'algo). Pas un mineur car pas de nouvelle feature produit.
- Guide d'installation dans `docs/index.html` passe à 2 étapes. Le texte EN
  de la section `I18N.en` suivra la même simplification.

## Alternatives considérées

- **Signer avec un certif d'une CA publique** (Sectigo, DigiCert) — plus aucun
  import nécessaire côté utilisateur. Coût : ~200 €/an. Prématuré pour une beta
  privée, on n'en est pas là. À reconsidérer quand on voudra ouvrir plus large.
- **Garder le script PS** en parallèle pour ceux qui préfèrent — rejeté :
  ajoute de la confusion, deux chemins à documenter. Le fichier reste dispo
  (URL publique inchangée), juste plus mis en avant.

## Validé par l'utilisateur

Observation du problème :
> "dans le guide d'install, j'ai l'impression que le powershell n'est pas
> necessaire.. je me trompe ?"

Approbation de la correction :
> "vas y"

## Statut

acté. Version 0.3.1 à builder + uploader + releases.html à mettre à jour.
