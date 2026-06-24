# Feat — Host VSCode (réalité livrée) : popup WPF persistante au caret + détection NER + auto-packages

**Date :** 2026-06-24
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** [2026-06-23-Feat-vscode-host.md](2026-06-23-Feat-vscode-host.md) (passé `retracté`)
**Lié à :** [2026-06-23-Refactor-delete-dead-host-contract.md](2026-06-23-Refactor-delete-dead-host-contract.md) (moteur pur portable), [2026-06-19-Feat-web-demo-real-mode-editor.md](2026-06-19-Feat-web-demo-real-mode-editor.md) (port JS SpanComputer), [2026-06-10-Feat-ner-auto-detection-debounce.md](2026-06-10-Feat-ner-auto-detection-debounce.md) (pipeline NER Word réutilisé)

## Citation acté

> « j'aimerai rajouter un plugin math cursor […] avec une popup du coup comme sur
> word mais dans vs code » ; puis « inspire toi de ce qu'on a fait sur Word, qui
> est très fluide » ; « vas y branche le vrai moteur et les vrais visu » ; « on
> peut faire le NER du coup plutôt que la détection naive ? » — utilisateur,
> 2026-06-23/24 (itératif, chaque pivot validé en direct)

## Contexte

L'ADR 2026-06-23 cadrait le host VSCode avec, pour l'UI, la **complétion native**
VSCode (CompletionItemProvider + aperçu SVG). À l'usage, l'utilisateur a voulu la
**popup au caret « comme Word »** (rendu des formules dans la liste, fluidité).
Or : (a) les lignes de la complétion native sont **texte seul** (pas d'image dans
les rangs) ; (b) VSCode **n'a aucune API de webview flottante au caret** (issue
microsoft/vscode#175234, non implémentée). La seule façon d'avoir un widget HTML/
formules rendu **au pixel du caret** est un **helper natif** (la popup Word le fait
déjà en WPF + MSAA). D'où la bascule, validée par spikes.

## Décision (architecture livrée)

**Trois helpers .NET persistants** sous `adapter-vscode/`, pilotés par l'extension
TypeScript via `child_process` + protocole texte (lignes, champs TAB), **hors
`MathCursor.sln`** (buildés uniquement par `adapter-vscode/extension/build.mjs` →
gate/workloads épargnés) :

1. **`engine-wasm`** — `MathCursor.Engine` compilé en `browser-wasm` (`[JSExport]
   Bridge.Analyze`), `dotnet.js` chargé dans l'extension host (Node), lazy. Même
   moteur que Word/web (zéro divergence). *(inchangé vs ADR superséd.)*
2. **`caret-popup`** (net48 WPF) — **popup persistante au caret façon Word** :
   position via MSAA `OBJID_CARET`, `WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW` (ne vole
   jamais le focus, hors Alt+Tab), **hook clavier global `WH_KEYBOARD_LL`** (↑↓
   nav, Entrée/Tab commit, Échap dismiss ; modificateurs laissés passer), nav-mode
   opt-in, thème suivant VSCode, **rendu WpfMath réutilisé de Word**. Persistante :
   suit le caret et se rafraîchit sur frappe/clic, se ferme hors zone.
3. **`ner-helper`** (net48 x64) — **détection NER** : `MathNerDetector` (ONNX, modèle
   `models/latest` ~46 Mo bundlé) + `ZoneRefiner`, réutilisés **à l'identique** de
   Word. Chargé une fois (~1,1 s, gardé chaud), ~7 ms/inférence. **Primaire** ;
   `SpanComputer` (port JS) en **repli** si NER indispo (non-Windows / modèle absent).

**UI = popup WPF (helper `caret-popup`)** sur Windows ; la complétion native VSCode
est reléguée en **repli hors Windows**. Déclenchement : **auto au fil de la frappe**
(debounce 120 ms) **et** **Ctrl+Espace forcé** (override) avec **cycle d'expansion**
de zone (un mot à gauche par appui répété).

**Traitements amont/aval** (extension TS) :
- **Masquage** des zones math déjà présentes (`$…$`, `$$…$$`, `\(…\)`, `\[…\]`,
  multi-lignes incluses, `\$` échappés exclus) avant détection → pas de reconversion.
- **Espace final** ajouté à l'analyse (signal moteur « signe postfixe » : `R*` →
  `R^{\ast}`, `lim x 0+` → `0⁺`), comme la détection live de Word.
