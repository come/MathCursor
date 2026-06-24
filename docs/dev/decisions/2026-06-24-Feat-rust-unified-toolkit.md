# Feat — Toolkit Rust unifié (coquille popup + moteur + NER) partagé LibreOffice/VSCode

**Date :** 2026-06-24
**Kind :** Feat
**Température :** forte
**Statut :** acté (roadmap phasée — exécution incrémentale)
**Lié à :** [2026-06-24-Feat-vscode-host-popup-ner.md](2026-06-24-Feat-vscode-host-popup-ner.md) (host VSCode .NET actuel, que ce toolkit allège), [2026-06-24-Feat-libreoffice-popup-webview-shell-rust.md](2026-06-24-Feat-libreoffice-popup-webview-shell-rust.md) (coquille Rust réutilisée), [2026-06-16-Feat-portable-engine-universal-vocab.md](2026-06-16-Feat-portable-engine-universal-vocab.md) (data JSON partagée)

## Citation acté

> « pourquoi forcément wpf pour vscode […] on peut pas spawn un petit exe ? » ;
> « maybe on peut faire le portage maintenant du moteur en rust (on l'a déjà fait
> en python), il se basera sur les jsons, et on l'embed avec le NER dans la popup ?
> c'est le plus rapide + léger à l'exécution non ? et on pourra réutiliser pour
> libreoffice derrière d'ailleurs non ? » — utilisateur, 2026-06-24 (a choisi
> « Cadrer le toolkit Rust complet (ADR) »)

## Contexte

Les hosts non-Word empilent des stacks lourdes/hétérogènes :
- **VSCode** : moteur en **WASM** (runtime .NET ≈ 23 Mo) + popup **WPF** (net48) +
  NER en helper .NET → 3 process .NET juste pour exécuter du C#.
- **LibreOffice** : moteur en **Python** (`engine-python/mc_engine`, shippé) + popup
  **coquille Rust** (wry+tao, KaTeX) déjà en production (`libreoffice-ext/shell`,
  binaire 505 Ko).

En questionnant (à raison) le runtime WASM puis le WPF, le constat : tout ça
exécute le **même moteur** et affiche la **même popup**, mais via 3 runtimes
différents (.NET WASM, .NET WPF, Python). Une coquille **Rust** native existe déjà
et marche. D'où la cible : un **cœur Rust unifié** que chaque host **spawn**.

## Décision

Créer un **workspace Rust `rust/`** = le « cœur » MathCursor des hosts non-Word :
- **`mc-popup`** — la coquille `libreoffice-ext/shell` migrée (wry+tao + KaTeX
  offline, IPC stdio JSON `show/update/close/quit` ↔ `ready/commit/dismiss`).
  Mode **passif** (coords + clavier fournis par le host = LibreOffice) **et** mode
  **actif Windows** (MSAA `OBJID_CARET` + hook `WH_KEYBOARD_LL`) pour les hosts sans
  API caret/clavier (VSCode) — reproduit ce que fait l'actuel `caret-popup` WPF.
- **`mc-engine`** — port **fidèle** du moteur (`engine/src/MathCursor.Engine`,
  2471 LoC : Lexer/Parser-CYK/Score/Segment/Vocab/Render), lit les **data JSON
  partagées** (`data/engine/*.json`, 22 Ko, embarquées via `build.rs`).
- **`mc-ner`** — `MathNerDetector` + `WordPieceTokenizer` + `ZoneRefiner` portés,
  inférence via crate **`ort`** (onnxruntime), modèle `models/latest`.
- **`mc-host`** — binaire assemblant popup + engine + NER, protocole stdio par host.

Chaque host spawn ce binaire : **LibreOffice** (UNO/Python) et **VSCode**
(extension TS) ; **Word reste inchangé** (moteur C# `ForestEngine.Analyze` en
process, `adapter-vsto/.../ConversionController.cs:215`).

**Roadmap phasée** (chaque phase livrable seule — cf. plan) :
1. **Popup** : VSCode spawn `mc-popup` (mode actif) → supprime `caret-popup` WPF.
2. **Moteur** : `mc-engine` (gate **456/456** `fixtures.json`) → supprime `engine-wasm`
   (−23 Mo, fin workloads wasm) côté VSCode ; LibreOffice quitte le runtime Python.
3. **NER** : `mc-ner` fusionné → supprime `ner-helper` (.NET).
4. **Consolidation/cross-OS** : VSCode = 0 .NET ; builds mac/linux ; packaging signé.

## Stratégie de parité (non négociable)

`engine/tests/.../fixtures.json` (**456 cas** : input → candidats LaTeX classés +
decision + note) = **source de vérité unique**, déjà rejouée par C# et Python
(`engine-python/conformance.py`). Le port Rust ajoute un miroir `conformance` et ne
passe en service qu'à **456/456**. Cible long terme : **C# (Word, canonique) + Rust
(autres hosts)** ; **Rust supersede le Python** comme port portable shippé (Python
secondaire/conformance, ou retiré une fois Rust vert) → pas de 3ᵉ impl pérenne.

## Tradeoff & alternatives écartées

- **Statu quo (.NET WASM + WPF + helper .NET)** : marche mais 23 Mo de runtime,
  2 toolchains, popup WPF (rendu WpfMath à adapters). Rust = natif léger + KaTeX.
- **Moteur dans un helper net48** (au lieu de WASM) : gain rapide (−23 Mo) **sans
  Rust**, mais reste .NET, ne sert pas LibreOffice, et n'unifie pas la popup. Écarté
  au profit de la cible unifiée (l'utilisateur vise le cœur natif partagé).
- **Garder WPF pour VSCode** : inutile — un host peut spawn n'importe quel exe ; la
  coquille Rust (déjà écrite) rend la même chose en KaTeX (100 % du vocab).
- **Rust = source unique, FFI Word** : trop coûteux/risqué maintenant (Word in-process)
  → Word reste C# ; FFI différé.
- **Port TS** : déjà écarté ailleurs (perf, divergence) ; Rust est natif + partageable.

## Conséquences

- **Coût** : Phase 1 ~2-4 j (gain immédiat) ; Phases 2-3 ~4-6 sem cumulées, dominé
  par le **Parser CYK** (forêt ambiguë) + la parité 456/456. Assumé pour un cœur
  natif unique.
- **Nouveau** : workspace `rust/` (`mc-engine`, `mc-ner`, `mc-popup`, `mc-host`) ; ADR.
- **Suppressions (par phase)** : `adapter-vscode/caret-popup` (Ph1),
  `adapter-vscode/engine-wasm` (Ph2), `adapter-vscode/ner-helper` (Ph3).
- **Allègement VSCode** : à terme 0 runtime .NET (plus de WASM/net48) ; VSIX réduit.
- **LibreOffice** : moteur Python → Rust (Ph2) ; coquille devient le `mc-core`.
- **Risques** : correctness/perf du Parser CYK en Rust ; natif MSAA+hook clavier en
  Rust (winapi/`windows`) ; popup cross-OS (Wayland/Retina, cf. `shell/README.md`) ;
  `ort` garde le natif onnxruntime (~10 Mo).
- **Data/doctrine** : `data/engine/*.json` + `fixtures.json` restent la source unique
  partagée C#/Python/Rust.

## Hors périmètre

Word (reste C# in-process) ; FFI Rust→Word ; retrain NER matrices (chantier user) ;
toute optimisation du moteur avant un port fidèle vert (456/456).
