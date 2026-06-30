# Fix — Coefficient signé en cellule de matrice (greffe unaire dans la forêt)

**Date :** 2026-06-30
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [2026-05-21-Feat-matrix-pattern](2026-05-21-Feat-matrix-pattern.md) — le modèle matrice
- [2026-05-23-Fix-engine-leading-unary-prefix](2026-05-23-Fix-engine-leading-unary-prefix.md) — l'unaire de tête (autre couche, archi core-csharp)

## Citation acté

> « pour moi ca doit venir dans l'arbre avec des coefs, (x -1 deux lectures : (x-1 et matrice (x,-1 » … « oui go plan » — utilisateur, 2026-06-30

(Design proposé par l'utilisateur : ne pas décider lexer/cellule, faire exister les deux lectures dans la forêt et laisser `Score` arbitrer.)

## Contexte

Une matrice avec un **nombre signé en cellule non-initiale** renvoie « erreur » au
moteur → **pas de popup** : `(1 -2; 3 4)`, `(x -1; y 2)`, `(1 2; 3 -4)`. Cas très
courant (coefficients négatifs). Diagnostic (exécution moteur + binaires Rust) :

- Le **lexer** fige signe-vs-binaire par le token précédent (`binaryPos`,
  `Lexer.cs:92`/`:286`) : après un opérande, `-` est **binaire**. Dans `(x -1`, le `-`
  suit `x` → binaire, **avant** `Matrices()`. La frontière de cellule (une espace)
  est invisible au lexer (pas de `sep`).
- `x -1` (soustraction) et la cellule `-1` ont **le même flux de tokens** ; seul le
  `;` (plus loin) distingue. Lexer gauche-à-droite **sans lookahead** → impossible de
  trancher au lexer sans casser la soustraction.
- En cellule isolée, le seul chemin unaire de `ParseSpan` est la garde `i==0`
  (`Parser.cs:365`), ancrée au **début absolu** de l'entrée → inopérante au milieu →
  cellule `-1` = forêt vide → cellule morte (`Parser.cs:494`) → matrice morte → erreur.

## Décision

**Greffer la lecture unaire au montage de chaque cellule de matrice**, en réutilisant
le primitif déjà présent dans `Splits()` (`Parser.cs:400-407`, miroir Rust
`parser.rs:601-625`) qui fait marcher `lim x -2`. On ne **décide** rien : on **produit
les deux lectures** (`x-1` binaire via la lecture non-matricielle ; `[x, -1]` matrice
via la greffe) et `Score` arbitre. Le `;` qui force la structure matricielle fait
gagner la lecture coefficient signé.

Helper `CellForest(s, e, allowGraft)` dans `Matrices()` : `ParseSpan(s, e)` ∪ (si
`allowGraft` ET la tête de cellule est un infixe avec `Unary` au vocabulaire :
`prefix(unary, ParseSpan(s+1, e))`). Miroir octet-pour-octet dans
`rust/mc-engine/src/parser.rs::matrices`.

**Déclencheur `allowGraft = hasExplicitSep || b == _end`** (b = fin du span matrice,
`_end` = frontière de frappe), affiné en deux temps :
- greffe **inconditionnelle** → cassait `(a +b)/2` (paren-opérande passée en popup au
  lieu de `\frac{a+b}{2}`) ;
- greffe **réservée au séparateur explicite `;`/`,`** → réparait les vraies matrices
  mais laissait les états EN COURS de frappe morts (`(a -2 ;` → « aucune lecture »,
  `(a -2 c` → erreur) ;
- greffe si **séparateur explicite OU matrice à la frontière de frappe** (`b == _end`,
  = paren en cours de saisie) → couvre les deux : `(a -2` propose la matrice, `(a -2 ;`
  affiche la matrice partielle à carrés `\begin{pmatrix}a & -2 \\ □ & □\end{pmatrix}`,
  tout en gardant `(a +b)/2` en auto (paren COMPLÈTE suivie de `/2` → pas à la frontière).

## Tradeoff & alternatives écartées

- **Trancher dans le lexer** (`binaryPos`) : écartée — `x -1` et la cellule `-1` sont
  lexicalement identiques, le lexer n'a pas de lookahead sur le `;` → casserait la
  soustraction `x -1`.
- **Relâcher la garde `i==0` de `ParseSpan:365`** : écartée — `ParseSpan` est un chart
  **mémoïsé sur `(i,j)` absolus** ; rendre l'unaire dépendant du contexte « cellule »
  casse la clé de mémoïsation ou inonde la forêt de lectures « trou gauche » partout.
  (Et la garde `i==0` ne sert de toute façon pas aux signes : le lexer les gère déjà.)
- **Re-signer la cellule au découpage** : la greffe est plus propre (additive, locale,
  réutilise un primitif éprouvé, laisse le `Score` décider au lieu d'un choix dur).

## Conséquences

- **Code touché** : `engine/src/MathCursor.Engine/Parser.cs` (`Matrices`, helper
  `CellForest`) + miroir `rust/mc-engine/src/parser.rs` (`matrices`).
- **Fixtures** : `engine/tests/.../fixtures.json` — **9 ajouts** (456 → **465**) :
  matrices signées complètes (`(1 -2; 3 4)`, `(x -1; y 2)`, `(1 2; 3 -4)`,
  `(-1 -2; -3 -4)`, `(-1 2; 3 4)`), états en cours de frappe (`(a -2 ;` partielle à
  carrés, `(a -2` popup avec matrice, `(a -2 c`), et régression anti-drift `(x -1)`
  (complète → reste `x-1`). Compte verrouillé dans `FixtureTests.cs` + `conformance.rs`.
  Rejouées C# **et** conformance Rust (465/465 des deux côtés).
- **API publique** : inchangée.
- **Drift borné/voulu** : la greffe ne s'active que pour une cellule à tête infixe
  unaire ET (séparateur explicite OU frontière de frappe) ; les parens COMPLÈTES sans
  `;` (`(a +b)/2`, `(x -1)`) restent des expressions groupées. Vérif clé : `(a +b)/2`
  reste `\frac{a+b}{2}`, `(x -1)` reste `x-1`.
- **Règles MC** : aucune. Cœur moteur pur (netstandard2.0) + miroir Rust — parité
  `fixtures.json` maintenue (465/465 des deux côtés).

## Limite connue (hors périmètre)

`(1 - 2; 3 4)` (espaces des **deux** côtés du `-`) : la cellule se découpe en `-`
isolé → reste imparsable. Cas moins courant que `-2` collé (le cas produit visé) ;
suivi possible ultérieurement.

## Validation post-fix

1. Moteur (`rust/mc-engine analyze` + console C#) sur les cas signés → sorties
   identiques C#/Rust → bakées dans les fixtures.
2. `scripts/run-tests.ps1` vert : engine C#, conformance Rust, adapter, parité.
3. `(x -1)` seul reste `x-1` (pas une matrice 1×2).
4. Dans Word : `(1 -2; 3 4)` → popup matrice (plus d'« erreur »).
