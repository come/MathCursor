# Feat — Whitespace = Sep réel + Pratt par tier + {body} greedy-anchor (P13)

**Date :** 2026-05-22
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-22-Feat-typed-slots.md](2026-05-22-Feat-typed-slots.md) (P12), [2026-05-22-Feat-engine-poc-isolation.md](2026-05-22-Feat-engine-poc-isolation.md) (P11)

## Citation acté

> « Brief complémentaire (delta v4 → v5) — migration collision + slots typés »
> [3 sections : whitespace=Sep, slots Pratt par tier, body greedy-anchor]
> + « ok go » + « tu me confirme que c'est des bonnes idées ? […] on
> abandonne la desambiguisation par petit bouton, a la place on doit avoir
> les choix les plus probables (2 presentés max + voir plus) dans la popup »
> — utilisateur, 2026-05-22

## Contexte

P12 livré avec heuristique `prev.End == current.Start` pour distinguer
"collé" vs "espacé". Le brief v5 §1 pointe la fragilité de cette
reconstruction depuis l'adjacence de kinds :
- `0 2` (= 2 atoms espacés) fusionnait parfois en `02`.
- `2x` (= produit implicite) ne marchait que par chance.
- L'info d'espace était reconstruite, pas portée.

Brief v5 propose 3 changements structurels :
1. **Sep tokenisé réel** pour le whitespace, conservé jusqu'au matcher.
2. **`{expr}` borné par tier de précédence** (Pratt min_bp), avec aliases
   sémantiques `{bound}` (= addsub), `{term}` (= muldiv), `{body}` (= greedy).
3. **`{body}` greedy-jusqu'à-ancre** pour les gros opérateurs, permettant
   la composition `lim f + lim g` et l'imbrication `lim x 0 sum k 1 n a`.

## Décision

### 1. Tokenizer émet Sep pour whitespace

Entre chaque paire de tokens séparés par whitespace dans la source, le
tokenizer émet un `Token { Kind=Sep, Text=" " }`. Pas de Sep en début/fin.

### 2. ShapeMatcher skip Sep entre parts

Avant chaque part de la shape, `SkipSep(tokens, ref ti)`. L'espace est
une boundary naturelle entre slots, jamais à l'intérieur d'un slot.

### 3. MatchExprPratt par tier

Nouveau `MatchExprPratt(tokens, maxTier, vocab)` :
- Consomme atoms, groupes, et opérateurs de `tier ≤ maxTier`.
- Stop sur `Sep`, `CloseDelim` top-level, ou opérateur de tier supérieur.
- 0 backtracking, O(n).

Aliases YAML :
```yaml
shape: "lim {var} {bound} {body}"
       # {bound} = expr:addsub, {body} = greedy-anchor
shape: "sum {var} =? {from:bound} {to:bound} {body}"
```

### 4. MatchBody greedy-jusqu'à-ancre

`MatchBody(tokens, vocab)` :
- Consomme tout : Sep absorbés, atoms, groupes, opérateurs.
- Stop sur `CloseDelim`, EOF, ou Word qui est une ancre (= dans
  `vocab.Anchors.Values`) **après au moins un opérande consommé**.
- Lookahead sur opérateurs : si suivi d'une ancre, l'op repart au niveau
  supérieur (= `f + lim g` = `(f) + (lim g)`).
- Imbrication : 1er opérande peut être une ancre (= `lim x 0 sum k 1 n a`).

### 5. StackParser interne : skip whitespace Sep

Le `StackParser.ParseExpression` traite désormais :
- Sep `,` ou `;` → boundary (= fin d'item dans une liste).
- Sep `" "` → skip (= whitespace interne consommé par le shapeMatcher).

### 6. LatexEmitter : pas d'espace autour de `+`/`-`

Convention math compact : `n+1` rendu collé, `=` `<` `>` `≤` `≥` `≠`
`⇒` `⇔` `∈` rendus espacés (= relations).

## Traces de référence (= golden cases P13)

| Source | Top LaTeX |
|---|---|
| `prod k 1 n+1 f(k)` | `\prod_{k=1}^{n+1} f(k)` |
| `lim x 0 2x+1` | `\lim_{x \to 0} 2x+1` |
| `lim x +oo f(x)` | `\lim_{x \to +oo} f(x)` |
| `sum k 1 n (1/k)` | `\sum_{k=1}^{n} \frac{1}{k}` |
| `lim x 0 1/x+1` | `\lim_{x \to 0} \frac{1}{x}+1` (= body greedy wide) |
| `lim x 0 f + lim x 1 g` | `\lim_{x \to 0} f` (= 2e ancre stoppe le body) |

## Tradeoff & alternatives écartées

- **Heuristique prev.End==current.Start** : leaky (= `02`, `2x` cassés).
  Rejeté par brief v5 §1.
- **Pas de Sep tokenisé, juste positions** : on a vu que ça reconstruit
  des frontières fragilement. Rejeté.
- **Body terminé par EOF strict** : casse la composition `lim f + lim g`.
  Rejeté brief v5 §3.
- **Body terminé au 1er espace** : casse `lim x 0 1/x+1` (= wide steno).
  Rejeté.
- **Backtracking** : rejeté doctrine §0 brief (O(n) non négociable).

## Conséquences

- **Code touché** :
  - `core-csharp/src/MathCursor.Engine/Tokenization/Tokenizer.cs` (= émission Sep)
  - `core-csharp/src/MathCursor.Engine/Rules/ShapeMatcher.cs` (= Pratt, MatchBody, aliases)
  - `core-csharp/src/MathCursor.Engine/Parsing/StackParser.cs` (= skip whitespace Sep)
  - `core-csharp/src/MathCursor.Engine/Emit/LatexEmitter.cs` (= `+`/`-` sans espace)
  - `data-v2/concepts/limites.yml` + `sommes.yml` (= aliases sémantiques)
- **Tests** : 81/81 engine verts. Tokenizer tests updated pour Sep counts.
- **API publique** : `SlotType` étendu avec `ExprAddsub`, `ExprMuldiv`,
  `ExprFuncpow`, `ExprComp`, `Body`.
- **Règles MC impactées** : aucune.

## Validation post-fix

- 81/81 engine verts.
- Trace de référence `lim x 0 f + lim x 1 g` → body=`f` (= 2e ancre stoppe).
- Test composition `lim x 0 1/x+1` → body wide `\frac{1}{x}+1`.
- Build VSTO inchangé.

## Plan en cours — état d'avancement

P13 — whitespace + Pratt + body :
- [x] P13.1 Tokenizer émet Sep
- [x] P13.2 ShapeMatcher skip Sep + MatchExpr stop Sep
- [x] P13.3 Pratt min_bp + aliases {bound} {term} {body}
- [x] P13.4 {body} greedy-jusqu'à-ancre + lookahead op→ancre
- [x] P13.5 Update YAML aliases sémantiques
- [x] P13.6 ADR (= ce document) + tests v5 référence

## Prochaine étape — P14 (popup IDE-style)

User citation : « on doit avoir les choix les plus probables (2 presentés
max + voir plus) dans la popup ». Mécanique Core déjà en place
(`EngineResult.Collisions` → `PatternCompletion[]`). Reste UI WPF :
`SuggestionPopupWindow` affiche max 2 candidats + bouton "voir plus" si > 2.
Estimé ~30 LOC dans le code-behind popup.
