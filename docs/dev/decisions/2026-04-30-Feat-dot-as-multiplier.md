# Feat — Le point `.` comme opérateur de multiplication (rendu `\cdot`)

**Date :** 2026-04-30
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Brief :** [`2026-04-30-dot-as-multiplier.md`](../briefs/2026-04-30-dot-as-multiplier.md)
**Brief frère :** [`2026-04-30-explicit-mult-times-vs-cdot.md`](../briefs/2026-04-30-explicit-mult-times-vs-cdot.md)

## Décision

Ajout du point `.` comme opérateur de multiplication explicite, en plus
de `*` et de la juxtaposition implicite. Spécificité : **`.` rend toujours
`\cdot`** (lecture littérale du point bas → centered dot), indépendamment
du setting culturel `GlobalOptions.MultSymbol`.

| Saisie utilisateur | Rendu LaTeX | Note |
|--------------------|-------------|------|
| `*` (étoile) | `\times` ou `\cdot` selon setting | Cf. ADR `Feat-explicit-mult-times-vs-cdot` |
| `.` (point) | `\cdot` (toujours) | Lecture littérale, non configurable |
| Juxtaposition `ab`, `2x` | rien (concaténation) | Cas standard inchangé |
| Juxtaposition `2 3` | symbole explicite (selon setting) | Fix bug ; cf. ADR frère |

`.` suit les **mêmes règles parser** que `*` (tightness, associativité,
flip alt) — l'utilisateur peut taper `a.b/3` (gauche-assoc PEMDAS) ou
`a .b/3` (droite-récursive) avec la même sémantique.

**Cascade de désambiguïsation** :

- **`RuleVecDotProduct`** étendue à `Bin(".")` : `u.v` propose `\vec{u}
  \cdot \vec{v}` en alt (idem `u*v`).
- **`RuleDecimalVsMultiplication` (NOUVELLE)** : pour le pattern
  `\d+\.\d+` (deux nombres séparés par `.`), propose l'alt décimal
  `n{,}m`. Permet aux utilisateurs anglo qui tapent `3.4` pour "trois
  virgule quatre" de switcher rapidement.

## Pourquoi

### Convention FR

En notation mathématique française, `.` est utilisé comme alternative à
`×` ou `*` pour la multiplication. C'est une frappe naturelle quand la
main est sur le pavé numérique. Devoir taper `×` (alt-x ou autre combo
clavier) ou `*` (shift) casse le flow.

### `.` toujours `\cdot`

