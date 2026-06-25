# Feat — Modèle de publication marketplace VS Code : UNE extension, N VSIX plateforme

**Date :** 2026-06-25
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Lié à :** [2026-06-24-Feat-vscode-vsix-packaging.md](2026-06-24-Feat-vscode-vsix-packaging.md) (build du VSIX win32-x64 + cadrage cross-OS), [2026-06-24-Feat-rust-unified-toolkit.md](2026-06-24-Feat-rust-unified-toolkit.md) (Phase 4)

## Citation acté

> « sur la partie vs code peux tu me confirmer qu'on peut bien creer les version
> vsix pour windows / linux / macos ? […] il faut les empaqueter dans un meme
> vsix ou publier 3 extensions differentes sur vscode extensions ? » puis, après
> exposé des options : « oui on formalise ca » + choix « Nouvel ADR dédié
> (forte) » — utilisateur, 2026-06-25

## Décision

L'extension VS Code est distribuée comme **UNE seule extension** — `publisher`,
`name` et `version` **uniques** (`mathcursor.mathcursor`) — déclinée en
**plusieurs VSIX *platform-specific*** produits par
`@vscode/vsce package --target <target>` :

- `win32-x64` (livré), puis `linux-x64`, `darwin-x64`, `darwin-arm64`
  (+ `win32-arm64` / `linux-arm64` au besoin).

Le Marketplace **et** VS Code sélectionnent et installent **automatiquement** le
VSIX correspondant à l'OS+arch de l'utilisateur. C'est le mécanisme natif VS Code
conçu pour les extensions à **binaires natifs** (notre cas : `analyze`,
`mc-ner`, `mc-popup` + onnxruntime).

**Règle dure : on ne publie JAMAIS 3 extensions distinctes** (une par OS).

Chaque VSIX n'embarque **que le binaire de sa cible** (les trois exes/ELF/Mach-O
de l'OS visé), pas les trois jeux d'OS. Le **modèle NER** (`model_quantized.onnx`
+ `tokenizer.json`, commun à toutes les plateformes) est, lui, présent dans
chaque VSIX.

## Pourquoi

- **L'identité d'une extension publiée est un contrat public** (`publisher.name`
  apparaît dans l'URL marketplace, l'historique d'installation, les reviews, les
  liens partagés). Revenir dessus après publication coûte très cher (URLs mortes,
  utilisateurs sur la « mauvaise » extension, reviews fragmentées) → **forte**.
- Le modèle 1-extension/N-VSIX est **exactement** ce que VS Code prévoit pour les
  binaires natifs : l'utilisateur voit **une** extension, installe sans se poser
  de question, et reçoit le bon binaire pour sa machine.
- Cohérent avec la promesse produit **« 100 % local, sans compte »** (cf. mémoire
  `project-mathcursor-positioning`) : aucun téléchargement réseau de binaire au
  premier lancement.
- S'appuie sur la mécanique **déjà actée** côté build (ADR du 24-06, §3 « un VSIX
  `--target` par plateforme ») — cet ADR ne fait que **verrouiller le modèle de
  publication** correspondant, qui n'était pas écrit comme décision en titre.

### Alternatives écartées

- **3 extensions distinctes** (`mathcursor-win`, `mathcursor-mac`, `mathcursor-linux`) :
  fragmente l'identité, force l'utilisateur à choisir son OS à la main, fait
  diverger les versions/changelogs/reviews, et **ne se refusionne pas** après
  coup. Rejeté.
- **VSIX unique « fat »** embarquant les binaires des 3 OS : VS Code ne sait pas
  choisir le bon binaire à l'installation (il faudrait un *dispatch* maison au
  runtime), gonfle inutilement le poids (chaque utilisateur télécharge les exes
  des 2 OS qu'il n'utilise pas), et se heurte aux limites de taille marketplace.
  Rejeté.
- **VSIX *universal* sans binaire + téléchargement au 1ᵉʳ lancement** : ajoute une
  dépendance réseau au premier run (contraire à « sans compte / local »),
  complexifie, et fait télécharger un exe non signé que Defender/SmartScreen
  inspectera. Rejeté.

## Conséquences

- `package.json` conserve **un seul** `name`/`publisher`/`version`. Pas de
  variante par OS dans les métadonnées.
- La **CI matrice** (GitHub Actions `windows`/`macos`/`ubuntu`, cf. ADR du 24-06)
  produit N `.vsix` ; `vsce publish --target <…>` les pousse **sous la même
  extension**.
