# Fix — Nom de fichier `.oxt` stable `MathCursor.oxt` en distribution

**Date :** 2026-06-29
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-29-Feat-alpha-distribution-vsix-oxt](2026-06-29-Feat-alpha-distribution-vsix-oxt.md) (amende la conséquence R2 « objet `MathCursor-0.1.0.oxt` ») ; [2026-06-16-Feat-libreoffice-uno-python-extension](2026-06-16-Feat-libreoffice-uno-python-extension.md) (URI de script bundlées)

## Citation acté

> « Nom de fichier stable MathCursor.oxt » — utilisateur, 2026-06-29 (réponse au cadrage : choix entre nom stable / Content-Disposition / URI versionnées au build)

## Contexte

Tout utilisateur installant l'extension LibreOffice téléchargée depuis le site obtient au
lancement d'un item de menu (ou de l'auto-détection) :

```
<class 'KeyError'>: 'MathCursor.oxt'
  File ".../pythonscript.py", line 437, in getStorageUrlFromPersistentUrl
    package = self.mapPackageName2Path[packageName]
```

**Cause racine** : LibreOffice clé sa table `mapPackageName2Path` par le **nom de fichier du
`.oxt` tel qu'installé** (`lastElement(pkg.URL)`, `pythonscript.py:884`). Les URI de script
bundlées dans l'extension sont figées sur ce nom :

- `oxt/Addons.xcu` — les 3 items de menu (`convert_selection`, `autodetect_start`, `autodetect_stop`)
- `oxt/jobs.py:11` — `autodetect_autostart`

Toutes commencent par `vnd.sun.star.script:MathCursor.oxt|Scripts|python|…`. Le lookup cherche
donc la clé `MathCursor.oxt`.

Or la distribution servait un nom **versionné** : `docs/functions/_latest.js` posait
`LATEST_OXT = "MathCursor-0.1.0.oxt"`, et `download/[[filename]].js` renvoie
`Content-Disposition: attachment; filename="${resolved}"` — donc l'utilisateur enregistrait et
installait `MathCursor-0.1.0.oxt`. Clé réelle `MathCursor-0.1.0.oxt` ≠ clé cherchée
`MathCursor.oxt` → `KeyError`. (Note : `build_oxt.py` produit pourtant bien `MathCursor.oxt` ;
seul l'étage distribution renommait.)

## Décision

Nom de fichier `.oxt` **stable `MathCursor.oxt` bout-en-bout** : clé de l'objet R2,
`LATEST_OXT`, et nom servi (`Content-Disposition`). La **version** d'un `.oxt` est portée par
`description.xml` (`<version>` + `<identifier>`), jamais par le nom de fichier — LibreOffice
pilote l'upgrade/réinstall sur ces champs, pas sur le nom.

- `docs/functions/_latest.js` : `LATEST_OXT = "MathCursor.oxt"`.
- R2 (`mathcursor-releases`) : objet servi `MathCursor.oxt` ; l'objet versionné
  `MathCursor-0.1.0.oxt` est supprimé.
- `download/[[filename]].js` inchangé : il sert déjà `filename="${resolved}"`, qui devient
  `MathCursor.oxt`.

## Tradeoff & alternatives écartées

- **Servir versionné + forcer `MathCursor.oxt` via `Content-Disposition`** : écarté — l'objet R2
  reste versionné mais le double-clic dépend du nom enregistré par le navigateur (comportements
  variables selon client), donc fix moins robuste que le nom stable bout-en-bout.
- **Injecter le nom versionné dans `Addons.xcu` + `jobs.py` au build (`build_oxt.py`)** : écarté —
  couple le contenu de l'oxt à son nom de fichier ; chaque bump de version casserait les URI si
  un maillon oublie de régénérer. Le versioning d'un oxt ne passe pas par le nom de fichier.

## Conséquences

- **Code touché** : `docs/functions/_latest.js` (constante `LATEST_OXT` + commentaire).
- **Outillage (garde long terme)** : `tools/cloudflare/deploy.sh` gagne une sous-commande
  **`oxt`** qui code en dur l'upload vers `mathcursor-releases/MathCursor.oxt` (nom stable, pas
  d'interpolation de version) — impossible de ré-introduire un nom versionné à la main.
  `tools/cloudflare/README.md` documente le flux + la contrainte de nom (section « Publier
  l'extension LibreOffice (.oxt) »).
- **R2** : ré-upload `MathCursor.oxt` depuis `libreoffice-ext/MathCursor.oxt`, suppression de
  `MathCursor-0.1.0.oxt`. Aucun nom versionné d'oxt sur le bucket ; un nom stable = pas de
  cleanup (la release suivante écrase l'objet).
- **Skill `/deploy-prod`** : inchangé — il pilote la release **Word** (`.exe` + modèle NER),
  cadence distincte des alphas (cf. ADR distribution). L'oxt se publie via `deploy.sh oxt`.
- **Tests** : aucun test auto (Function + objet R2). Validation = `curl` GET Range + install réelle.
- **API publique** : `/download/latest.oxt` inchangé en surface ; sert désormais le bon nom.
- **Doc** : `install.html` promettait déjà `MathCursor.oxt` — désormais exact.

## Validation post-fix

1. `curl -I https://mathcursor.com/download/latest.oxt` → `200` + `Content-Disposition:
   attachment; filename="MathCursor.oxt"`.
2. Installer le fichier téléchargé dans LibreOffice, redémarrer complètement (y compris le
   démarrage rapide), cliquer `Outils ▸ … ▸ MathCursor : convertir la sélection` → plus de
   `KeyError`, conversion exécutée.
