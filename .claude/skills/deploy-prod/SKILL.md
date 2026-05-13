---
name: deploy-prod
description: Déploie une nouvelle version MathCursor en production — bump version, rebuild WASM, upload installer R2, ajout du changelog FR sur la page téléchargement, deploy Cloudflare Pages, puis cleanup R2 (garde les 2 dernières). Utilise quand l'utilisateur dit "deploy", "release", "mettre en prod", "publier la X.Y.Z".
user-invocable: true
allowed-tools:
  - Read
  - Edit
  - Write
  - Bash
  - AskUserQuestion
---

# /deploy-prod — Déploiement production MathCursor

Pipeline de release pour MathCursor. Orchestre :

1. Bump version (iss + download function)
2. Rebuild WASM démo + mirror vers `docs/demo/`
3. Vérification de l'installer (déjà buildé par l'utilisateur)
4. Upload R2 (nouvelle version)
5. Ajout carte changelog FR sur `docs/releases.html` + bump CTA
6. Deploy Cloudflare Pages
7. **Cleanup R2 à la fin** : garde les 2 versions les plus récentes, supprime le reste après confirmation

L'argument est la version cible : `/deploy-prod 0.5.4`. Si pas d'argument, lis `MathCursor.iss` et propose un bump patch via AskUserQuestion.

---

## Pré-requis (à vérifier avant de commencer)

- `~/.mathcursor/cloudflare.env` existe et contient un token valide. Si non → stop, demande à l'utilisateur de le configurer (cf. `tools/cloudflare/README.md`).
- L'installer est **déjà buildé** par l'utilisateur (la skill ne build PAS l'ISS). Le fichier attendu : `adapter-vsto/installer/output/MathCursor-Setup-<VERSION>.exe`. Si absent → stop, dis à l'utilisateur de lancer `powershell -ExecutionPolicy Bypass -File adapter-vsto/installer/build.ps1` puis `ISCC.exe adapter-vsto/installer/MathCursor.iss` d'abord.
- Working dir : `D:/Software/DocMath`. Tous les chemins ci-dessous sont relatifs à cette racine.

Si l'arbre git est dirty, **demande confirmation** avant de continuer (un bump de version va créer des modifs supplémentaires et tu ne veux pas mélanger).

---

## Étape 1 — Résolution de la version

- Lis la version courante dans `adapter-vsto/installer/MathCursor.iss` (ligne `#define MyAppVersion "X.Y.Z"`).
- Si l'argument `$ARGUMENTS` contient une version (regex `\d+\.\d+\.\d+`), utilise-la.
- Sinon, propose via AskUserQuestion un bump patch (par défaut) ou minor/major.
- Vérifie que la version cible est **strictement supérieure** à la version courante. Si non → stop avec message clair.
- Vérifie que `adapter-vsto/installer/output/MathCursor-Setup-<VERSION>.exe` existe. Si non → stop.

Stocke `OLD_VERSION` et `NEW_VERSION` pour la suite.

---

## Étape 2 — Bump version dans les fichiers

Edit deux fichiers :

**a) `adapter-vsto/installer/MathCursor.iss`** :
```
#define MyAppVersion "<OLD_VERSION>"  →  #define MyAppVersion "<NEW_VERSION>"
```

**b) `docs/functions/download/[[filename]].js`** :
```
const LATEST_VERSION = "<OLD_VERSION>";  →  const LATEST_VERSION = "<NEW_VERSION>";
```

---

## Étape 3 — Rebuild WASM démo

```bash
dotnet publish web-demo/MathCursor.Demo.WebAssembly/MathCursor.Demo.WebAssembly.csproj \
  -c Release \
  -o web-demo/publish/ \
  --nologo
```

Si le build échoue → stop, montre l'erreur. Sinon, mirror `web-demo/publish/wwwroot/` vers `docs/demo/` :

```bash
rm -rf docs/demo/*
cp -R web-demo/publish/wwwroot/. docs/demo/
```

(Le `cp -R .../.` copie le contenu, pas le dossier lui-même.)

---

## Étape 4 — Upload installer sur R2

```bash
tools/cloudflare/deploy.sh installer <NEW_VERSION>
```

Le script lit `~/.mathcursor/cloudflare.env`, source les credentials et upload via wrangler. Si exit code ≠ 0 → stop.

---

## Étape 5 — Changelog FR + bump CTA dans `docs/releases.html`

### 5.1 — Récupère le contenu du changelog auprès de l'utilisateur

