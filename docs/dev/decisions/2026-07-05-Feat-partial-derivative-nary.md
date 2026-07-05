# Feat — Dérivée partielle : `partial` en n-aire (part x y → ∂x/∂y, part x → ∂x)

**Date :** 2026-07-05
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Lié à :** `data/engine/symbols.json` (`partial`), `engine/tests/.../fixtures.json`
**Prépare / réutilise :** ADR
[2026-06-12 nary-skeleton-pair](2026-06-12-Feat-nary-skeleton-pair.md) (paire de
squelettes courts à la frontière)

## Citation acté

> « un partial (raccourci part) avec deux arguments part x y => dx / dy (d du
> partial) et si on n'a qu'un argument ca fait part x => dx seul … implementer les
> deux choix (comme dans les sommes ou integrales courtes) » … « du coup j'ai la
> meme erreur exactement si je fais somme k f(k) / somme k g(k) :) c'est coherent
> pour moi » — utilisateur, 2026-07-05.

## Contexte

`partial` était un **prefix** arité 1 (`\partial {0}`). On veut la **dérivée
partielle** en fraction : `part x y` → `\frac{\partial x}{\partial y}`, tout en
gardant `part x` → `\partial x`. C'est exactement le modèle **n-aire + variantes
courtes** de `sum`/`int`/`lim` (une forme pleine + une forme courte, offertes en
**paire de squelettes à la frontière de frappe**).

## Décision

Convertir `partial` de `prefix` en **`nary`** :
- **arité 2** (canonique) : `\frac{\partial {0}}{\partial {1}}`
- **variante arité 1** : `\partial {0}` (acceptGuard `tight_body_0`, réutilisé de `lim`)

`part` reste l'**alias auto-préfixe** (≥ 4 lettres, non ambigu) vers `partial` —
aucune entrée `cultures.json` à ajouter. Résultat :
- `part x y` → `\frac{\partial x}{\partial y}` (**auto**)
- `part x` (et `partial x`) → **popup** `[ \partial x , \frac{\partial x}{\partial □} ]`
- `part f x` → `\frac{\partial f}{\partial x}` (voie canonique de ∂f/∂x)

## Tradeoff & alternatives écartées

- **Régression assumée sur la forme longue `partial f / partial x`** : elle ne
  rend plus `\frac{\partial f}{\partial x}` mais `\partial \frac{f}{\partial x}`
  (le premier `partial`, n-aire, avale en avant hors frontière). **C'est le même
  comportement que `sum k f(k) / sum k g(k)`** (→ `\sum_{k} \frac{f(k)}{\sum_{k}
  g(k)}`) — vérifié ; limite générale des n-aires courts en milieu d'expression,
  jugée **cohérente** par l'utilisateur. La voie canonique de ∂f/∂x devient
  `part f x`. Fixture `partial f / partial x` retirée, remplacée par `part f x`.
- **Option A (`part` n-aire distinct, `partial` prefix conservé)** : évitait la
  régression mais faisait diverger `part` ≠ `partial`. Écartée au profit de la
  cohérence `part == partial` (choix utilisateur, régression acceptée car alignée
  sur `sum`/`int`).
- **Rendre la variante arité-1 disponible hors frontière** (pour sauver la forme
  longue) : modif moteur invasive + ambiguïté accrue. Écartée.

## Conséquences

- Changement **100 % données** (`symbols.json`), zéro code moteur (mécanique
  n-aire + `tight_body_0` déjà en place). Parité C#/Rust automatique.
- Gate **fixtures 481 → 484** (C# `FixtureTests` + conformance Rust) : ajouts
  `part x y`, `part x`, `partial x`, `part f x` ; retrait `partial f / partial x`.
- `nabla` (prefix ∂-like) **inchangé**. WASM rebuild ; add-in VSTO à rebuild.

## Validation

`dotnet test` engine 22/22 · conformance Rust **484/484** · binaire `analyze` :
`part x y` → `\frac{\partial x}{\partial y}` (auto), `part x` → popup
`[\partial x, \frac{\partial x}{\partial □}]`, `part f x` → `\frac{\partial f}{\partial x}`.