- `build.mjs` / `.vscodeignore` sélectionnent le binaire **de la cible** et
  n'embarquent pas les trois jeux d'OS.
- Le modèle ONNX commun (~44 Mo) reste dupliqué dans chaque VSIX — **assumé**
  (c'est le coût du « tout-local »).

## Séquencement du portage (provisoire — palier par palier)

Indépendant du modèle de publication (qui, lui, est *forte*), l'**ordre** de
sortie des plateformes est *provisoire* et suit la difficulté technique réelle
(cf. ADR du 24-06, §3) :

1. **Windows x64** — livré (les 3 binaires + mode actif caret).
2. **`mc-engine` + `mc-ner` mac/linux** — quasi gratuits : Rust pur, `ort`
   récupère l'onnxruntime de l'OS. + **`mc-popup` en mode passif** (webview
   WKWebView/WebKitGTK OK ; pas de positionnement caret natif).
3. **`mc-popup` mode actif mac/linux** — point dur : le suivi caret + hook
   clavier est MSAA/Windows-only ; équivalents AX API (macOS) / AT-SPI/X11
   (Linux) = chantier à part.

À chaque palier l'extension reste **publiable** : `engine.ts`/`ner.ts` lèvent
déjà l'indisponibilité proprement hors win32 (repli SpanComputer / erreur, pas de
crash). Un OS « pas encore prêt » signifie simplement qu'on ne publie pas encore
son `--target`, pas qu'on casse les autres.

## Exécution palier 2 (2026-06-25)

Mise en œuvre du palier 2 (engine + ner cross-OS) :

- **Code rendu portable** : `build.mjs` nomme les binaires selon l'OS
  (`EXE = win32 ? '.exe' : ''`) ; `engine.ts` / `ner.ts` remplacent la garde
  `process.platform !== 'win32'` par une garde « binaire (+ modèle) présent »
  → indispo propre si la cible n'est pas buildée. `popup.ts` reste
  **Windows-only** (mode actif caret = palier 3).
- **NER câblé hors-Windows** : le `MathCursorCompletionProvider` (repli mac/linux)
  utilise désormais le NER (primaire) → SpanComputer en repli, comme le chemin
  popup. Sans ça les ~46 Mo de NER seraient embarqués mais **inutilisés** sur
  mac/linux.
- **CI** `.github/workflows/vscode-vsix.yml` : matrice **dynamique** (push →
  Linux+Windows ; `workflow_dispatch` → +macOS, coût ×10), cibles **natives**
  `win32-x64` / `linux-x64` / `darwin-arm64`, packaging `vsce package --target`.
  Aligné sur `rust-ci.yml`.
- **Modèle NER en CI** : le modèle (~46 Mo, **hors git** — `.gitignore models/`)
  est tiré de R2. Bucket **dédié `mathcursor-models`** (séparé de
  `mathcursor-releases` pour ne **pas** tomber sous le cleanup de
  `/deploy-prod`), **public-lecture** via r2.dev (modèle non sensible, choix
  utilisateur). URL publique en **défaut** dans le workflow (surchargeable par le
  secret `NER_MODEL_BASE_URL`) → CI autonome, zéro secret à configurer.
  - **Couplage opérationnel nouveau** : un réentraînement NER (`/update-ner-model`
    → `models/latest/`) impose de **ré-uploader** `model_quantized.onnx` +
    `tokenizer.json` sur `mathcursor-models`, sinon la CI garde l'ancien modèle.
- **Vérifié sur Windows** : `tsc --noEmit` vert, `build.mjs` vert, VSIX
  `win32-x64` **33,26 Mo** au bon contenu (3 binaires + modèle + KaTeX).
  mac/linux = validés **en CI** (non buildables localement).
- Cleanup au passage : `.vscodeignore` exclut `sign.ps1` (fuyait dans le VSIX).

## Hors périmètre

Compte éditeur marketplace, signature des binaires (cadrées dans l'ADR du 24-06),
implémentation du mode actif caret mac/linux, LibreOffice.

## Validé par l'utilisateur

Question initiale (cf. citation) + choix explicite de la forme « Nouvel ADR dédié
(forte) » parmi les options proposées, 2026-06-25.

Exécution palier 2 validée le 2026-06-25 : « ok on y va », puis choix « Code +
workflow CI » / « Natif par runner (3) » / « Téléchargement R2/URL » / « URL
publique r2.dev » parmi les options proposées, et « oui » pour consigner
l'exécution dans l'ADR et committer.
