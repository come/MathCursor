# Fix — `lim`/`sup`/… : opérande n-aire `▒` pour ne pas happer le 1er token

**Date :** 2026-06-02
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** `LatexToUnicodeMath`, mémoire `reference_office_2019_omath_limits` (bug de rendu = valider en Word)

## Citation acté

> « y'a une cause racine, y'a aucune raison que ca rende ca alors que le latex est bon » puis « tente la correction » — utilisateur, 2026-06-02

(Observé en Word : `Lim x 0 1/x+1`, collision `\lim_{x \to 0} \frac{1}{x+1}` choisie → rendu `\frac{\lim_{x\to0} 1}{x+1}` — le `lim` happé dans le numérateur.)

## Contexte

Les logs confirment : moteur OK (`top="\lim_{x \to 0} \frac{1}{x}+1"`,
collision `"\lim_{x \to 0} \frac{1}{x+1}"`), `InsertOMathAt: unicodeMath="lim_(x → 0) 1/(x+1)"`,
mais Word rend `\frac{\lim 1}{x+1}`.

**Cause racine** (spec UnicodeMath / Unicode TN28) : `lim` est un opérateur
n-aire rendu en lettres. Sans marqueur, Word prend « la première expression
simple qui suit » comme opérande → `lim_(x→0)` happe `1`, puis `/` en fait le
numérateur. Le LaTeX `\frac{1}{x+1}` rendait le groupement explicite ; la
conversion en `/` linéaire le perd, et Word re-parse avec sa précédence
(fonction > fraction). `∑`/`∫` n'ont pas le souci car leur glyphe est
auto-reconnu n-aire.

## Décision

Dans `LatexToUnicodeMath`, dispatcher les opérateurs-limite rendus en lettres
(`lim`, `limsup`, `liminf`, `sup`, `inf`, `max`, `min`) : émettre l'opérateur,
son indice `_{...}`, puis le **n-aryand `▒` (U+2592)** avant l'opérande. `▒`
force Word à prendre TOUTE l'expression suivante comme opérande de l'opérateur.

```
\lim_{x \to 0} \frac{1}{x+1}  →  lim_(x → 0)▒1/(x+1)
```

Si aucun opérande ne suit (ex. `\lim_{x \to 0}` seul), pas de `▒`.

## Tradeoff & alternatives écartées

- **Parenthéser la fraction** (`lim_(x→0) (1/(x+1))`) : rejeté — parenthèses
  visibles, et `lim` happe quand même `(...)`. Testé en Word par l'utilisateur : KO.
- **Function-application U+2061 / crochets invisibles `〖〗`** : testés en Word : KO.
- **`▒` (n-aryand)** : la mécanique OFFICIELLE prévue par la spec pour ce cas.

## Conséquences

- **Code touché** : `MathCursor.Core/LatexToUnicodeMath.cs` (dispatch
  limit-operators + set `LimitOperators`).
- **Tests** : `LatexToUnicodeMathTests` (cas `lim … = 1/…`) + e2e adapter
  (`lim x->0 f(x)`) mis à jour pour l'attendu `▒`. Engine 166, Adapter 56.
- **Limite** : ce bug est un RENDU Word — la validation finale est dans Word
  (xUnit confirme seulement l'UnicodeMath produit). À valider par l'utilisateur.

## Validation post-fix

UnicodeMath produit : `lim_(x → 0)▒1/(x+1)`. À confirmer en Word : `lim`
au-dessus, fraction `1/(x+1)` comme opérande (plus de fraction-de-lim).

## Note — état des tests legacy Core

`MathCursor.Core.Tests` a **89 échecs pré-existants** (templates `Patterns.Templates.*`
de l'ancien moteur `[Obsolete]`, sans rapport avec ce fix — baseline mesurée
en stashant le changement). Non traités ici : legacy en voie de suppression.
