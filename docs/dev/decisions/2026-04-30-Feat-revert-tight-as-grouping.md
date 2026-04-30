# Feat — Précédence math standard pour `/` collé (revert tight-as-grouping)

**Date :** 2026-04-30
**Kind :** Feat
**Température :** molle
**Statut :** retracté
**Supersedes :** [2026-04-29-Feat-tight-as-grouping.md](2026-04-29-Feat-tight-as-grouping.md)
**Superseded by :** [2026-04-30-Feat-tight-implicit-mult-grouping.md](2026-04-30-Feat-tight-implicit-mult-grouping.md)

## Décision

Revenir sur la règle "juxtaposition tight = groupement implicite" pour `/`,
introduite la veille (cf. ADR superseded). La précédence math standard est
restaurée pour la division. Les opérateurs `^` et `_` continuent à grouper
leur opérande droit via `ParseTightChain` (comportement antérieur à
l'ADR superseded, jamais remis en cause).

| Source | AST (retour standard) | Rendu LaTeX |
|--------|------------------------|-------------|
| `AB/BC` | `Bin("*", Bin("/", Bin("*", A, B), B), C)` | `\frac{AB}{B}C` |
| `1/x+1` (collé) | `Bin("+", Bin("/", 1, x), 1)` | `\frac{1}{x} + 1` |
| `1/x +1` (espace) | idem | idem |
| `A/B/C` (collé) | `Bin("/", Bin("/", A, B), C)` (gauche-assoc) | `\frac{\frac{A}{B}}{C}` |

`^` et `_` ne sont **pas affectés** par ce revert :

| Source | AST (inchangé) | Rendu LaTeX |
|--------|----------------|-------------|
| `u_n+1` (collé) | `Sub(u, Bin("+", n, 1, tight))` | `u_{n+1}` |
| `x^a+b` (collé) | `Sup(x, Bin("+", a, b, tight))` | `x^{a+b}` |

## Pourquoi

Après une journée d'usage de la règle tight-as-grouping, retour sur la
décision initiale : la règle divergeait de la convention math standard
(PEMDAS / précédence usuelle), ce qui crée une dissonance avec les attentes
des élèves et des profs. La précédence enseignée à l'école doit rester celle
qui s'applique dans MathCursor — sinon on enseigne implicitement une
sémantique non-standard, contraire à la mission produit (élève PAP en cours
de maths lycée).

Tradeoff accepté : `AB/BC` redevient `\frac{AB}{B} \cdot C` au lieu de
`\frac{AB}{BC}`. Pour grouper le dénominateur, l'utilisateur doit
parenthéser explicitement (`AB/(BC)`) — coût frappe minimal, mais cohérence
math préservée.

`^` et `_` continuent à grouper leur rhs en chaîne tight. C'est cohérent
avec la convention typographique : l'exposant et l'indice sont visuellement
groupés par leur position structurelle (élevé / abaissé), contrairement à la
division où la barre de fraction vient après lecture séquentielle des deux
opérandes.

## Conséquences

### Code (couche 1 — core)

- **Parser.cs `ParseTerm`** : suppression de la branche conditionnelle
  ajoutée le 29-04 (`if op.Value == "/" && op.Tight → ParseTightChain`).
  Retour à `ParsePostfix` systématique pour le rhs de `*` et `/`.
- **Parser.cs `ParsePostfix`** : aucun changement. `^` / `_` continuent à
  utiliser `ParseArgument → ParseTightChain`.
- **LatexRenderer.cs** : aucun changement.

### Tests

- Suppression des tests propres au tight-grouping `/` :
  `Tight_slash_AB_over_BC_groups_both_sides`,
  `Tight_slash_1_over_x_plus_1_absorbs_addition`,
  `Tight_slash_chain_is_right_associative`,
  `Render_AB_over_BC`, `Render_1_over_x_plus_1_tight`.
- Conservation des tests qui pinent `^` et `_` (comportement hors revert) :
  `Tight_underscore_groups_subscript`, `Tight_caret_groups_exponent`,
  `Render_tight_subscript_grouped`, `Render_tight_exponent_grouped`.
- Conservation des tests non-régression `Loose_slash_then_loose_plus_*`,
  `Slash_between_groups_keeps_groups` (toujours valides post-revert).
- Ajout d'un test pin standard : `1/x+1` (collé) →
  `Bin("+", Bin("/", 1, x), 1)`, rendu `\frac{1}{x}+1`.

## Validé par l'utilisateur

Retour spontané après usage :

> "+ je pense m'etre trompé avec ma regle de espace/tight c'est pas
> mathématiques et ca va perturber tout .. fais moi un plan"

Validation du plan de revert + format ADR :

> "ok, tout propre stp / P2 refactor supersedes"

## Statut

retracté — après quelques heures d'usage, l'utilisateur a trouvé le revert
trop strict (`AB/BC` → `\frac{AB}{B}·C` casse la fluidité sténo). La
nouvelle ADR (cf. `Superseded by`) apporte une nuance : la mult implicite
groupe (juxtaposition = unité), les ops explicites cassent la chaîne
(PEMDAS), et l'élargissement complet reste accessible via cascade.
