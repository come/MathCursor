# Feat — Mots-clés proba conditionnelle : « sachant » (fr) / « given » (us) → `\mid`

**Date :** 2026-07-03
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Lié à :** `data/engine/cultures.json` (bloc `aliases`), `engine/tests/.../CultureTests.cs`

## Citation acté

> « je veux bien qu'on rajoute dans nos mots clés : sachant -> given pour le pipe
> avec le raccourci sach et giv ? » … « mid => | » — utilisateur, 2026-07-03.

## Contexte

La barre verticale `|` au milieu d'une expression produit déjà `\mid`
(probabilité conditionnelle `P(A|M)`), via la détection de barre dans
`Parser.cs` (token `bar`/`abs` au milieu → infixe `·mid`). Mais `|` (AltGr+6 en
AZERTY) n'est pas évident pour un lycéen ; le mot lu à voix haute est
« probabilité de A **sachant** M » (« A **given** M » en anglais).

Le moteur dispose déjà d'un système d'**alias lexicaux culture-scoped**
(`data/engine/cultures.json`, ADR
[2026-06-16-Feat-portable-engine-universal-vocab](2026-06-16-Feat-portable-engine-universal-vocab.md)) :
un mot saisi → une clé canonique de `Vocab`. La cible `·mid` est **déjà** une
clé canonique (`symbols.json`, shape `infix`, `cut:true`, rendu `{0}\mid {1}`).

## Décision

Ajouter dans `cultures.json` des alias vers `·mid` :

- **générique** (fr **et** us) : `mid` — c'est le nom LaTeX (`\mid`),
  langue-neutre, comme `cup`/`cap`/`int`.
- **fr** : `sachant` + raccourci `sach`
- **en** (culture `us`) : `given` + raccourci `giv`

`P(A sachant M)` → `P(A\mid M)` (décision `auto`, insertion directe). La barre
`|` continue de marcher en parallèle (chemins indépendants). `giv` (3 lettres)
est **sous** le seuil de préfixe auto (`MinPrefixLen = 4`) donc obligatoirement
explicite ; `sach`/`giv` sont écrits explicitement pour cohérence et lisibilité.

## Tradeoff & alternatives écartées

- **Ne rien ajouter, garder `|` seul** : `|` reste peu découvrable au clavier
  lycéen. Écarté.
- **Alias génériques (toutes cultures)** : `given` mot anglais courant, `sachant`
  français — un scope par culture évite qu'un mot d'une langue morde sur l'autre
  et suit le pattern existant (`somme`/`racine` fr-only). Écarté au profit du
  scope.
- **Laisser le préfixe auto générer `sach`** : `giv` ne serait de toute façon pas
  généré (< 4 lettres). Explicite pour les deux = plus clair. Écarté.

## Conséquences

- **Un seul fichier de données** : C# (`EmbeddedResource`) et Rust
  (`include_str!`) lisent le même `cultures.json` → parité automatique au
  rebuild. Vérifié : le binaire `analyze` Rust rend `P(A\mid M)` pour les quatre
  mots **et** la barre, culture correcte.
- **Tests** : `CultureTests.Conditional_*` verrouillent fr/us + le générique
  `mid` (deux cultures) + l'isolation par culture (`given` inactif en fr,
  `sachant` inactif en us). `dotnet test` engine 25/25, gate fixtures 465/465
  (C# + conformance Rust) inchangé.
- **WASM** : rebuild pour embarquer le `cultures.json` mis à jour.

## Validation

`dotnet test engine/...` 24/24 · `cargo test` mc-engine vert · binaire `analyze`
Rust : `P(A\mid M)` pour `sachant`/`sach` (fr), `given`/`giv` (us), `|` (fr+us).
