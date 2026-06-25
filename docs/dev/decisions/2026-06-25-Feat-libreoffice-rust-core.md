# Feat — Cœur unifié : LibreOffice sur le moteur + NER RUST (fin du Python)

**Date :** 2026-06-25
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Lié à :** [2026-06-24-Feat-rust-unified-toolkit.md](2026-06-24-Feat-rust-unified-toolkit.md), [2026-06-24-Feat-rust-engine-port.md](2026-06-24-Feat-rust-engine-port.md), [2026-06-24-Feat-rust-ner-port.md](2026-06-24-Feat-rust-ner-port.md), [2026-06-24-Feat-libreoffice-popup-webview-shell-rust.md](2026-06-24-Feat-libreoffice-popup-webview-shell-rust.md)

## Citation acté

> « on unifie le cœur unifié pour libreoffice » ; « tu peux supprimer l'ancien
> engine python après il est dans git de toute façon » — utilisateur, 2026-06-25

## Contexte

VSCode tournait 100 % Rust et LibreOffice partageait déjà la **popup** Rust, mais
exécutait encore son **moteur en Python** (`engine-python/mc_engine`) et sa
**détection NER en Python** (`libreoffice-ext/mc_ner`). Objectif : LibreOffice
appelle les **mêmes binaires Rust que VSCode** → un seul cœur, fin du Python
produit.

**Blocage** : LibreOffice n'insère pas du LaTeX mais du **StarMath**
(`_convert` → `to_starmath(node)` → `_insert_formula`). Le binaire `analyze` ne
sortait que du LaTeX. Unifier le moteur a donc exigé de porter le rendu StarMath
en Rust.

## Décision

- **StarMath en Rust** : `engine-python/mc_engine/starmath.py` → `rust/mc-engine/
  src/starmath.rs` (`render_starmath(node, culture)`) ; table `sameas` ajoutée au
  `Registry`. Le binaire **`analyze`** émet désormais
  `{"latex","starmath","cost"}` par candidat (VSCode ignore `starmath`).
- **Gate de parité StarMath** : Python `to_starmath` vs Rust `render_starmath`
  sur les 456 fixtures (comparaison par LaTeX commun) → **477/477 candidats
  identiques** (1 bug corrigé : espace `iint`/`iiint` incomplet). Vérifié AVANT
  suppression du Python.
- **NER LibreOffice → `mc-ner.exe`** : le service Rust fait détection + raffinage
  (pick_nearest/merge/extend) et rend la zone finale. Supprime tout `mc_ner`
  Python (detector/refiner/tokenizer/zone).
- **Clients Python** : `libreoffice-ext/rust_clients.py` (`EngineClient` →
  `analyze.exe` ; `NerClient` → `mc-ner.exe`, stdio synchrone). `mathcursor.py`
  retire `mc_engine`/`to_starmath`/`culture`/`mc_ner` et utilise les clients ;
  résultat = dict `{decision, ranked:[{latex,starmath,cost}], hasNote}` ;
  `_CULTURE` = "fr". `mathcursor.py` reste la **colle UNO** (insertion StarMath,
  key handler, tick) — c'est tout ce qui demeure en Python côté LibreOffice.
- **`build_oxt.py`** : build + stage `bin/<tag>/{analyze,mc-ner}` + bundle
  `rust_clients.py` ; **retire** `mc_engine`, `data/engine`, `mc_ner`. Le binaire
  est trouvé par `mathcursor._bin()` (installé `bin/<tag>/` sinon dev
  `rust/target/release`).
- **Suppression** d'`engine-python/` et `libreoffice-ext/mc_ner/` (récupérables
  dans git). `scripts/run-tests.ps1` : la conformance Python (non bloquante) est
  remplacée par la **conformance Rust `mc-engine` (bloquante)**. CLAUDE.md mis à
  jour (structure + parité).

## Conséquences

- LibreOffice spawne **3 binaires Rust** (analyze, mc-ner, mc-popup) — les mêmes
  que VSCode. **Plus de moteur ni de NER Python** ; il ne reste que la colle UNO.
- Parité = **C# (Word) + Rust (VSCode & LibreOffice)**. Le port Python, scaffold
  de portage, est retiré.
- Modèle NER (~46 Mo) **non bundlé** : en dev lu depuis le repo (`models/latest`).
  Sans modèle → auto-détection off (Ctrl+Espace marche), comme avant.

## Risques

- **LibreOffice non testable** par l'agent (pas de runtime) → validation par
  réinstall .oxt (utilisateur). Risque sur le câblage UNO (mathcursor).
- **StarMath** : justesse fine non automatiquement gardée après retrait du Python
  (verrouillée == Python à l'instant du port, 477/477). Le gate permanent reste
  le LaTeX (456/456 Rust + C#).
- Offsets mc-ner en UTF-16 vs `str` Python (code points) : identiques en BMP
  (maths) ; astral non géré (déjà le cas avant).

## Écartés

- Garder le moteur Python juste pour `to_starmath` (hybride) : pas unifié, et le
  `to_starmath` exige l'AST que le binaire n'expose pas → rejeté.

## Vérification

1. `cargo run -p mc-engine --bin conformance` → 456/456 (LaTeX inchangé).
2. Parité StarMath Python↔Rust : 477/477 candidats LaTeX communs identiques.
3. `analyze.exe` : `fr\t1/2` → `…"starmath":"{1} over {2}"…`.
4. `python build_oxt.py` → .oxt avec `bin/<tag>/analyze.exe`+`mc-ner.exe`+
   `rust_clients.py`, sans `mc_engine`/`mc_ner`/`data` ; py_compile OK.
5. **LibreOffice (utilisateur)** : réinstall .oxt, redémarrage LO ; Ctrl+Espace
   (insertion StarMath) + auto-détection NER + popup « voir plus ».

## Hors périmètre

Bundle release du modèle NER (46 Mo) ; builds mac/linux des binaires ; signature.
