# Fix — Détection NER/moteur hors thread UI (LibreOffice) + timeouts stdio

**Date :** 2026-06-29
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-25-Feat-libreoffice-rust-core](2026-06-25-Feat-libreoffice-rust-core.md) (cœur unifié Rust = origine de la régression) ; [2026-06-17-Feat-libreoffice-ner-autodetection](2026-06-17-Feat-libreoffice-ner-autodetection.md) (tick AsyncCallback) ; mémoire `libreoffice-uno-gotchas` (threading)

## Citation acté

> « Détection hors thread UI + timeouts » — utilisateur, 2026-06-29 (choix entre fix complet hors-thread, timeouts seuls, ou diagnostic supplémentaire)

## Contexte

**Symptôme : Writer gèle (AppHang)** pendant l'auto-détection LibreOffice.

Le tick de détection (`_autodetect_tick`) est posté sur le **thread principal** via
`AsyncCallback` — correct pour toucher l'UNO et créer la popup. Mais il appelait
`_detect_candidate()` qui fait des requêtes **stdio synchrones** aux binaires Rust
(`mc-ner` détection, `analyze` moteur) **sur ce même thread principal**. Or
`rust_clients.py` lit la réponse via `subprocess … stdout.readline()` **sans aucun
timeout** :

- **Au 1ᵉʳ `DETECT`** : `mc-ner` charge le modèle ONNX (~46 Mo) ; le `readline` du
  handshake `READY` (`_spawn`, l.37) **bloque l'UI** le temps du chargement (plusieurs
  secondes), à chaque activation.
- **Ensuite** : si un enfant Rust se coince (inférence bloquée, crash sans fermer
  stdout) → `readline` (`request`, l.53) **ne revient jamais** → **gel permanent de
  Writer, sans récupération**.

Régression introduite par le cœur unifié Rust (`df8846c`, ADR 2026-06-25) : avant, le
moteur était du Python in-process, aucun subprocess à attendre. VSCode ne gèle pas car
il tourne dans l'extension host Node, pas sur un thread UI.

Ni crash ni boucle CPU : un **blocage d'I/O sur le thread UI**.

## Décision

**Sortir toute la détection stdio du thread UI**, et **borner les lectures stdio** par
des timeouts (défense en profondeur).

### 1. Tick en trois étapes (`mathcursor.py`)

L'UNO n'est pas thread-safe → seules les lectures de document restent sur le thread
principal ; le stdio (lent / susceptible de bloquer) part en fond.

- **Étape 1 — snapshot (thread principal)** : `_autodetect_tick` lit le contexte ¶
  (`_para_context` : texte + caret) — lectures UNO rapides, **zéro stdio** — pose la
  garde `busy`, stocke le snapshot, lance un thread de fond.
- **Étape 2 — détection (thread de fond)** : `_detect_worker` → `_detect_offsets(texte,
  caret)`, version **pure** de l'ex-`_detect_candidate` (NER + moteur en stdio,
  renvoie des **offsets** `(début, fin)`, **aucun accès UNO**). Re-poste l'étape 3 via
  `AsyncCallback`, quoi qu'il arrive.
- **Étape 3 — application (thread principal)** : `_ApplyCallback` crée le range UNO à
  partir des offsets (`_zone_range`) et ouvre/rafraîchit/ferme la popup ; libère `busy`.

La garde `busy` sérialise un cycle à la fois (coalesce les rafales de frappe), comme
avant. La coquille popup reste pré-chauffée en fond au `start` (inchangé) → `show()` ne
bloque pas non plus.

### 2. Timeouts stdio (`rust_clients.py`)

`_StdioProc` gagne un **thread lecteur + `queue.Queue`** :

- `_spawn` attend `READY` via `queue.get(timeout=spawn_timeout)` (généreux, modèle ONNX).
- `request` écrit puis lit via `queue.get(timeout=req_timeout)`.
- **Sur timeout → on TUE l'enfant** (`_kill`) et on renvoie `None` : un process coincé
  est recyclé (respawn paresseux au prochain appel) au lieu d'être attendu indéfiniment.
  Tuer (plutôt que renvoyer None en gardant le process) évite la **désynchronisation**
  réponse-N-pour-requête-N+1.

## Tradeoff & alternatives écartées

- **Timeouts seuls (sans sortir du thread UI)** : écarté — transforme le gel infini en
  gel **borné** (le timeout + le chargement modèle bloquent quand même l'UI à chaque
  détection). Insuffisant pour une frappe fluide.
- **Tout faire sur le thread de fond (y compris l'UNO)** : impossible — l'UNO de
  LibreOffice n'est pas thread-safe ; lire le document / créer un range hors thread
  principal corrompt l'état ou plante.
- **Garder le moteur Rust mais le ré-héberger in-process (FFI/PyO3)** : hors périmètre
  (re-architecture lourde) ; le modèle stdio est partagé avec VSCode (cœur unifié).

## Conséquences

- **Code touché** : `libreoffice-ext/mathcursor.py` (split tick : `_autodetect_tick`
  réécrit, `_detect_worker` + `_ApplyCallback` + `_apply_detection` ajoutés,
  `_autodetect_tick_inner` supprimé, `_detect_candidate` → `_detect_offsets` renvoyant
  des offsets) ; `libreoffice-ext/rust_clients.py` (`_StdioProc` : reader-thread +
  queue + `spawn_timeout`/`req_timeout` + `_kill`).
- **Latence** : le chargement du modèle (1ᵉʳ DETECT) n'impacte plus l'UI — il se fait en
  fond ; la 1ʳᵉ popup apparaît avec un léger délai au lieu de geler Writer.
- **Tests** : `rust_clients.py` est pur (testable hors LibreOffice) ; le split tick se
  valide en usage réel (frappe → popup, pas de gel ; couper `mc-ner` en plein vol ne
  doit plus figer Writer).
- **Règle MC** : renforce la règle threading de `libreoffice-uno-gotchas` (UNO sur thread
  principal, I/O bloquante jamais sur le thread UI).

## Validation post-fix

1. Activer l'auto-détection, taper des maths : la popup apparaît, **Writer ne gèle
   jamais** (y compris au tout premier déclenchement / chargement modèle).
2. Tuer `mc-ner.exe` pendant la frappe (`Stop-Process`) : Writer **reste réactif** (la
   détection échoue silencieusement, le service se respawne au coup suivant) au lieu de
   figer.
3. Frappe soutenue : pas de cycles de détection empilés (garde `busy` respectée).
