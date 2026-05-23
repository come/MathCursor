# Feat — Slots typés `{var}` `{const}` `{expr}` + quantificateurs (P12)

**Date :** 2026-05-22
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-22-Feat-engine-poc-isolation.md](2026-05-22-Feat-engine-poc-isolation.md) (P11)

## Citation acté

> « ca marche assez moyennement des qu'on sort des clous. prod k 1 n+1 f(k)
> n'est pas reconnu par exemple. […] j'avais imaginé un systeme de type de
> pattern genre {expr} qui est une combinatoire de {var} et d'{operandes}
> par exemple.. et du coup le pattern lim s'ecrirait aussi sous forme de
> lim {expr} etc avec des opérateur un peu type regexp: ? .. tu me confirmes
> que c'est pas encore en place ca ? » + « ok go » — utilisateur, 2026-05-22

Choix validés via `AskUserQuestion` :
- **Boundary `{expr}`** : Pratt typé par précédence (= heuristique token-run en POC)
- **Quantificateurs** : `?`, `*`, `+`
- **Types de slots** : 3 (= `{var}`, `{const}`, `{expr}`)

## Contexte

P11 livré avec un `$slot` minimaliste : mono-token sauf si dernier (= greedy)
ou groupe parenthésé. Cas user limites :
- `prod k 1 n+1 f(k)` ne match pas (= `n+1` consommé comme 1 token).
- Workaround : parens obligatoires → `prod k 1 (n+1) f(k)`. UX dégradée.

Objectif P12 : permettre à l'user d'écrire des expressions composées
sans parens, tant que la frontière entre slots reste détectable.

## Décision

### 1. Slots typés (= 3 types)

```yaml
shape: "lim <filler>? {var} <to>? {bound:expr} {body:expr}"
```

- `{var}` — 1 token `Word` identifier (= variables k, x, h, …).
- `{const}` — 1 token `Number` (= 0, 42, 3.14).
- `{expr}` — séquence d'atomes + opérateurs bornée par heuristique token-run.

Syntaxe :
- `{type}` raccourci (= nom == type) → emit ref `$type`.
- `{name:type}` explicite → emit ref `$name`.
- Référencement positionnel automatique : `$1`, `$2`, … = slots typés
  dans l'ordre de la shape (= utile pour shapes avec slots anonymes).

### 2. Heuristique `{expr}` (0 backtracking, O(n))

| Token courant | Action |
|---|---|
| `Symbol` / `Glue` (= `+`, `-`, `*`, `->`, `=`, …) | toujours pris, marque "op à gauche" |
| `Number` / `Word` après "op à gauche" OU `isLast` (greedy) | pris |
| `Number` / `Word` après atome (= `lastWasOpOrOpen=false`) | pris **uniquement si collé** au précédent (= `prev.End == current.Start`, = produit implicite `2n`). Sinon = nouveau slot, stop. |
| `OpenDelim` après "op à gauche" OU `isLast` | consomme tout le groupe |
| `OpenDelim` après atome (= `n(`) | stop (= nouveau slot) sauf si `isLast` |
| `Sep` / `CloseDelim` top-level | stop |

**Espace ≡ frontière** : `1 n` (avec espace) = 2 slots. `2n` (collé) = produit
implicite. La position source des tokens (`Token.Start`/`Token.End`) discrimine.

### 3. Quantificateurs

- `?` (= déjà P11) optionnel.
- `*` zéro-ou-plus répétitions.
- `+` un-ou-plus (≥ 1 requis).

S'appliquent à n'importe quel type de part (`{expr}*`, `<to>?`, `=?`, etc.).

### 4. Rendu slot par concat brut (sauf `/`)

`{expr}` est rendu en **concatant les Token.Text bruts** par défaut. Préserve
la fidélité au source : `n+1` reste `n+1` (pas `n + 1`).

Exception : si le slot contient `/` (= Symbol), on re-parse + re-emit via
`StackParser` + `LatexEmitter` pour produire `\frac{a}{b}`.

### 5. Backward-compat P11

Le `$slot` historique reste fonctionnel (= shape mixte autorisée). Les
règles peuvent être migrées progressivement.

## Tradeoff & alternatives écartées

- **Pratt classique avec stack de précédences** : plus puissant mais demande
  un parser séparé. POC v2 vise simplicité — heuristique token-run suffit
  pour 90 % des cas math collège+prépa.
- **Backtracking longest-then-shrink** : O(n²) worst case, complexe à
  debugger. Rejeté par doctrine §1 du brief v4 (= O(n) non négociable).
- **Auto-detect produit implicite via espace** : retenu (= `2n` collé OK,
  `1 n` séparé stop). Coût : l'user doit comprendre que collé/séparé est
  significatif. C'est cohérent avec le brief §1.1.
- **`{slot kind:expr stop:eol}` syntaxe verbose** : rejetée pour 3 types
  de base, retenu pour extensions futures.

## Conséquences

- **Code touché** :
  - `core-csharp/src/MathCursor.Engine/Rules/ShapeMatcher.cs` étendu (~150 LOC)
  - `core-csharp/src/MathCursor.Engine/Emit/TemplateEmitter.cs` (= concat brut + $N positionnel)
  - `data-v2/concepts/limites.yml` + `sommes.yml` migrés vers slots typés
- **Tests** : 80/80 engine verts, dont 12 golden cases au lieu de 6.
- **Couverture nouvelle** : `prod k 1 n+1 f(k)`, `sum k 0 2n+3 g(k)`,
  `prod i 0 N (1-x_i)`, `lim x 0 2x+1`, `lim x +oo f(x)`.
- **API publique** : `SlotType` enum exposé (Var, Const, Expr). `ShapePart`
  reste internal.
- **Règles MC impactées** : aucune.

## Validation post-fix

- 80/80 engine tests verts (= +6 vs P11).
- Cas user `prod k 1 n+1 f(k)` → `\prod_{k=1}^{n+1} f(k)` ✓.
- Non-régression Core 1266+ adapter 393 attendue (= P11 inchangé).
- Validation manuelle Word avec feature flag actif (= cf.
  [`engine-poc-test-scenario.md`](../engine-poc-test-scenario.md)).

## Plan en cours — état d'avancement

P12 — Slots typés :
- [x] P12.1 ShapePart typed + parseur shape `{type}`
- [x] P12.2 MatchExpr heuristique token-run
- [x] P12.3 MatchVar + MatchConst mono-token
- [x] P12.4 Quantificateurs `*` `+`
- [x] P12.5 Référencement positionnel `$1 $2 $N`
- [x] P12.6 Update YAML limites + sommes
- [x] P12.7 ADR (= ce document)