Utilise AskUserQuestion : demande **les bullets FR** pour cette version (un par ligne, format libre). Demande aussi un **résumé court** (1 phrase, pour `dl_<version>_intro`) et une **date** (par défaut aujourd'hui en FR, ex. "30 avril 2026").

### 5.2 — Insère la nouvelle carte en haut de l'historique

Dans `docs/releases.html`, juste avant la première `<article class="card">` (qui suit `<h2 data-i18n="dl_history_h">`), insère :

```html
        <article class="card" style="margin-bottom: 20px;">
          <h3><NEW_VERSION> <span style="color: #888; font-weight: normal; font-size: 14px;" data-i18n="dl_<vNoDot>_date">— <DATE_FR></span></h3>
          <p data-i18n="dl_<vNoDot>_intro"><INTRO_FR></p>
          <ul>
            <li data-i18n="dl_<vNoDot>_1"><BULLET_1_FR></li>
            <li data-i18n="dl_<vNoDot>_2"><BULLET_2_FR></li>
            <!-- ... -->
          </ul>
          <a href="/download/MathCursor-Setup-<NEW_VERSION>.exe" class="button button--secondary" download data-i18n="dl_<vNoDot>_btn">Télécharger <NEW_VERSION></a>
        </article>
```

`<vNoDot>` = version sans points (ex. `054` pour `0.5.4`). C'est la convention déjà utilisée.

### 5.3 — Démote l'ancienne carte (ex-top)

Sur l'ancienne carte top (celle avec version `OLD_VERSION`), remplace le `<a class="button button--secondary" download>...</a>` final par :

```html
          <div class="retired-note" data-i18n="dl_retired">Version archivée — utilise la <NEW_VERSION> qui inclut ces changements.</div>
```

### 5.4 — Bump le CTA principal et les chaînes EN

Dans `docs/releases.html` :

- **FR (ligne ~53)** : `Télécharger la dernière version (<OLD_VERSION>)` → `Télécharger la dernière version (<NEW_VERSION>)`.
- **EN (dans le bloc `I18N.en`)** : `dl_cta_latest: "Download the latest version (<OLD_VERSION>)"` → idem avec `<NEW_VERSION>`.
- **`dl_retired` FR et EN** : remplacer toutes les occurrences de `<OLD_VERSION>` par `<NEW_VERSION>` dans la valeur de `dl_retired` (deux endroits potentiellement : le span FR statique et la traduction EN). Utilise `replace_all` si la chaîne est unique.

### 5.5 — Ajoute les clés EN par défaut (best-effort)

Dans `I18N.en`, ajoute des clés `dl_<vNoDot>_*` avec une **traduction littérale** des bullets FR. Si tu ne sais pas traduire fidèlement (jargon métier), copie les valeurs FR telles quelles (mieux qu'une page cassée). L'utilisateur pourra peaufiner après.

---

## Étape 6 — Deploy site Cloudflare Pages

```bash
tools/cloudflare/deploy.sh site
```

Si exit code ≠ 0 → stop, montre l'erreur. À ce stade le site est en ligne avec la nouvelle version, l'installer est dispo via R2.

---

## Étape 7 — Cleanup R2 (à la FIN)

C'est l'étape qui demande **le plus de prudence** : on supprime des artefacts en prod.

### 7.1 — Liste les objets

```bash
source ~/.mathcursor/cloudflare.env
npx --yes wrangler@latest r2 object list mathcursor-releases --remote
```

### 7.2 — Filtre + tri

Garde uniquement les fichiers qui matchent `MathCursor-Setup-X.Y.Z.exe`. Trie par version sémantique décroissante. **Les 2 premiers** (= les 2 plus récents, dont la nouvelle qui vient d'être uploadée) sont conservés. Le reste est candidat à la suppression.

Edge case : si la liste a ≤ 2 entrées, rien à supprimer → skip avec message info.

### 7.3 — Confirmation

Affiche la liste des fichiers à supprimer (un par ligne, avec leur version). Demande confirmation explicite via AskUserQuestion :

> Supprimer ces N anciennes versions du bucket R2 ? (les 2 plus récentes sont conservées)

Si l'utilisateur dit non → skip cette étape, retourne le rapport final sans cleanup.

### 7.4 — Suppression

Pour chaque fichier confirmé :
```bash
npx --yes wrangler@latest r2 object delete "mathcursor-releases/<filename>" --remote
```

Loope séquentiellement (pas en parallèle, pour avoir des erreurs lisibles). Si une suppression échoue → log l'erreur, continue avec les autres, mention dans le rapport final.

---

## Rapport final

À la fin (succès ou abandon partiel), affiche un résumé court :

```
✓ Version : <OLD_VERSION> → <NEW_VERSION>
✓ WASM rebuild : OK
✓ Installer R2 : MathCursor-Setup-<NEW_VERSION>.exe uploadé
✓ Changelog : carte FR ajoutée, CTA bumpé
✓ Site Pages : déployé sur mathcursor.pages.dev
✓ Cleanup R2 : N fichier(s) supprimé(s), 2 conservés
```

Marque `✗` ce qui a échoué ou été skippé. **Ne fais pas de commit git automatique** — la mémoire utilisateur dit explicitement de ne JAMAIS commit sans demande explicite. Indique seulement à l'utilisateur les fichiers modifiés (`MathCursor.iss`, `[[filename]].js`, `releases.html`) qu'il devra commit lui-même.

---

## Garde-fous récap

- **Ne jamais** continuer après une erreur build/upload sans demander.
- **Ne jamais** supprimer R2 en premier — toujours en dernière étape, après confirmation.
- **Ne jamais** committer automatiquement.
- Si `~/.mathcursor/cloudflare.env` est absent → message clair, pointe vers `tools/cloudflare/README.md`.
- Si la nouvelle version ≤ ancienne → stop net, pas de downgrade silencieux.

Arguments passés : `$ARGUMENTS`