L'utilisateur tape `.` parce qu'il veut **un point**. Le mapping littéral
`. → \cdot` est intuitif et prévisible. Le setting culturel
(`\times`/`\cdot`) ne doit affecter que `*` (le caractère "neutre" qui
n'a pas de représentation typographique évidente) — le `.` est déjà un
choix typographique de l'utilisateur.

Avantage : l'utilisateur a deux frappes pour deux symboles. `*` pour le
symbole de son setting, `.` pour `\cdot` quoi qu'il arrive. Plus de
flexibilité.

### AST distinct `Bin(".")` vs `Bin("*")`

Puisque `.` et `*` rendent différemment maintenant, l'AST DOIT les
distinguer. Solution la plus simple : étendre le champ `Op` de `Bin`
existant (`"*"` / `"."`), pas de nouveau type AST.

Le mode édition revert (Ctrl+E) utilise le **texte source brut** côté
adapter, pas le LaTeX rendu, donc le revert préserve la distinction sans
effort supplémentaire.

### Pourquoi mult par défaut pour `\d.\d`

Le brief acte la convention FR pure : `3.4` = `3 × 4`. L'alt cascade
décimal `3{,}4` permet aux utilisateurs anglo de switcher quand ils
tapent par habitude clavier numérique.

Tradeoff accepté : breaking change pour ceux habitués au point décimal.
Mitigation : popup propose l'alt en un clic, pas de re-saisie. Sticky
preference (V2) si l'usage justifie.

### Pas de support intervalle décimal

Avant ce brief, `[0.5, 1]` parsait avec `0.5` comme number décimal. Avec
le nouveau lexer, `0.5` devient `0 . 5` = mult. Le test
`Inter_keyword_renders_with_cap` a été adapté pour utiliser des bornes
entières (`[0,1] inter [1,2]`), évitant le cas pathologique.

Pour les utilisateurs qui veulent un intervalle décimal :
- Notation FR : `[0,5; 1]` (virgule décimale + point-virgule séparateur).
  Mais le parser actuel utilise `,` comme séparateur low/high — conflit.
- Workaround V1 : utiliser des bornes entières ou parenthéser.
- V2 : règle dédiée pour décimaux dans intervalles si demande.

## Conséquences

### Code (couche 1 — core)

- **`Vocabulary.cs`** :
  - `SingleOps` étendu avec `.`.
  - `TightOpChars` étendu avec `.` (bénéficie des règles tightness comme `*`).
- **`Lexer.cs`** : tokenisation Number ne consomme plus `.` (digits
  uniquement). `3.14` produit 3 tokens : `Number=3, Op=., Number=14`.
- **`Parser.cs`** : `ParseTerm` étend `IsOp("*", ".")` ; `Bin(opValue, …)`
  préserve la distinction `"*"` vs `"."` dans l'AST.
- **`LatexRenderer.cs`** : `Bin(".") → \cdot ` toujours (avant le test
  `*` qui dépend du setting).
- **`AlternativeGenerator.cs`** :
  - `RuleVecDotProduct` étendue à `Bin(".")`. Default rendu : `\cdot`
    (pas `MultSymbol` car `.` est toujours `\cdot`).
  - Nouvelle `RuleDecimalVsMultiplication` qui scan source pour
    `\d+\.\d+` et propose alt `n{,}m`. Priorité 3 (sémantique change).

### Tests

- **`LatexRendererTests`** : 8 tests pour le `.` (single, chain, paren,
  func, settings, tightness, loose). Constructor force
  `GlobalOptions.MultSymbol = "\\times "` pour déterminisme.
- **`LexerTests`** : `Number_consumes_digits_only_dot_is_op` adapté pour
  vérifier la nouvelle tokenisation.
- **`AlternativeGeneratorTests`** : 5 tests
  (`Dot_number_pair_proposes_decimal_alt`,
  `Dot_letter_pair_no_decimal_alt`, etc.).
- **`LatexToUnicodeMathTests`** : conversion `\cdot → ⋅` (U+22C5)
  pinée par `Multiplication_symbols_convert`.

### Régressions adressées

- `Inter_keyword_renders_with_cap` : utilise maintenant des bornes
  entières (`[0,1] inter [1,2]`) pour ne pas dépendre du décimal.

### Hors scope V1

- ❌ Préserver `\d.\d` comme number unique (= décimal anglo silencieux).
  L'alt cascade compense.
- ❌ Décimal en input via virgule. Le parser FR utilise déjà `,` comme
  séparateur low/high d'intervalle ; le décimal `0,5` créerait des
  conflits → reportée à V2.
- ❌ Distinction `.` vs `*` dans le mode édition. Le revert utilise le
  source brut, donc `a*b` reste `a*b` et `a.b` reste `a.b`.
- ❌ Configurabilité du rendu `.` (toujours `\cdot`).

## Validé par l'utilisateur

Demande initiale :

> "en attendant tu peux faire le brief multiplicateur aulieu de poiint,
> pour remplacer la notation a.b ou 3.4 par 3x4"

Précision règle (point littéral) :

> "alors ce que je veux c'est si il tape * => x ou . selon un settings
> (...) si il tape . c'est ."

Décisions sur les cas ratés :

> "1. peux tu forcer que vec * vec ou vec.vec est toujours un cdot ?"
> "4. revert recuperer le brut !"

Autorisation de coder :

> "oui et go dans la foulée sur les deux briefs"

## Statut

acté
