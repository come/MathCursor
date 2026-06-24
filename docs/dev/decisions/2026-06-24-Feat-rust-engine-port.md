# Feat — Moteur forest porté en Rust (`mc-engine`) + intégration VSCode (−WASM)

**Date :** 2026-06-24
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Lié à :** [2026-06-24-Feat-rust-unified-toolkit.md](2026-06-24-Feat-rust-unified-toolkit.md) (exécute sa **Phase 2**), [2026-06-24-Feat-vscode-host-popup-ner.md](2026-06-24-Feat-vscode-host-popup-ner.md) (host VSCode dont on retire le moteur WASM), [2026-06-16-Feat-portable-engine-universal-vocab.md](2026-06-16-Feat-portable-engine-universal-vocab.md) (data JSON partagée, port Python = patron)

## Citation acté

> « super on envoie la phase 2 » ; « ok on phase termine » — utilisateur, 2026-06-24

## Contexte

Phase 1 du toolkit Rust livrée (popup `mc-popup`). Phase 2 = remplacer le **moteur
WASM .NET** (≈ 23 Mo de runtime + `dotnet.js` chargé dans le host Node) côté VSCode
par un **moteur natif Rust**, en suivant le patron du port Python
(`engine-python/mc_engine`, transcription 1:1 par module). Word reste inchangé
(moteur C# en process).

## Décision

Créer **`rust/mc-engine`** (membre du workspace `rust/`), port **fidèle** du moteur
forest (`engine/src/MathCursor.Engine` : `Lexer`, `Parser`/Forest CYK, `Score`,
`Segment`, `LatexRenderer`, `Vocabulary`, `EngineCulture`) + data JSON **embarquées**
(`data/engine/*.json` via `include_str!`). Registre global (`OnceLock`) bâti une fois.

- **Gate de parité : 456/456 `fixtures.json`** (source de vérité C#). Binaire
  `conformance` (miroir de `conformance.py`).
- **Intégration VSCode** : binaire **`analyze`** = service **stdio persistant**
  (protocole `<culture>\t<src>` → 1 ligne JSON `{decision,ranked:[{latex,cost}],hasNote}`,
  parité `ner-helper`). `engine.ts` le pilote (même interface `analyze(src,culture)`
  exportée → `extension.ts` inchangé), avec timeout + re-spawn auto. Un caractère
  inattendu (entrée libre) fait paniquer le lexer comme le C# lève une exception :
  **rattrapé** (`catch_unwind`) → `"erreur"`, le process survit (d'où
  `panic = "unwind"` sur le profil release du workspace).
- **Suppression** d'`adapter-vscode/engine-wasm` (projet WASM mort) + des étapes
  WASM de `build.mjs`. Bundle `out/engine` : **~23 Mo → 440 Ko**.

## Parité C# au-delà du port Python

Le port Python est à **441/456** (il précède 3 ADRs). Pour atteindre 456 (= C#
canonique), 3 comportements C# ont été ajoutés au port Rust en lisant la source C# :

1. **`PairSkeletons` cas « complet »** (ADR 2026-06-22) : quand le meilleur parse
   est une forme COURTE complète, on propose AUSSI le squelette frère PLUS LONG
   (forme courte en tête). Le Python n'avait que le cas incomplet (ADR 2026-06-12).
   → +11 fixtures.
2. **`AddPrefixAliases`** (préfixes non ambigus ≥ 4 : `unio`→union, `appro`→approx)
   + branche `v.Cut` du lexer (relation-mot sans opérande gauche reste infixe). → +2.
3. **flag `SignSup`** (ADR 2026-06-22) : un sup issu d'un postSign (`0⁺`) ne peut pas
   s'orienter en chapeau orphelin quand la découpe n-aire coupe sa base
   (`lim x 0+` → `\lim_{x\to 0^{+}}\square`, pas `\lim_{x\to 0}\hat{+}`). → +2.

Le port **Python** garde sa dette (441/456) — non bloquant ; la tri-parité pourra
être recomblée plus tard. Source de vérité : **C# (Word) + Rust (autres hosts)**.

## Conséquences

- VSCode : **0 runtime moteur .NET** (reste `ner-helper` net48 — Phase 3). Démarrage
  du service moteur instantané (pas de modèle), ~µs/analyse.
- **Hors Windows** : `analyze` renvoie `erreur` (binaire win-x64 seulement ; builds
  mac/linux = Phase 4). Le repli complétion native non-Windows perd donc le moteur
  jusqu'à la Phase 4 — assumé (produit Windows-first).
- LibreOffice (bascule Python→Rust) : **pas dans ce lot** (même Phase 2, autre host).

## Écartés

- **napi/WASM Rust** pour appeler le moteur depuis Node : le spawn stdio est déjà le
  pattern des deux autres helpers (popup, NER) → cohérence, zéro toolchain en plus.
- **Refactor du moteur** : port fidèle d'abord (parité), optimisation après mesure.
- **Rendre le lexer faillible (`Result`)** partout : `catch_unwind` au bord du
  service suffit et reste fidèle au C# (exception → host gère).

## Vérification

- `cargo run -p mc-engine --bin conformance` → **456/456**.
- `analyze.exe` stdio testé (`vecAB.vecBC`, `1/x+1`, `x dans R` us→erreur,
  `&bad`→erreur+survie, `lim x 0+`→popup).
- `node build.mjs` : `out/engine/analyze.exe` (440 Ko), bundle 31 Ko, plus de
  `_framework` WASM ; workspace release OK (mc-popup + mc-engine).
