# Feat — Packaging VSIX win32-x64 + cadrage signature & cross-OS (Phase 4)

**Date :** 2026-06-24
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Lié à :** [2026-06-24-Feat-rust-unified-toolkit.md](2026-06-24-Feat-rust-unified-toolkit.md) (Phase 4), [2026-06-24-Feat-rust-engine-port.md](2026-06-24-Feat-rust-engine-port.md), [2026-06-24-Feat-rust-ner-port.md](2026-06-24-Feat-rust-ner-port.md)

## Citation acté

> (Phase 4 « quoi maintenant ? ») « Packaging VSIX win32-x64, Pousser les 3
> commits, Signature exes (cadrage), Cross-OS mac/linux (cadrage) » — utilisateur,
> 2026-06-24

## Contexte

Phases 1-3 livrées : VSCode tourne 100 % en binaires Rust (moteur `analyze`, NER
`mc-ner`, popup `mc-popup`), **0 .NET**. Il faut maintenant un **livrable
installable** (VSIX) et cadrer ce qui dépend de l'utilisateur (signature) ou
d'autres OS (cross-OS).

## Décision

### 1. Packaging VSIX `win32-x64` (fait)

- `@vscode/vsce package --target win32-x64` (VSIX **plateforme** : il embarque des
  binaires win-x64).
- **`.vscodeignore`** : garde `out/**` (bundle + 3 exes + modèle + KaTeX),
  `package.json`, `LICENSE`, `README.md` ; exclut `src/`, `popup/` (sources),
  `node_modules/`, `build.mjs`, `tsconfig`, `*.map`, `demo.*`.
- **`LICENSE`** (GPL-3) copié depuis la racine par `build.mjs` (pas de doublon en
  git ; `.gitignore` ignore `extension/LICENSE`). `package.json` gagne `repository`.
- **`README.md`** = page marketplace minimale.
- Résultat : `mathcursor-win32-x64-0.1.0.vsix` — **33 Mo**, 34 fichiers
  (`extension.js` 31 Ko + `analyze.exe` 430 Ko + `mc-ner.exe` 20,7 Mo +
  `mc-popup.exe` 699 Ko + `model_quantized.onnx` 44 Mo + KaTeX). Aucun .NET,
  aucun src/node_modules. Le `.vsix` est un artefact (gitignoré).

### 2. Signature (cadrage — script fourni, certificat = utilisateur)

Le hook clavier global (`mc-popup`) + des exes non signés déclenchent
Defender/SmartScreen. **Process** : signer les 3 binaires AVANT `vsce package`.
Script **`adapter-vscode/extension/sign.ps1`** (signtool, SHA256 + timestamp ;
`-Thumbprint <cert installé>` ou `-PfxPath/-PfxPassword`). **Bloquant** : il faut
TON certificat code-signing (non automatisable ici). Étape manuelle :
`build.mjs` → `sign.ps1` → `vsce package`.

### 3. Cross-OS mac/linux (cadrage — non buildable d'ici)

Les binaires sont **win-x64** ; mac/linux exigent un build **par OS** (pas de
cross-compile fiable d'ici, surtout `mc-popup`). Plan CI (matrice GitHub Actions
`windows/macos/ubuntu`) :
- **`mc-engine` / `mc-ner`** : portables (Rust pur + `ort` qui télécharge
  l'onnxruntime de l'OS). `mc-ner` : vérifier que le natif onnxruntime est bien
  statique/embarqué sur mac/linux comme sur Windows.
- **`mc-popup`** : `wry`/`tao` → backend webview **par OS** (Windows WebView2,
  macOS WKWebView, Linux WebKitGTK) ; **mode actif** (MSAA caret + hook clavier)
  est **Windows-only** → mac/linux nécessitent un mode actif équivalent
  (AX API macOS, AT-SPI/X11 Linux) — chantier à part, ou rester passif.
- `build.mjs` : sélectionner le triplet selon `process.platform` ; `engine.ts` /
  `ner.ts` lèvent déjà l'indispo proprement hors win32 (repli SpanComputer /
  erreur) → pas de crash en attendant.
- Packaging : un VSIX `--target` par plateforme (`darwin-x64`, `darwin-arm64`,
  `linux-x64`).

## Conséquences

- Extension **installable** sur Windows (`code --install-extension *.vsix`).
- Signature + cross-OS restent **ouverts** (dépendent du cert / de runners CI).
- Word et LibreOffice inchangés.

## Hors périmètre

Publication marketplace (compte éditeur), LibreOffice Python→Rust, retrain modèle,
mode actif caret mac/linux.

## Vérification

- `node build.mjs` puis `npx @vscode/vsce package --target win32-x64` → VSIX 33 Mo,
  contenu attendu (3 exes + modèle, pas de src/node_modules/map).
- `code --install-extension mathcursor-win32-x64-0.1.0.vsix` puis test au caret.
- `sign.ps1` : exécutable avec un cert de test (signtool présent dans le SDK Windows).
