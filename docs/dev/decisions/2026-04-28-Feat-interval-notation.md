# Feat — Notation intervalle française au clavier

**Date :** 2026-04-28
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Reconnaître au parser les **intervalles à la française** :

| Source | Sémantique | Rendu |
|--------|------------|-------|
| `[a,b]` | fermé fermé | `[a,b]` |
| `[a,b[` | fermé ouvert | `[a,b[` |
| `]a,b]` | ouvert fermé | `]a,b]` |
| `]a,b[` | ouvert ouvert | `]a,b[` |

Nouveau nœud AST `Interval(low, high, leftClosed, rightClosed)`. Le parser
remplace l'interprétation actuelle où `[0,1]` était un `Group` contenant le
binop virgule. Désormais `[0,1]` produit toujours un `Interval`.

Le renderer émet les brackets **bruts** (`[`, `]`), sans `\left` / `\right` —
parce que `\left]` et `\right[` (brackets « inverses » nécessaires pour les
intervalles ouverts) ne sont pas universellement supportés (WpfMath, KaTeX,
Word OMath ont des comportements variables). Les brackets bruts rendent
correctement dans Word et WpfMath pour les cas typiques (intervalles à bornes
numériques ou identifiants courts).

## Pourquoi

### Convention lycée française

La notation `]a,b[` (= intervalle ouvert) est l'écriture canonique en lycée
français. La notation anglo-saxonne équivalente `(a,b)` est ambigüe avec un
couple ordonné, donc le programme français privilégie les crochets retournés.

L'utilisateur (élève PAP cible) tape `[0,1[` au clavier comme il l'écrirait
au tableau. L'add-in doit le rendre tel quel.

### Pourquoi remplacer Group, pas coexister

Aujourd'hui `[0,1]` est parsé comme `Group(Bin(",", 0, 1))` → rendu
`\left[0,1\right]` (par le code Group qui utilise `\left[` / `\right]` quand
le délimiteur d'origine est `[`). Cohabitation possible mais source de
confusion : l'AST n'expriment pas la sémantique « ce sont des bornes
d'intervalle, pas un couple parenthésé ». Les futures features (test
d'appartenance `x ∈ [0,1]`, union `[0,1[ ∪ [3,5]`) ont besoin du concept
Interval comme nœud distinct.

### Pourquoi pas `\left`/`\right`

`\left]` (bracket inverse à gauche) est la commande LaTeX strictement correcte
pour le délimiteur d'un intervalle ouvert à gauche. Mais :
- WpfMath (popup) ne supporte pas tous les délimiteurs inversés
- Word OMath BuildUp parse `\left]` de façon imprévisible
- Pour les cas typiques (intervalles à bornes simples), les brackets bruts
  rendent visuellement bien

Compromis : on émet `[`, `]` directement. Si plus tard on veut auto-grossir
les brackets pour un intervalle qui contient une fraction empilée, on fera
un `\left[` / `\right]` conditionnel à `LeftClosed && RightClosed` (le seul
cas standard). Hors scope.

## Conséquences

### Code (couche 1 — core)

- **AstNodes.cs** : nouveau `Interval { Low, High, LeftClosed, RightClosed }`
- **Parser.cs** : `ParsePrimary` reconnaît `[` ou `]` au début comme intervalle.
  Consume bracket → low (ParseExpr) → `,` → high (ParseExpr) → bracket
  fermant (`[` ou `]`). Args manquants → `Hole`.
- **LatexRenderer.cs** : `Interval => "{leftBr}{low},{high}{rightBr}"` avec
  leftBr/rightBr ∈ {`[`, `]`}.

### Tests

- Parser : 4 patterns (`[a,b]`, `[a,b[`, `]a,b]`, `]a,b[`) → AST Interval
- Renderer : 4 patterns → string littérale brackets
- Régression : `(a,b)` reste un Group avec `,` (les parens classiques inchangées)
- Pipeline : `[0,1]` → `[0,1]`, `f([0,1])` → `f\left([0,1]\right)` (parens
  externes intactes), `forall x [0,1] x ≥ 0` → `\forall x \in [0,1] x \geq 0`
  (intervalle dans le set d'un quantif)

### Hors scope

- **Union `U` / intersection `inter`** entre intervalles : briefs ultérieurs.
- **Bornes infinies** : `]-inf,1]` marche déjà tel quel (`-inf` → `-\infty`
  via Unary + Const, intervalle l'enveloppe).
- **Auto-grossissement des brackets** (`\left[ \right]` conditionnel pour
  intervalles complexes). À traiter si on rencontre un cas qui rend mal.

## Validé par l'utilisateur

Direction et plan :
> "ok ca ira, on peut passer aux intervalles ?"
> "[0,1] tu remplace par l'intervalle"

## Statut

acté
