# Refactor — Anchor unifié `KEYWORD args` ≡ `KEYWORD(args)` ≡ `KEYWORD(args,...)`

**Date :** 2026-05-26
**Kind :** Refactor
**Température :** forte
**Statut :** proposé
**Supersedes :** —
**Lié à :**
- ADRs Phase D (RewriteEngine).
- Brief 2026-05-26 `Refactor-trig-rules-deferred`.

## Citation

> « en fait assez globalement : KEYWORD expr expr <=> KEYWORD(expr expr) <=> KEYWORD(expr, expr) non ? » — utilisateur, 2026-05-26

## Contexte

Aujourd'hui, chaque règle YAML force une seule syntaxe d'appel :
- `frac {num} {den}` → l'user doit taper `frac 1 2`, pas `frac(1, 2)`.
- `lim {var} {bound} {body}` → `lim x 0 f(x)`, pas `lim(x, 0, f(x))`.

L'utilisateur veut **liberté de style** : les 3 formes doivent être équivalentes au matching.

## Décision

Étendre le `RewriteMatcher` pour reconnaître automatiquement les 3 formes d'appel des règles avec **anchor literal**. Aucun changement YAML.

### Détection de l'anchor

Lors du chargement d'une `RewriteRule`, si le 1er `PatternElement` est un `Literal` de longueur ≥3 chars (= mot, pas symbole), il est marqué comme **anchor** automatiquement.

Exemples auto-classés anchor :
- `frac`, `lim`, `sum`, `prod`, `int`, `iint`, `norm`, `vec`, `cos`, `sin`, `sqrt`, `congru`, `forall`, `exists`, `derive`.

Exemples non-anchor (= 1er literal court ou symbole) :
- `=`, `+`, `(`, `<` — primitives binaires.

### Matcher étendu

Quand le pattern commence par un anchor :

```
match(anchor) :
    consume anchor literal
    scope_parens = false
    if next item is "(" :
        consume "("
        scope_parens = true
    for each remaining slot in pattern :
        if scope_parens : skip "," and " " between slots
        match slot (= consume 1 Item Expr)
    if scope_parens :
        skip "," and " "
        consume ")"
```

### Convention

- `KEYWORD a b` (= sans parens, espaces) — forme courte.
- `KEYWORD(a b)` (= parens, espaces) — verbeux mais bien délimité.
- `KEYWORD(a, b)` (= parens, virgules) — style classique math.

Les 3 produisent le même match.

## Tradeoff & alternatives écartées

- **Dupliquer chaque règle 3 fois** : rejetée (= explosion combinatoire).
- **Règle générique phase 0 `( {args:expr...} ) → strip parens`** : rejetée (= casserait `(0,1)` qui peut être un tuple voulu, et `f(x)` qui n'est pas un anchor).
- **Modifier le YAML pour autoriser parens optionnelles `frac(? {num} ,? {den} )?`** : rejetée (= rend le YAML illisible, et c'est le rôle du matcher pas du format).

## Conséquences

- **Code** : ~30 LOC dans `RewriteMatcher.cs`. Détection automatique de l'anchor sur 1er Literal ≥3 chars. Switch entre 3 modes de scope dans la boucle slots.
- **Tests** : ajouter ~15 cas pour valider les 3 formes pour `frac`, `lim`, `sum`, `norm`, `sin`, etc.
- **API publique** : aucune.
- **YAML** : aucune modification.

## Validation post-fix

1. `frac 1 2` ≡ `frac(1 2)` ≡ `frac(1, 2)` → `\frac{1}{2}` (3 cas, même output).
2. `lim x 0 f(x)` ≡ `lim(x, 0, f(x))` → `\lim_{x \to 0} f(x)`.
3. `f(x)` reste function-call (= `f` est Letter, pas anchor literal ≥3 chars).
4. `(0,1)` reste tuple/intervalle (= pas de keyword anchor avant).

## Quand reprendre ce brief

- Lors de la **bascule prod Phase D-6**. Cette extension fait partie naturellement de la finalisation du RewriteEngine en moteur principal.
- Peut être implémentée AVANT la bascule (= comme amélioration POC) si désiré.

## Plan en cours — état d'avancement

| # | Chantier                                  | Statut |
|---|------------------------------------------|--------|
| 1 | hardcoded FR → YAML                      | ✅     |
| 2 | Normalizer dédié                         | ✅     |
| 3 | Pre-passes → IPreResolver               | ✅     |
| 4 | Refonte rewriting-based (POC)            | ✅ Phase D-5 (100% audit YAML) |
| **4-D6** | **Bascule prod + anchor unifié** | **proposé ici** |
| 5 | (absorbé Ch4)                            | absorbé |
| 6 | Découper SuggestionService god class    | à faire |
