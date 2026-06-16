# Feat — Parenthèses tapées conservées autour d'un atome/indice (généralise les point-pairs)

**Date :** 2026-06-16
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** [2026-06-10-Feat-geo-point-pairs.md](2026-06-10-Feat-geo-point-pairs.md)

## Citation acté

> « (U_n) doit garder les parenthèses (comme (AB)) on en avait parlé […] » — utilisateur, 2026-06-16. Choix de cadrage (plan validé) : périmètre = **atome ou indice** ; **unifier** (remplacer le cas spécial point-pairs) ; **auto, garde direct** (pas d'alternative sans-parenthèses).

## Contexte

Un délimiteur tapé autour d'une expression « visuellement atomique » porte une intention de notation, pas du groupement de précédence. `(U_n)` rendait `U_{n}` (parenthèses perdues) ; seuls `(AB)`/`[AB]` les gardaient, via le cas spécial géométrie `IsPointPair` (ADR superseded). On généralise et on unifie.

## Décision

Règle générale : **un groupe délimité tapé se conserve au rendu si son intérieur est un ATOME ou un INDICE (`_`), sauf s'il est consommé par un opérateur qui regroupe déjà** (`Bracketed`, ex. `/`).

- Nouveau type de nœud `"paren"` (wrapper, `Parts[0]` = intérieur, `Lb`/`Rb` pour `[AB]`), famille structurelle (tuple/set/interval).
- Prédicat `KeepEligible` (features only) : atome non-trou, OU infixe avec feature `Sub`. Exclut exposants (`(x^2)`, et les modificateurs d'ensemble `(R*)`/`(R+)`/`(0+)` lexés en `^` → parenthèses supprimées comme avant), fonctions (`(cos x)`), opérateurs lâches (`(x+1)`).
- **Émission kept-only** (décision « auto, garde direct ») : un groupe keep-eligible n'a QUE la lecture gardée → `(U_n)` → AUTO `(U_{n})`. Pas de lecture dissoute → pas d'hybride → aucun terme de cohérence (on supprime le `Coh="geo"` du cas spécial).
- **Dissolution** uniquement au rendu sous parent `Bracketed` : `Child()` déballe un `paren` → `(U_n)/2` → `\frac{U_{n}}{2}`. Sous un opérateur non-regroupant (`+`, apply), les parenthèses restent (`(U_n)+1`, `f(x)`).

## Conséquences

- **Code** : `Parser.cs` (helper `KeepEligible`, réécriture du bloc `(…)`, bloc `[…]` keep-eligible, suppression `IsPointPair`/cas spécial `[AB]` ; `IsRepere` inchangé), `LatexRenderer.cs` (`case "paren"` + déballage dans `Child` sous `Bracketed`), `Score.cs` (`Shape()` : `?? n.Type` au lieu de `?? "mat"`, défensif), `Node.cs` (commentaire de types).
- **Comportement changé (assumé)** : `(AB) // (CD)` et `(AB) perp (CD)` passent de popup [avec/sans parenthèses] à **auto** avec parenthèses (on perd l'alternative sans-parenthèses — c'est l'esprit « garde mes parenthèses », et ça aligne `()` sur `[]` qui était déjà auto). `(a (b) c)` : la cellule `(b)` garde ses parenthèses.
- **Inchangé** : `[AB]`, `(R*)`/`(R+)`/`(0+)`, `(cos x)`, `(1/2)x`, `(n+1)!`, `2(x+1)`, tuples/repère, `f(x)`, `f(k+1)/2`.
- **Fixtures** : ajouts `(U_n)`, `(a_i)`, `(x)`, `(AB)` (+ blindage `(x^2)`, `(x+1)/(x+2)`) ; régénérations `(AB) // (CD)`, `(AB) perp (CD)`, `(a (b) c)`.

## Validation post-feat

`dotnet test` engine (corpus + nouvelles entrées) + serialization (`OmmlCoverageTests` rejoue les nouveaux `(…)` → aucun backslash résiduel).
