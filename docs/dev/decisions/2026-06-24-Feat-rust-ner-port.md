# Feat — NER porté en Rust (`mc-ner`) → VSCode 0 .NET + corrige le tokenizer

**Date :** 2026-06-24
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Lié à :** [2026-06-24-Feat-rust-unified-toolkit.md](2026-06-24-Feat-rust-unified-toolkit.md) (exécute sa **Phase 3**), [2026-06-24-Feat-rust-engine-port.md](2026-06-24-Feat-rust-engine-port.md) (Phase 2), [2026-06-24-Feat-vscode-host-popup-ner.md](2026-06-24-Feat-vscode-host-popup-ner.md) (host VSCode dont on retire le dernier helper .NET)

## Citation acté

> « Phase 3 — NER en Rust » (choix) ; « ok on continue » — utilisateur, 2026-06-24

## Contexte

Phases 1 (popup) et 2 (moteur) livrées. Restait **un seul process .NET** côté
VSCode : `ner-helper` (net48 + `Microsoft.ML.OnnxRuntime`) pour la détection de
zone math (DistilBERT-multilingual PRUNED, vocab 3569, token-classification BIO).
Phase 3 = le porter en Rust → VSCode **0 .NET**.

**Bug découvert** : le tokenizer C# (`WordPieceTokenizer.cs`) code en dur les ids
de tokens spéciaux du modèle 119k d'origine (`[CLS]=101, [SEP]=102, [UNK]=100`),
alors que le modèle **pruned** a remappé ces ids (`[CLS]=2, [SEP]=3, [UNK]=1`).
Le C# enveloppe donc chaque phrase de « ¥ … ¦ » (ids 101/102) au lieu de
« [CLS] … [SEP] ». Le modèle tolère mais **rate les formules isolées** — c'est la
cause de la régression utilisateur « `vecAB.vecBC` n'est plus reconnu ».

## Décision

Créer **`rust/mc-ner`** (membre du workspace), port de
`adapter-vsto/.../Detection/*` + `Host/Detection/ZoneRefiner.cs`, qui utilise le
**tokenizer NATIF du modèle** (`tokenizer.json` via crate `tokenizers`) → bons ids.

- **Tokenize** : `tokenizers` (offsets) ; le post-processeur ajoute [CLS]/[SEP].
- **Infer** : `ort` v2.0.0-rc.10 (onnxruntime) sur `model_quantized.onnx`,
  `input_ids` + `attention_mask`, tronqué à 128. onnxruntime **statiquement lié**
  → exe **autonome 21,7 Mo, aucune DLL**.
- **Decode BIO** : argmax+softmax par token (mêmes règles de départage que le C#),
  spans char, skip [PAD]/[CLS]/[SEP]/[MASK] ([UNK] passe), seuil moyenne ≥ 0.85.
- **ZoneRefiner** : port pur (pick_nearest, merge_whitespace_adjacent,
  try_extend_forward_whitespace, extend_backward_with_keyword + table mots-clés).
- **Offsets** : `tokenizers` rend des offsets en OCTETS (UTF-8) ; l'hôte (VSCode
  Range / C#) raisonne en UNITÉS UTF-16 → conversion octet→UTF-16 en sortie
  (table `byte_to_utf16`). Caret reçu et offsets renvoyés en UTF-16.
- **Binaire `mc-ner`** : service stdio persistant, **protocole identique** au
  ner-helper (`DETECT\t<caret>\t<text>` → `ZONE\ts\te`/`NONE`, `READY`/`FATAL`).

Parité = **correct, pas bug-for-bug** : le Rust corrige le tokenizer ; gate =
Rust ne détecte pas MOINS bien que le C#.

## Résultat (harnais de parité Rust vs ner-helper C#)

| Entrée | Rust `mc-ner` | C# `ner-helper` |
|---|---|---|
| `vecAB.vecBC` (seul) | **ZONE 0–11** ✅ | **NONE** ❌ |
| `on a vecAB.vecBC` | ZONE 5–16 | ZONE 5–16 |
| `1/x+1` | ZONE 0–5 | ZONE 0–5 |
| `on a f(x)=2x+1 et c'est tout` | ZONE 5–18 | ZONE 5–18 |
| prose pure (`Le chat dort.`) | NONE | NONE |

Le Rust est **strictement meilleur** : identique partout + **détecte les formules
pointées/isolées** que le C# ratait (corrige `vecAB.vecBC`, `4.5`, `u.v`…). Pas de
faux positif sur la prose. Le seuil 0.85 tient.

## Conséquences

- **VSCode = 0 .NET** : un seul service NER natif (`mc-ner.exe`, onnxruntime
  statique). `build.mjs` : `cargo build -p mc-ner` au lieu de `dotnet build
  ner-helper` ; copie `mc-ner.exe` + `tokenizer.json` (au lieu de `vocab.txt`).
  `ner.ts` : chemin exe → `mc-ner.exe` (protocole inchangé). Supprime
  `adapter-vscode/ner-helper`.
- La régression `vecAB.vecBC` est **réglée** par ce lot (tokenizer correct).
- Bundle `out/ner` : exe autonome 21,7 Mo (+ modèle 46 Mo). Plus de net48.

## Écartés

- **Bug-for-bug** (répliquer les mauvais ids C#) : garderait le bug et la
  régression utilisateur. Rejeté.
- **Port du WordPieceTokenizer C#** (vocab.txt) : réimplémente ce que
  `tokenizer.json` + `tokenizers` font nativement, et hériterait du risque d'ids.
- **Corriger aussi le tokenizer C# de Word** : add-in hors périmètre VSCode (à
  faire séparément si on veut aligner Word).

## Hors périmètre

LibreOffice (bascule vers `mc-ner` = plus tard), retrain du modèle (job Colab
user), correction NER de l'add-in Word, Phase 4 (cross-OS, VSIX, signature).

## Vérification

- `cargo build --release -p mc-ner` OK ; `mc-ner.exe` autonome (aucune DLL).
- Harnais parité ci-dessus : Rust ≥ C#, prose → NONE.
- `node build.mjs` : `out/ner/mc-ner.exe` + `tokenizer.json`, plus de net48.
- F5 VSCode : détection auto au caret (à valider par l'utilisateur).