- **Délimiteurs auto** : formule seule sur sa ligne → display (`\[…\]` LaTeX /
  `$$…$$` Markdown), sinon inline `$…$` (réglable). **Displaystyle inline malin** :
  `\displaystyle` seulement si la formule en a besoin (fraction, grand opérateur,
  lim, binom). **Auto-packages** : `\usepackage{amsmath}/{amssymb}` ajoutés au
  préambule LaTeX si la formule insérée les requiert.

**Réglages** : `culture`, `delimiters` (auto/inline/display/paren/none),
`maxCandidates`, `autoDetect`, `autoPackages`, `inlineDisplaystyle` (auto/always/never).

**Robustesse** (audit 2026-06-24) : UTF-8 forcé sur les pipes des helpers ;
revalidation de la zone avant insertion (range non périmé) ; NER timeout +
redémarrage auto (sans tuer le warmup, sans `dead` sur restart volontaire) ;
hook clavier désinstallé sur QUIT/EOF ; garde multi-curseur / sélection multi-ligne ;
réessai du moteur WASM si 1er chargement échoue.

## Tradeoff & alternatives écartées

- **Complétion native (ADR superséd.)** : rangs texte seul + pas d'images inline →
  ne rend pas la popup « comme Word ». Conservée en repli hors Windows.
- **Webview flottante au caret** : **impossible** (pas d'API VSCode ; pas d'accès
  pixel caret). Écartée d'office.
- **Inset webview (`editorInsets`)** : API proposée, réserve des lignes, non
  publiable marketplace → écartée au profit du helper natif (vrai flottant pixel).
- **Port TS du NER / réimplémentation tokenizer** : 2ᵉ implémentation à maintenir →
  on réutilise `MathNerDetector` C# via helper (parité Word).
- **Détection heuristique seule (SpanComputer)** : moins fine sur la prose ; gardée
  en repli. NER primaire (corpus Word).

## Conséquences

- **Code (nouveau)** : `adapter-vscode/{engine-wasm, caret-popup, ner-helper,
  extension}`. L'extension réutilise par **fichiers liés** (`<Compile Include=
  "..\..\adapter-vsto\...">`) : `WpfMathAdapter`, `MixedLatexRenderer` (rendu) et
  `MathNerDetector`, `WordPieceTokenizer`, `DetectedZone`, `ZoneRefiner` (NER).
  ⚠️ **Couplage** : un déplacement/renommage côté add-in Word casse le build VSCode
  **sans signal** (helpers hors sln/gate) → à terme, extraire en projet partagé.
- **Cœur** : `engine/` et `serialization/` **inchangés** (réutilisation pure).
- **Build** : workloads `wasm-tools`+`wasm-experimental` ; deps npm `mathjax-full`
  (repli complétion), `esbuild`. Modèle NER `models/latest` bundlé (`out/models`).
  `models/` et `out/` gitignorés (pas de gros binaire commité).
- **Licences VSIX** : MathJax (Apache-2.0), WpfMath (MIT), ONNX Runtime (MIT),
  **modèle NER** (interne) — à inventorier au packaging.
- **Spikes retirés** : `spike-extension/`, `spike-native/`, `ner-spike/`,
  `node-smoketest.mjs`, `spike-prerender.json`.

## Reste à faire (hors périmètre de cet ADR)

- **Packaging VSIX** : `@vscode/vsce` + `.vscodeignore` + `LICENSE` dans `extension/`,
  cible `--target win32-x64`, **signature Authenticode** des exes (le `caret-popup`
  porte un hook clavier `WH_KEYBOARD_LL` → flaggé Defender/SmartScreen sinon ;
  réutiliser le cert de l'installeur Word). **Taille** : `out/` ≈ **83 Mo** non
  compressé (modèle réduit `latest` 46 Mo + runtime WASM 23 Mo + ONNX natif 11 Mo
  + bundle 2,6 Mo) → VSIX zippé ≈ **55–70 Mo**. Acceptable (sideload + marketplace),
  pas besoin de téléchargement au 1er run pour l'instant.
- **Extraction projet partagé** des fichiers liés `adapter-vsto` (+ test de fumée
  build des helpers au gate pour détecter la casse tôt).
- **NER & matrices** : le modèle `latest` fragmente encore les matrices (corpus
  Word sans matrices) → retrain en cours côté utilisateur.
