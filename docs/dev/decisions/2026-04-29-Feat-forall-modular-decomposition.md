# Feat — Décomposition modulaire de `forall`/`exists` (∀ + var + ∈ + set)

**Date :** 2026-04-29
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** [2026-04-28-Feat-forall-scope-source-mutation.md](2026-04-28-Feat-forall-scope-source-mutation.md)

## Décision

Revenir d'une grammaire scope `forall var (in?) set` (introduite le 28-04)
vers une **composition modulaire** où chaque morceau est traité unitairement :

| Source | Décomposition | Rendu |
|--------|---------------|-------|
| `forall` | `\forall` (Const seul) | `\forall` |
| `dans` / `in` / `appartient` | ` \in ` (Const seul) | ` \in ` |
| `(-` | nouvel alias de `in` | ` \in ` |
| `forall x dans R` | juxtaposition de 3 morceaux | `\forall x \in R` |
| `forall x,y dans R^2` | idem, sans grammaire spéciale | `\forall x,y \in R^{2}` |
| `forall x dans R, x ≥ 0` | continue librement après le set | `\forall x \in R, x \geq 0` |

La désambig `V` → 3 alts (V identity / ∀ / √) est préservée, mais la mutation
ensemble `V → forall` produit désormais un simple `Const("\\forall")` au lieu
d'un scope `Quant`.

## Pourquoi (revirement)

L'ADR du 28-04 avait introduit un nœud AST `Quant(symbol, var, set)` avec une
règle de scope dans le parser qui consommait `var (in?) set` séquentiellement,
matérialisait les args manquants par des `Hole` (rendus `\square`). UX :
*"on voit V [] (- [] les carrés se remplissent au fur et a mesure de la frappe"*.

À l'usage et après essai, l'utilisateur a constaté que c'était une **usine à
gaz** — environ 150 lignes de code dédié (AST node, render, parse scope,
helpers `LhsEndsWithInterval` qui descend dans `Quant.Set`) pour reproduire
ce qui se compose **naturellement par juxtaposition** des Const existants.

Citation utilisateur :
> "est ce qu'on pourrait pas se dire V => desambig sur forall / puis
> l'utilisateur tape x / x,y / ce qu'il veut / puis (- ou appartient ou dans
> / puis l'interval ? tout ca un peu unitairement ?"

### Bénéfices de la décomposition modulaire

- **~150 lignes de code en moins** (Quant AST, RenderQuant, ParseScope forall,
  LhsEndsWithInterval branche Quant, tests Quant dédiés).
- **Composition libre** : l'utilisateur peut taper `forall x,y dans R^2`,
  `forall x dans R, x ≥ 0`, `exists y dans N tel que y > 5` sans hack
  spécial. La grammaire scope figeait `var (in?) set` et tout ce qui
  suivait devenait du `Bin(*, Quant, ...)`.
- **Cohérent avec la sémantique math standard** où ∀, ∈ sont des opérateurs
  distincts et non un macro-scope.
- **Plus simple à étendre** : ajouter `(-` comme alias de `in` est juste
  une ligne dans Vocabulary.

### Trade-offs assumés

- **Plus de Holes guides** (`\forall \square \in \square`) au moment où
  l'utilisateur tape juste `V`. Le rendu sera juste `\forall`, et l'user
  doit savoir qu'il faut taper `var dans set` derrière. Acceptable parce
  que :
  - L'utilisateur a déjà le clavier MathCursor mental "tape les morceaux"
  - Le brief NER v5 prévoit que `forall x dans R` soit détecté comme zone
    math native par le NER
  - Pas de friction réelle : taper `forall x dans R` n'est pas plus long
    que `forall x R`

- **L'option courte `forall x R` (sans `in`/`dans`) ne marche plus**.
  L'utilisateur doit taper le symbole d'appartenance explicitement. Le
  raccourci `(-` (deux touches) compense.

## Conséquences

### Code (couche 1 — core)

- **AstNodes.cs** : supprimer le nœud `Quant`.
- **LatexRenderer.cs** : supprimer la branche `Quant` et `RenderQuant`.
- **Parser.cs ParseScope** : `case "forall"` / `case "exists"` revert vers
  `return new Const("\\forall")` / `Const("\\exists")`. Supprimer
  l'helper `IsKwCanon` s'il n'est plus utilisé ailleurs (vérifier).
- **Parser.cs LhsEndsWithInterval** : supprimer la branche `Quant`.
- **Vocabulary.cs** : ajouter `(-` dans `MultiCharOps` avec canon `in`.
- **AlternativeGenerator.cs** : `ScanVAsForallEAsExists` reste tel quel
  (mutation V→forall, E→exists). Le preview du rendu sera juste
  `\forall` (pas `\forall \square \in \square`) puisque le keyword n'a
  plus de scope.

### Tests

- Supprimer tests `Quant_*` dans ParserTests + LatexRendererTests
- Mettre à jour `Forall_alone_*` → preview = `\forall`, plus de squares
- Ajuster `V_alt_previews_render_real_post_mutation` : alt forall preview
  = `\forall xy` (juxtaposition simple) au lieu de `\forall x \in y`
- Tests pour `(-` comme alias de `in`
- Régression `forall x dans R` → `\forall x \in R` doit toujours marcher
  par juxtaposition

### ADR superseded

L'ADR `2026-04-28-Feat-forall-scope-source-mutation.md` passe en
`Statut: retracté` avec `Superseded by` pointant ici.

## Validé par l'utilisateur

Direction (revirement assumé) :
> "j'ai un soucis de conception dans ma tete, est ce qu'on a pas fait une
> usine a gaz pour le V x R ? est ce qu'on pourrait pas se dire V => desambig
> sur forall / puis l'utilisateur tape x / x,y / ce qu'il veut / puis (- ou
> appartient ou dans / puis l'interval ? tout ca un peu unitairement ?"

Validation du plan de simplification :
> "oui go"

## Statut

acté
