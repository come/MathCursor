# Fix — Un sup issu d'un postSign (0⁺) ne doit jamais s'orienter en chapeau

**Date :** 2026-06-22
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Lié à :** —

## Citation acté

> [capture : `Lim x 0+ ` → `\lim_{x\to 0} \hat{+}`] « peux tu expliquer ca ? (ce n'est pas une regression c'est lié je pense) » puis « oui je veux bien [le corriger] » — utilisateur, 2026-06-22

## Contexte

En tapant une limite à direction et en s'arrêtant pile après la borne, avec l'espace
final que la détection live ajoute : `lim x 0+ ` → `\lim_{x\to 0} \hat{+}` (« limite de
chapeau-de-plus »). Chaîne exacte :

1. Le lexer transforme un `+`/`-` collé-à-gauche et détaché-à-droite (espace, `)]`,`;`)
   en **exposant** (`Lexer.cs:289-293`) : `0+ ` → `0 ^ +` = `0⁺`. Voulu (`(0+)`, `lim x 0+ 1/x`).
2. Ce sup est poussé en `infix "^"` — **indistinguable** d'un `^` tapé. En découpe d'args
   n-aire (`Parser.cs:397`), un `^` en tête de span génère AUSSI une lecture **chapeau
   unaire** (`^` a `unary: "hat"`).
3. Limite incomplète (corps vide) → le parser coupe la borne `0` du `^` : `cible=0`,
   `corps=^+` → `\hat{+}` (coût 0), moins cher que `cible=0⁺, corps=□` (coût 3). La
   lecture absurde gagne.

Pré-existant, indépendant de la règle de paire de squelettes.

## Décision

Un sup **issu d'un postSign** est un signe d'exposant : il doit toujours avoir une base à
sa gauche, il ne peut **jamais** devenir un chapeau accent.

1. `Token.SignSup` (nouveau flag) posé sur le sup poussé en `Lexer.cs:291`.
2. `Parser.cs:397` : la dérivation unaire (chapeau) est **supprimée** quand le token de
   tête est `SignSup`.

Effet : `lim x 0+ ` → `\lim_{x\to 0^{+}} \square` (cohérent avec `lim x 0` → `\lim_{x\to 0} \square`),
car la seule lecture restante rattache `0⁺` en cible, corps vide. Un `^` réellement tapé
(`^a` → `\hat{a}`) et les unaires `+`/`-` (signe en tête de corps) sont intacts.

## Tradeoff & alternatives écartées

- **Pénalité Score sur le chapeau orphelin / chapeau d'un non-lettre** : déplace du coût,
  collatéral possible sur fixtures. Écartée — le fix structurel ne touche pas le Score.
- **Ne rien faire** : artefact transitoire mais visible et déroutant pendant la frappe.

## Conséquences

- **Code** : `Node.cs` (flag `Token.SignSup`), `Lexer.cs` (pose du flag), `Parser.cs`
  (garde sur la dérivation unaire). `Score.cs` : zéro.
- **Tests** : fixtures `lim x 0+ ` / `lim x 0- ` (espace final) → `\lim_{x\to 0^{+}} \square` /
  `\lim_{x\to 0^{-}} \square`. Non-régression `(0+)`, `lim x 0+ 1/x`, `(R*)`, `R*N`.
