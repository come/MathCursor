# Feat — VSIX VS Code multiplateforme : distribution via le site (R2), 3 cibles exposées

**Date :** 2026-06-29
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-29-Feat-alpha-distribution-vsix-oxt](2026-06-29-Feat-alpha-distribution-vsix-oxt.md) (distribution alpha mono-OS étendue ici au multiplateforme), [2026-06-25-Feat-vscode-marketplace-publishing-model](2026-06-25-Feat-vscode-marketplace-publishing-model.md) (UNE extension, N VSIX `--target`), [2026-06-24-Feat-vscode-vsix-packaging](2026-06-24-Feat-vscode-vsix-packaging.md), `docs/SIGNING.md`

## Citation acté

> « je veux préparer correctement le déploiement notamment du vsix multiplateforme »
> — utilisateur, 2026-06-29

Arbitrages (questions posées) :
- **Canal** : « non je veux un DL depuis notre site pour l'instant » (pas Marketplace,
  pas GitHub Releases) → R2 + `releases.html`, comme le `.exe` Word et les alphas.
- **Signature** : « Rester non signé (palier alpha) » → conforme à `SIGNING.md` (beta = pas de signature).
- **Acheminement CI → R2** : « Manuel via `deploy.sh` » (pas d'upload direct depuis la CI).
- **UX site** : « 3 boutons explicites (Win / Linux / Mac) » (pas de détection d'OS).

## Contexte

La CI `vscode-vsix.yml` produit déjà **3 VSIX `--target`** (`win32-x64`, `linux-x64`,
`darwin-arm64`) — mais seulement en **artifacts éphémères**. La distribution publique
(ADR 2026-06-29-alpha) n'expose qu'**un** VSIX : `_latest.js` a un `LATEST_VSCODE_VSIX`
scalaire (Windows), l'alias `latest.vsix` ne sert que Windows, et `releases.html` annonce
« Windows 64 bits pour l'instant ». Le multiplateforme est **construit mais pas distribué**.

Il manque trois maillons : (1) exposer les 3 cibles côté site (alias + map), (2) un chemin
pour amener les VSIX de la CI vers R2, (3) la doc du flux release de bout en bout.

## Décision

Distribuer les 3 VSIX depuis le site (bucket R2 `mathcursor-releases` + `/download/*`),
comme le `.exe` Word et le `.oxt`. Trois phases.

### 1. Exposer le multiplateforme (config site)

- **`_latest.js`** : `LATEST_VSCODE_VSIX` devient une **map cible → fichier versionné**
  (`win32-x64` / `linux-x64` / `darwin-arm64`). `LATEST_OXT` et `LATEST_VERSION` (pastille
  MAJ add-in Word) inchangés.
- **`download/[[filename]].js`** : alias par plateforme `latest-<target>.vsix` résolus depuis
  la map, aux **deux** points (GET + HEAD). `latest.vsix` reste un alias vers **win32-x64**
  (rétro-compat avec les liens existants). Regex `ALLOWED` inchangée (`.vsix` déjà autorisé).
- **`releases.html`** : section VS Code = **3 boutons explicites** (Windows / Linux / macOS
  Apple Silicon), avec caveats honnêtes par OS — popup au caret **Windows-only** au palier 2
  (sur Mac/Linux l'extension fonctionne sans la popup au caret), binaires **non signés**,
  Intel Mac / arm Linux / arm Windows à venir. FR markup + `I18N.en`.

### 2. Acheminer les VSIX CI → R2 (manuel)

- **`deploy.sh vsix <version> [dossier]`** : upload des 3 `.vsix` (téléchargés depuis les
  artifacts CI) vers R2 sous leur nom versionné — calqué sur la commande `installer`.
  Tolère l'absence d'une cible (Mac non buildé si pas de `workflow_dispatch`).
- Pas d'upload direct depuis la CI : aucun secret R2 en CI, l'humain valide ce qui devient public.

### 3. Doc du flux release

`tools/cloudflare/README.md` : section « Publier un VSIX multiplateforme » = bump
`package.json` → `workflow_dispatch` (3 cibles) → download artifacts → `deploy.sh vsix` →
bump la map `_latest.js` → 3 boutons `releases.html` → `deploy.sh site`.

## Tradeoff & alternatives écartées

- **CI pousse direct vers R2** : plus auto, mais ajoute un token R2 en secret GitHub et
  publie sans validation manuelle. Écarté (cohérence `deploy.sh` + moindre exposition).
- **Détection d'OS sur `latest.vsix` (User-Agent)** : fragile — un VSIX se télécharge souvent
  depuis un autre poste que la cible (élève qui prépare la clé d'un autre). Écarté au profit
  des 3 alias/boutons explicites.
- **Marketplace VS Code** : visibilité max mais compte éditeur + PAT + idéalement binaires
  signés (faux positif AV sur `mc-popup`). Écarté pour ce palier (« DL depuis notre site »).
- **Bouton principal auto-détecté** : plus joli mais masque les autres cibles ; l'alpha
  technique gagne à montrer les 3 d'emblée.

## Conséquences

- **Code touché** : `docs/functions/_latest.js` (map), `docs/functions/download/[[filename]].js`
  (alias par plateforme ×2), `docs/releases.html` (3 boutons + I18N), `tools/cloudflare/deploy.sh`
  (commande `vsix`), `tools/cloudflare/README.md` (+ `docs/SIGNING.md` rappel flux).
- **API publique** : aucun contrat binaire. Les liens `/download/latest.vsix` existants
  restent valides (→ win32-x64). Nouveaux liens `/download/latest-<target>.vsix`.
- **Tests** : pas de tests auto (config Pages + bash). Validation = `deploy.sh site` puis
  `curl -I` sur les 3 alias.
- **Règles MC impactées** : aucune.

## Validation post-fix

1. `curl -sI https://mathcursor.pages.dev/download/latest-linux-x64.vsix` → 200 (après upload).
2. `latest.vsix` → toujours le VSIX win32-x64 (rétro-compat).
3. `releases.html` : 3 boutons, caveats par OS, FR + EN.

## Hors périmètre

Signature des binaires (palier diffusion large, `SIGNING.md`), Marketplace, cibles
darwin-x64 / win32-arm64 / linux-arm64 (CI palier 2 : différées), notarisation macOS,
bundle du modèle NER dans le `.oxt`.
</content>
</invoke>
