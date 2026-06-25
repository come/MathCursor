# Feat — Portage de la couche « chaînes d'équations multilignes » vers LibreOffice & VSCode

**Date :** 2026-06-25
**Kind :** Feat
**Température :** forte (nouvelle couche structurelle partagée par les hôtes phase 2)
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-multiline-chain-eqarr-architecture](2026-06-10-Feat-multiline-chain-eqarr-architecture.md) (la couche Word d'origine), [2026-06-25-Feat-libreoffice-rust-core](2026-06-25-Feat-libreoffice-rust-core.md) (cœur Rust unifié), `LeadingRelationTests.cs` (frontière moteur/adapter)

## Citations acté

> « on va porter la couche WORD de chaining ! fais moi le plan, idéalement pour
> vscode aussi, mais on commence par libreoffice » — utilisateur, 2026-06-25

> « Incrémental, mais on a abandonné le tag CC, tu peux le laisser de côté aussi,
> on controle Z pour revenir en arriere » — utilisateur, 2026-06-25

> « on va differer de word pour ça, les systèmes seront gérés en multilignes avec
> ctrl+entree OU ; comme les matrices » — utilisateur, 2026-06-25

## Contexte

Sur Word, une **relation** (`=`, `≤`, `≈`…) ou un **connecteur** (`⟺`, `⇒`…) en
début de ligne crée/étend un **bloc aligné** (equation array) empilant les lignes
de raisonnement, signes alignés. Cette couche vit **hors moteur** (ADR
2026-06-10) : le moteur renvoie « erreur » sur une relation en tête (volontaire,
verrouillé par `LeadingRelationTests.cs` — « membre gauche vide = pas une
expression »), et c'est l'**adapter** Word qui détecte le marqueur, n'envoie que
le *reste* au moteur, et compose le bloc OMML `<m:eqArr>`.

**LibreOffice et VSCode n'ont jamais reçu cette couche** → taper `=1/2` n'y produit
rien (le moteur seul voit `=1/2` → erreur). D'où l'étonnement utilisateur : « ça
marche sur Word » = c'est la couche chaîne de l'adapter VSTO, pas le moteur. On la
porte vers les hôtes phase 2.

## Décision

### 1. Logique pure dans le cœur Rust partagé

La logique pure de la couche Word (table marqueurs, détection ligne-relation, split
top-level, composition) est portée dans **`mc-engine`** (`rust/mc-engine/src/chain.rs`),
donc **partagée LibreOffice + VSCode** (cohérent avec le cœur unifié). Le moteur
produisant déjà `latex` ET `starmath` par candidat, la composition émet les **deux**
rendus alignés. Exposé par un verbe stdio `COMPOSE` du binaire `analyze`.

### 2. UX incrémentale SANS métadonnée de bloc

On reproduit l'UX incrémentale de Word (taper une ligne → valider → fusion avec le
bloc au-dessus), **mais sans l'infra « source de vérité » du Tag CC** (abandonnée).
À la place : **état de chaîne transitoire en mémoire** côté hôte (dernier bloc
inséré + ses lignes sources `{steno, index}`). **Retour arrière = Ctrl+Z natif**
(chaque commit = un pas d'undo). Append-only (ADR 2026-06-10, P3). Limite assumée :
l'état se perd si on quitte la zone / ferme le doc → on ré-amorce une chaîne.

### 3. Systèmes `{` hors périmètre

Les systèmes d'équations (accolade englobante) ne sont **pas** portés ici. Ils
seront gérés différemment de Word, en **multiligne via `Ctrl+Entrée` ou `;`**,
comme les matrices (chantier séparé).

### 4. Rendus cibles

- **VSCode** : `\begin{aligned}` inséré en display (`$$…$$` / `\[…\]`).
- **LibreOffice** : StarMath aligné (`matrix{ alignr … # alignl … ## … }`), idiome
  exact **dé-risqué par un POC visuel** avant câblage (StarMath capricieux).

### 5. Phasage

- **Phase 0** — cœur Rust `chain.rs` + verbe `COMPOSE` + tests portés du C#.
- **Phase 1** — LibreOffice (StarMath, incrémental). *On commence ici.*
- **Phase 2** — VSCode (LaTeX, incrémental). Après validation Phase 1.

## Tradeoff & alternatives écartées

- **« Bloc d'un coup »** (taper toute la chaîne en texte puis tout convertir) :
  plus simple, mais l'utilisateur veut le flow incrémental façon Word. Écarté.
- **Porter l'infra Tag CC** (source de vérité persistée + re-génération/édition/revert
  par hôte) : très gros chantier (StarMath objects / texte LaTeX n'ont pas d'analogue
  propre au CC+SourceMap). Remplacé par état transitoire + Ctrl+Z (décision user).
- **Logique dans chaque adapter** (Python + TS) : duplication, dérive de parité.
  Écarté au profit du cœur Rust unique.
- **Re-stocker les LaTeX choisis** (comme Word) : inutile — `compose_chain` rejoue
  `analyze(steno)` + `ranked[index]` (déterministe) → même candidat, sans stockage.

## Conséquences

- **Code (Phase 0)** : `rust/mc-engine/src/chain.rs` (neuf), `rust/mc-engine/src/lib.rs`
  (expose le module), `rust/mc-engine/src/bin/analyze.rs` (verbe `COMPOSE`), tests.
- **Code (Phase 1)** : `libreoffice-ext/rust_clients.py` (`compose`), `libreoffice-ext/mathcursor.py`
  (détection relation-line, état transitoire, commit incrémental, repli autonome).
- **Code (Phase 2)** : `adapter-vscode/extension/src/{engine.ts, extension.ts}`.
- **API** : protocole stdio `analyze` étendu (verbe `COMPOSE`), rétro-compatible
  (ligne sans préfixe `COMPOSE` = ancien chemin `analyze`).
- **Moteur** : inchangé (relation en tête = « erreur », toujours) ; la gate
  `fixtures.json` 456/456 reste intacte. Nouveaux tests = suite séparée `chain`.
- **Parité Word** : les rendus diffèrent (OMML eqArr vs LaTeX `aligned` vs StarMath
  `matrix`) → pas de fixture de parité commune ; la spec rejouée = les cas des tests
  C# (detector/split/composer), pas l'OMML.

## Validation post-fix

- **Rust** : `cargo test -p mc-engine` (tests `chain` verts) + smoke stdin/stdout `COMPOSE`.
- **LibreOffice manuel** (Windows) : `f(x)=2x+2-2` ⏎ `=2x` ⏎ `<=> x=1`, valider chaque
  ligne → bloc aligné qui s'étend ; Ctrl+Z remonte ligne par ligne ; repli autonome
  si rien au-dessus.
- **VSCode manuel** : idem → `$$\begin{aligned}…$$`.
