# Feat — Popup : Entrée sans sélection redescend à l'éditeur ; touche commit-1er désignée par l'hôte

**Date :** 2026-06-25
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-24-Feat-rust-unified-toolkit](2026-06-24-Feat-rust-unified-toolkit.md) (coquille `mc-popup`), [2026-06-25-Feat-libreoffice-rust-core](2026-06-25-Feat-libreoffice-rust-core.md), [2026-06-24-Feat-vscode-host-popup-ner](2026-06-24-Feat-vscode-host-popup-ner.md)

## Citation acté

> « le entrée valide le choix MEME si aucun choix n'est selectionné. il faudrait
> optionnelement envoyer à la popup la touche attendue pour ce comportement
> (AUCUNE par defaut) et l'adapteur vscode ou libreoffice envoie sur option
> validée par l'utilisateur (tab par exemple dans word) » — utilisateur, 2026-06-25

Puis, en correction du comportement attendu :

> « en fait je me suis trompé, entrée sans choix ca fait un entree dans l'editeur
> et du coup probablement la popup se ferme » — utilisateur, 2026-06-25

## Contexte

La coquille popup Rust partagée (VSCode mode actif + LibreOffice mode passif)
**avale la touche Entrée et valide le 1er candidat même quand aucune ligne n'est
surlignée** :

- `rust/mc-popup/web/index.html` `mcActivate` : `var i = (_sel < 0) ? 0 : _sel;`
  → commit l'index 0 par défaut, même sans navigation.
- `rust/mc-popup/src/main.rs` hook clavier : `VK_RETURN || VK_TAB` → même
  `KeyCommit`, **toujours avalé** (`LRESULT(1)`).

Conséquence : une frappe Entrée juste après l'ouverture insère un candidat que
l'utilisateur n'a pas choisi, et le saut de ligne attendu dans l'éditeur n'a pas
lieu.

## Décision

### 1. Entrée sans sélection = touche normale de l'éditeur

Quand l'utilisateur n'a **pas navigué** (aucune ligne surlignée), la popup
**n'intercepte pas** Entrée : la touche redescend à l'éditeur (saut de ligne
normal) et la popup se ferme. Quand l'utilisateur **a navigué** (`_sel >= 0`),
Entrée valide la ligne surlignée (avalée).

### 2. Touche « commit-1er » désignée par l'hôte (AUCUNE par défaut)

La coquille ne fige aucune touche de validation implicite. L'hôte (adapter) peut
désigner **une** touche qui valide le 1er candidat sans navigation, façon Tab dans
Word. Par défaut : AUCUNE → aucune touche ne valide sans sélection.

- **Mode actif (VSCode)** : la coquille lit le clavier → l'hôte passe la touche
  dans `show` (`commitFirstKey`, ex. `"Tab"`) ; le hook compare la VK pressée.
- **Mode passif (LibreOffice)** : l'hôte lit le clavier → il porte l'intention
  dans la commande `activate` (`implicit: true`).

VSCode et LibreOffice désignent **Tab** comme touche commit-1er.

### 3. Mécanisme : mirror « a navigué » dans la couche d'interception

La décision d'avaler ou non Entrée est **synchrone** (hook bas-niveau Rust /
`XKeyHandler` UNO) alors que la sélection (`_sel`) vit dans le HTML. On exploite
l'invariant de `mcNav` : **dès la 1ʳᵉ flèche, `_sel >= 0` et le reste jusqu'au
prochain `show`**. Donc « a navigué » ⇔ « il y a une sélection » — un simple
booléen miroir (`HAS_SEL` en Rust, `navigated` en Python) suffit ; l'index exact
n'est pas nécessaire à la couche d'interception.

## Tradeoff & alternatives écartées

- **Réinjecter un saut de ligne via `SendInput` après commit** : hacky, fragile
  (timing, focus), dépendant OS. Le pass-through natif (ne pas avaler la touche)
  est déterministe.
- **Faire remonter l'index sélectionné à l'hôte en continu (stdout)** : plomberie
  asynchrone + races, alors qu'un booléen « a navigué » mis à jour côté
  interception suffit.
- **Garder une touche de commit-1er figée dans la coquille** : contraire au
  besoin « la touche est décidée par le logiciel qui ouvre la popup » ; chaque
  hôte a sa convention.
- **mcActivate commit 0 par défaut (statu quo)** : c'est le bug.

## Conséquences

- **Code touché** :
  - `rust/mc-popup/web/index.html` — `mcActivate(implicit)` : commit `_sel` si
    `>= 0`, sinon commit 0 **seulement si** `implicit`, sinon rien.
  - `rust/mc-popup/src/main.rs` — `UserEvent::KeyCommit { implicit }` ;
    `commitFirstKey` dans `show` ; `implicit` dans `activate` ; statics
    `COMMIT_FIRST_VK` + `HAS_SEL` ; `hook_proc` réécrit (Entrée pass-through si
    pas de sélection : `post(Close)` + `CallNextHookEx` sans `LRESULT(1)`).
  - `adapter-vscode/extension/src/popup.ts` — `commitFirstKey: 'Tab'` dans le payload `show`.
  - `libreoffice-ext/popup_client.py` — `activate(implicit=False)`.
  - `libreoffice-ext/mathcursor.py` — `_K_TAB`, miroir `navigated`, `_KeyHandler`
    (RETURN pass-through si pas navigué, TAB = commit-1er), `_activate(implicit)`.
- **API publique** : protocole stdin de la coquille étendu (champs **optionnels**
  `commitFirstKey`, `implicit`) → **rétro-compatible** (anciens hôtes/tests :
  VK=0, `implicit=false` → comportement strict).
- **Tests** : `mc-popup` n'a pas de tests Rust (validé manuellement + smoke
  `popup_client.py`). Gate moteur (`fixtures.json` 456/456) non concernée.
- **Règles MC impactées** : aucune.
- **Code mort constaté** : `libreoffice-ext/mathcursor.py:_autopopup_move` (aucun
  appelant ; le chemin câblé est `_nav`/`_activate`) — non touché ici.

## Validation post-fix

Test auto impossible (interaction clavier + fenêtrage). Observation utilisateur,
sur Windows :

- **VSCode** : (a) Entrée sans nav → saut de ligne inséré dans l'éditeur + popup
  fermée ; (b) ↓ puis Entrée → commit la ligne surlignée ; (c) Tab sans nav →
  commit le 1er candidat.
- **LibreOffice** : idem ; Tab popup-ouverte = commit-1er (et n'indente pas le
  document) ; Entrée sans nav insère un paragraphe + ferme.
- **Build** : `cargo build` (cible `mc-popup`) sans warning.
