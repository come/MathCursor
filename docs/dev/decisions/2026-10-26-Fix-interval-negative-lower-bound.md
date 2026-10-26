# Fix — Intervalles à borne gauche négative (]−2 ; 3[, [−2 ; 3], ]−∞ ; −2[)

**Date :** 2026-10-26
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-04-29-Feat-canonical-sets](2026-04-29-Feat-canonical-sets.md)

## Citation acté

> « corrige les intervalles ouvert neg » — utilisateur, 2026-10-26.

## Contexte

En rédigeant un exercice (signe d'un trinôme, conclusion
`]−∞ ; −2[ ∪ ]3 ; +∞[`) : tout intervalle dont la **borne gauche est négative**
renvoyait « erreur » (`]-1;1[`, `[-2;3]`, `]-inf;2[`), alors que `]2;3[`,
`]2;+inf[` et `]0;-1[` passaient. Notation omniprésente au lycée.

Cause : le lexer range `[` et `]` dans un même genre de jeton « crochet » et
traitait **tout** crochet comme une fin d'opérande. Le `-` qui suit était donc lu
comme une soustraction (infixe) sans opérande gauche, au lieu d'un signe (préfixe).
`]0;-1[` passait parce que ce `-` suit un `;`.

## Décision

Un crochet retient, au moment où il est lexé, s'il est en **position d'ouverture**
(`Token.Opens` / `opens` : aucun opérande juste avant lui). Seul un crochet qui
**ferme** termine un opérande ; après un crochet ouvrant, `-` / `+` sont des signes.
Même règle factorisée (`EndsOperand` / `ends_operand`) pour les deux endroits où le
lexer choisit infixe ou préfixe. C# et Rust identiques.

## Tradeoff & alternatives écartées

- **Distinguer `[`/`]` par leur caractère** : faux en notation française, où `]`
  ouvre (`]a;b[`) et `[` ferme.
- **Rattraper au parseur** (greffer un préfixe sur un infixe sans opérande gauche
  dans un intervalle) : traite le symptôme dans une seule construction ; la cause est
  au lexer.

## Conséquences

- **Code touché** : `engine/.../Node.cs` + `Lexer.cs`, `rust/mc-engine/src/node.rs`
  + `lexer.rs`.
- **Tests** : 6 fixtures (`]-2;3[`, `[-2;3]`, `]-inf;-2[ U ]3;+inf[`,
  `x in ]-2;3[`, `[0;1] - 2` reste une soustraction, `]0;-1[`), 599 → 605 ; gate
  `run-tests.ps1` vert ; `engine_diff.py` : 0 écart sur 17 695 entrées × 4 modes et
  1 242 compositions, parité C# = Rust.

## Validation post-fix

Dans Word : `x in ]-inf;-2[ U ]3;+inf[` se convertit en `x ∈ ]−∞ ; −2[ ∪ ]3 ; +∞[`.
