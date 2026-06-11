# Fix — Unité vs frontière d'arguments n-aire : l'espace séparateur prime

**Date :** 2026-06-11
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-11-Feat-nary-arity-variants.md](2026-06-11-Feat-nary-arity-variants.md) (formes courtes lim/sum), table des unités `Units.cs`

## Citation acté

> « faut que les espaces de séparation prenne le pas sur les unités dans le cas d'une fonction non ? » — utilisateur, 2026-06-11
> « commit ça » — utilisateur, 2026-06-11

## Contexte

**Mega bug signalé** : `lim x 0 g(x)` → popup à 3 candidats tous faux
(`\lim_{x\to 0\,\mathrm{g}(x)} □`…), la bonne lecture `\lim_{x\to 0} g(x)`
absente du forest. Idem `m(x)`, `h(x)`, et `sum x 0 1 g*x`.

Cause : `g`, `m`, `h`… sont des mots-unités (gramme, mètre, heure). La règle de
jonction `num + unit` (Lexer.cs, rôle `unitOp`) est **CrossSpace** — voulu pour
que « 5 g », « 1 cm » se lisent comme quantités même tapés avec espace. Elle
insère un token infixe `·unit` sticky entre `0` et `g`, qui :

1. **bloque la frontière d'arguments du n-aire** : le découpage `[x | 0 | g(x)]`
   devient impossible dans `Splits` (un span ne peut pas commencer par un token
   infixe hors début d'entrée) — la bonne lecture n'existe même pas, ce n'est
   pas un problème de score ;
2. ne laisse que des lectures quantité absurdes (`0` grammes dans la borne).

## Décision

**L'espace séparateur d'arguments prend le pas sur le collage unité, comme
lecture alternative.** Au découpage des arguments d'un n-aire (`Splits`,
Parser.cs), une frontière d'argument peut tomber sur un token `·unit` **créé à
travers un espace** (`Spaced = true`) et le faire céder : la découpe qui saute
ce token est ajoutée au forest. Les deux lectures (quantité vs frontière d'arg)
coexistent, le Score tranche.

Le collage tapé soudé (« 5g ») reste inconditionnel, et le collage espacé reste
la seule lecture hors contexte n-aire (« 2 g(x) » seul → quantité).

## Tradeoff & alternatives écartées

- **Garde lexer « unité suivie de `(` collée = fonction »** (proto 1) : corrige
  `g(x)` mais rate `sum x 0 1 g*x` (cas utilisateur) et tout autre opérateur
  après l'unité. Symptôme, pas structure — la notion d'argument n'existe qu'au
  parser.
- **Supprimer CrossSpace sur `num + unit`** (proto 2, « que le 1 g ne déclenche
  pas un espace bizarre ») : mesuré → tue toutes les quantités espacées
  (`1 cm`, `2 cm + 3 cm`, `4,5 cm`, `5 m/s`, `10 km/h` → erreur, 3 tests
  cassés). Or « 5 m/s » avec espace est la frappe naturelle d'un élève en
  physique. Trop destructif.

## Conséquences

- **Code touché** : `engine/src/MathCursor.Engine/Parser.cs` — `Splits()`,
  ~10 lignes (découpe alternative sautant un `·unit` spaced à la frontière).
- **Tests** : 3 fixtures ajoutées (`lim x 0 g(x)`, `sum x 0 1 g*x`,
  `lim x 0 m(x)` → auto), compteur 380 → 383. Suite complète verte 21/21,
  zéro régression sur les 380 fixtures existantes.
- **API publique** : aucune.
- **Limites résiduelles connues** : `2 g(x)` hors n-aire reste lu
  `2\,\mathrm{g}(x)` ; `lim x 0 g x` reste `\lim_{x\to 0\,\mathrm{g}} x` (le
  corps « g x » sans opérateur n'est pas parsable — l'équivalent `f` donne
  erreur). À rouvrir si ça mord en usage réel.
- **Hors sujet découvert** : `int 0 1 f(x) dx` rend `\, ddx` (un « d » en
  trop) — préexistant, indépendant, à traiter séparément.

## Validation post-fix

`lim x 0 g(x)` → **auto** `\lim_{x\to 0} g(x)` ; `sum x 0 1 g*x` → **auto**
`\sum_{x=0}^{1} g\times x` ; `5 g`, `1 cm`, `2 cm + 3 cm`, `5 m/s` inchangés.
Rejoué à chaque build par les 3 pipelines fixtures (moteur, OMML, popup) +
mutations de tolérance.
