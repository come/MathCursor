# Feat — Espace séparant deux propositions : fallback de parse (étiquette `\quad` + produit fraction×facteur)

**Date :** 2026-07-05
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Lié à :** `engine/src/MathCursor.Engine/Parser.cs` + `ForestEngine.cs`,
`rust/mc-engine/src/parser.rs` + `engine.rs`, `data/engine/symbols.json` (`·gap`),
`rust/mc-engine/src/starmath.rs`, `serialization/.../LatexToOmml.cs` (\quad),
`adapter-vsto/.../UI/WpfMathAdapter.cs` (\quad),
`engine/tests/.../fixtures.json`

## Citation acté

> « soit l'equation (E) ax2+by+c = 0 aujourd'hui ca plante completement » … « si un
> truc est en erreur à cause d'un espace, on traite deux propositions dans l'algo et on
> garde la separation » … « la multiplication implicite entre une fraction et un
> scalaire » … « int cos sum etc sont egalement des scalaires » … « (x 1/2) …
> clairement ca devrait pas arriver ! il ne faut pas » — utilisateur, 2026-07-05.

## Contexte

Un **espace** entre deux expressions supprime la jonction implicite du lexer (règle
`close/name → name/open`, sans `CrossSpace`) → trou opératoire → **0 lecture** →
« erreur ». Cas voulus : l'**étiquette** `(E) ax2+by+c=0` (une équation nommée) et le
**produit** `x b/a` (coefficient × fraction). `(E)` n'était qu'un cas particulier d'une
règle générale : *espace non résolu → lire les deux côtés séparément*.

## Décision

**Fallback de parse**, déclenché UNIQUEMENT si (1) le span entier n'a **aucune** lecture
(vrai fallback, zéro impact sur l'existant) et (2) au **vrai top-level** — jamais dans un
intérieur de parenthèses (flag `allowSpaceFallback`, faux via `OnGroup` : `(x 1/2)` ne
doit PAS devenir `(x\frac{1}{2})`). Couper à un espace de profondeur 0, deux tiers :

- **(A) Étiquette** : groupe parenthésé **fermé à intérieur atomique** en tête + espace
  → `(groupe)\quad <reste>` (infixe `·gap`, rendu `{0}\quad {1}`, StarMath `{0} ~ {1}`).
  `(E) f(x)=1/x` → `(E)\quad f(x)=\frac{1}{x}`. Garde anti-prose (droite ≠ mot nu).
- **(B) Produit fraction × facteur** : un côté est une **fraction** (`/`), l'autre un
  **facteur** = atome nu **ou** application d'opérateur (préfixe `cos`/`sin`/`ln`,
  n-aire `int`/`sum`/`lim`, postfixe `n!`) → **produit implicite collé** (symbole `*`,
  `renderImplicit` `{0}{1}`). `x b/a` → `x\frac{b}{a}`, `1/x cos x` →
  `\frac{1}{x}\cos(x)`, `2 1/2` → `2\frac{1}{2}` (nombre mixte), `1/2 x=0` →
  `\frac{1}{2}x=0` (½x=0, via le membre gauche de la relation).

**Garde-fous** : `2 x`, prose « soit le triangle » → aucun côté structuré → **erreur**
(voulu, fixtures). Le cas `()` étiquette **ne change pas**. Fraction × fraction hors
scope (le facteur exclut la fraction). Parenthèses = tier A seulement (pas de doublon).

Deux fixes d'affichage du `\quad` : **OMML** (`\quad`/`\qquad` → cadratin U+2003, sinon
Word écrivait « quad ») ; **aperçu WpfMath** (WpfMath 2.1 ne connaît pas `\quad` →
barre rouge → dégradé en `\,`).

## Tradeoff & alternatives écartées

- **Split universel à N'IMPORTE quel espace** : cassait 3 fixtures (`2 x`→`2\quad x`,
  et via réentrance `_onGroup` `(1\quad 2)` / `a-(2\quad c)`). Le garde « au moins un
  côté structuré (parenthèse OU fraction) » + « jamais dans une parenthèse » rendent la
  règle sûre.
- **`1/2 x=0` → séparation `\frac{1}{2}\quad x=0`** (proposé) : écarté — `\frac{1}{2}x=0`
  (½x=0) est la lecture naturelle, obtenue « gratuitement » car la relation est coupée
  avant le fallback (membre gauche `1/2 x` → produit).
- **Facteur = atome nu seulement** : trop étroit — un coefficient fractionnaire devant
  une intégrale/fonction est courant (`1/2 int f dx`). Élargi aux applications
  d'opérateurs.
- **Jonction implicite `CrossSpace`** (espace = multiplication) : `(E)·(…)`,
  mathématiquement faux pour une étiquette. Écarté.

## Conséquences

- Parité **C#/Rust** (même `symbols.json`, logique miroir), gate **fixtures 481/481**
  (C# `FixtureTests` + conformance Rust). Fixtures ajoutées : mid/sachant/given, `(E)`
  label, `x b/a`, `1/2 x`, `2 1/2`, `1/x cos x`, `1/2 x=0`, `(x 1/2)` (anti-régression
  « pas de gap dans les parenthèses »).
- `(x+1) y` (parenthèse à intérieur composé, pas un atome-étiquette ni fraction×facteur)
  → **erreur** (ni `\quad` ni produit ; group×scalaire hors scope).
- WASM rebuild ; **add-in VSTO à rebuild** (les 3 couches sont compilées dedans).

## Validation

`dotnet test` engine + serialization + adapter (net48) verts · `cargo` conformance
**481/481** · binaire `analyze` : cas produit/étiquette OK, `(x 1/2)` = matrice (pas de
gap), `2 x` = erreur.
