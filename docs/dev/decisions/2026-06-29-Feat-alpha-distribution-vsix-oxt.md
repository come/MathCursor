# Feat — Distribution publique des alphas (VS Code .vsix, LibreOffice .oxt) via R2 + releases.html

**Date :** 2026-06-29
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-26-UX-home-capter-intention](2026-06-26-UX-home-capter-intention.md) (ligne plateformes / FAQ « autres éditeurs ») ; [2026-04-24-Feat-cloudflare-deployment](2026-04-24-Feat-cloudflare-deployment.md) (R2 + Pages Functions)

## Citation acté

> Distribution : « Téléchargement public » — Structure : « Matrice dans releases.html »
> — utilisateur, 2026-06-29 (réponses aux deux questions de cadrage)

## Contexte

Le site annonce déjà trois éditeurs (Word stable, LibreOffice alpha, VS Code alpha) dans le
hero, la note « Autres éditeurs » et la FAQ, mais **seul Word est téléchargeable**
(`/download/latest.exe` via R2 + Pages Function). Les deux alphas existent en artefacts
(`mathcursor-win32-x64-0.1.0.vsix` 34,8 Mo ; `MathCursor.oxt` 8,1 Mo) mais ne sont ni hébergés
ni documentés ; le site renvoyait vers « Écrivez-moi ».

## Décision

Rendre les deux alphas **téléchargeables publiquement** et documenter leur installation, le tout
dans `releases.html` (hub multi-éditeur).

- **Infra** : héberger `.vsix`/`.oxt` sur le bucket R2 existant `mathcursor-releases` ; étendre la
  route `docs/functions/download/[[filename]].js` (alias `latest.vsix`/`latest.oxt` + regex de
  validation `\.(exe|vsix|oxt)$`) ; ajouter `LATEST_VSCODE_VSIX` et `LATEST_OXT` dans
  `docs/functions/_latest.js` **sans toucher** `LATEST_VERSION` (qui alimente la pastille MAJ de
  l'add-in Word via `api/v1/version.js`).
- **`releases.html`** : une section d'install par éditeur (Word stable conservé ; nouvelles
  sections VS Code et LibreOffice avec bouton de téléchargement + pas-à-pas + prérequis + note
  alpha), FR markup + `I18N.en`.
- **`index.html`** : `inst_platforms` et `faq_3_a` pointent vers `releases.html` au lieu de
  « Écrivez-moi » (contact gardé en repli).

**Honnêteté assumée dans la copy** : les deux alphas sont **Windows 64 bits aujourd'hui** ;
VS Code = binaires **non signés** (Defender peut demander confirmation), pas sur la Marketplace ;
LibreOffice = **redémarrage requis**, **raccourci à lier à la main**, **auto-détection off**
sans le modèle NER (mode manuel sélection + Ctrl+Espace), Writer seul, StarMath best-effort.

## Tradeoff & alternatives écartées

- **Alpha sur demande (gated, « Écrivez-moi »)** : écarté par l'utilisateur au profit du public —
  moins de friction pour les testeurs. Coût accepté : exposition d'alphas non signés / Windows-only
  à tout visiteur (atténué par les notes « alpha » explicites).
- **Pages d'install dédiées (`install/vscode.html`, `install/libreoffice.html`)** : écarté au
  profit de tout regrouper dans `releases.html`.
- **Repurposer `LATEST_VERSION`** : exclu — elle pilote la pastille MAJ Word ; constantes séparées.

## Conséquences

- **Code touché** : `docs/functions/_latest.js`, `docs/functions/download/[[filename]].js`,
  `docs/releases.html`, `docs/index.html`.
- **R2** : 2 objets ajoutés à `mathcursor-releases` (`mathcursor-win32-x64-0.1.0.vsix`,
  `MathCursor-0.1.0.oxt`). Hors `/deploy-prod` cleanup (qui ne supprime que les `MathCursor-Setup-*.exe`).
- **Tests** : aucun test auto (site statique + Function). Validation = rendu navigateur, `curl -I`
  sur les routes, install réelle Windows.
- **API publique** : route `/download` élargie (`.vsix`/`.oxt` en plus de `.exe`), rétro-compatible.

## Validation post-fix

`curl -I https://mathcursor.com/download/latest.vsix` et `…/latest.oxt` → 200 + `Content-Disposition`
au bon nom ; `releases.html` affiche les 3 sections FR/EN sans retombée FR en mode EN ; install
réelle du `.vsix` (VS Code) et du `.oxt` (LibreOffice) en suivant la doc.

## Hors périmètre

Signature des binaires VS Code (ADR 2026-06-24) ; builds mac/Linux (CI par OS) ; embarquer le
modèle NER dans le `.oxt` (active l'auto-détection LibreOffice) ; versionnage automatisé des
alphas dans `/deploy-prod`.
