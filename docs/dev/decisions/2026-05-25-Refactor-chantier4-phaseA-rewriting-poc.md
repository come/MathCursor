# Refactor — Chantier 4 Phase A : POC rewriting-based engine

**Date :** 2026-05-25
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- ADR [2026-05-25-Refactor-chantier3-preresolvers](2026-05-25-Refactor-chantier3-preresolvers.md) (= Chantier précédent).
- ADR [2026-05-23-Meta-yaml-collision-dsl-future](2026-05-23-Meta-yaml-collision-dsl-future.md) (= ancien brief, partiellement absorbé).
- Plan simplification du Resolve 2026-05-25 (= refondu : Ch4 absorbe Ch5 + une partie de Ch6).

## Citation acté

> « go » — utilisateur, 2026-05-25 (= validation directe du plan refonte rewriting-based après reformulation « pour moi il peut y'avoir plusieurs passes, surtout qu'on vient en flow d'ecriture .. mais globalement c'est gratuit de boucler plusieurs fois pour checker si y'a pas un subpattern non ? » et « je veux de l'élégance .. la on a empilé du merdier .. et ça me fait mal au cœur .. c'était l'objet de la V2 »)

## Contexte

L'engine v2 actuelle a accumulé :

- **Main loop** dans `MathEngine.Resolve` (= match top-level + composition).
- **StackParser** + `PrecedenceClimber` + `ListCombinator` (= parsing récursif).
- **7 détecteurs C# `ICollisionDetector`** (= patterns ambigus : vec-letter, dot-vec, slurp×2, etc.).
- **3 mergers** (Intra, MarkerChain, CasesChain).
- **Pre-resolvers** (= multi-line, prefix-match — Chantier 3).

Plusieurs niveaux d'abstraction qui se côtoient. La V2 avait pour but de défaire la dette de la V1 ; ce n'est pas atteint tant que ces couches co-existent.

L'utilisateur a reformulé la collision : **« plusieurs YML matchent même tokens → collision, sinon non »**. Et a confirmé que **plusieurs passes de matching sont gratuites** au regard de la taille de l'entrée (= zone curseur ~100 tokens).

Cette double formulation pointe vers un unique mécanisme : **moteur de rewriting à point fixe**.

## Décision (Phase A)

Construire un **POC isolé** d'un `RewriteEngine` qui implémente :

1. **Items typés** : `TokenItem` (primitive du Tokenizer) | `RewriteItem` (produit d'une règle, porte `Category` + LaTeX émis).
2. **Catégories sémantiques** : `Expr`, `Letter`, `Number`, `Interval`, `Set`, `Function`, `Vector`, etc. — typage des slots des règles.
3. **Pattern + RewriteRule** : `id + pattern + produces + emitTemplate`. Pattern = suite de `Literal` (= texte exact) + `Slot` (= capture d'un Item d'une catégorie).
4. **RewriteMatcher** : tente une règle à une position. Subsumption catégorielle : `Expr` accepte `Letter|Number|Var|Interval|Set|Function|Vector|Expr`.
5. **RewriteEngine** : loop à point fixe — scan toutes positions × toutes règles, applique leftmost-longest, stash alternatives en `Alternatives`.

Le POC vit dans `core-csharp/src/MathCursor.Engine/Rewriting/` **parallèle au moteur actuel**. Zéro touche au `MathEngine` en prod. Les 302 tests engine existants restent verts intacts.

## Tradeoff & alternatives écartées

- **Convertir les 7 détecteurs C# en règles YAML dans le moteur actuel** (= Ch4 « pragmatique ») : rejetée par l'utilisateur. Maintenait la dette legacy de la V2. « Je veux de l'élégance, on a empilé du merdier ».

- **Refonte directe sans POC isolé** : rejetée. `feedback_word_api_workflow` impose un POC isolé avant tout changement de fond. Le POC permet de valider l'algorithme sur 9 cas représentatifs avant d'engager la migration.

- **Parser à grammaire context-free explicite** (= ANTLR-style) : rejetée. Trop lourd, ne supporte pas bien la composition bottom-up dynamique entre règles produisant des catégories. Le rewriting à point fixe est plus simple et plus flexible.

- **Garder slot `{expr}` non typé** : rejetée. Sans typage, on perd la composition « union d'intervalles → règle qui demande 2 Items `Interval` » qui justifie le POC.

## Conséquences

- **Code nouveau** :
  - `Rewriting/Category.cs` (+47 lignes).
  - `Rewriting/Item.cs` (+78 lignes) : `Item` abstract + `TokenItem` + `RewriteItem`.
  - `Rewriting/Pattern.cs` (+50 lignes) : `Pattern`, `PatternElement`, `Literal`, `Slot`.
  - `Rewriting/RewriteRule.cs` (+27 lignes).
  - `Rewriting/RewriteMatcher.cs` (+115 lignes) : `TryMatch`, `CategoryMatches`, `ApplyTemplate`.
  - `Rewriting/RewriteEngine.cs` (+115 lignes) : loop fixed-point, `RewriteResult`.
  - `Rewriting/PilotRules.cs` (+95 lignes) : 7 règles pilote hardcoded.

- **Tests** :
  - `Rewriting/RewriteEnginePilotTests.cs` (+9 cas) : `frac`, `dot-vec`, `interval-closed`, `interval-union` (= composition bottom-up démontrée), `sum`, `lim`, `funcdef`, empty source, plain text passthrough.
  - 311/311 engine v2 verts (= 302 anciens préservés + 9 POC) + 3 skipped.

- **API publique** : 7 nouveaux types `public` dans `MathCursor.Engine.Rewriting.*`. Zéro modification de l'API existante.

- **MathEngine actuel** : intact. `Resolve` non touché. Pipeline collision/merger inchangé.

## Validation post-fix

1. `dotnet test core-csharp/tests/MathCursor.Engine.Tests/` → 311/311 + 3 skipped.
2. Test `Interval_union_composes_bottom_up` démontre que `[0;1] union [2;3]` rend `[0;1] \cup [2;3]` via 2 passes successives (passe 1 : 2 intervals, passe 2 : union).

## Suite (Phases B → D)

| Phase | Livrable |
|---|---|
| **A** (= cet ADR) | **POC isolé `Rewriting/`** |
| B | Comparaison vs `MathEngine.Resolve` actuel sur les 302 tests : quels patterns YAML manquent ? |
| C | Migration des règles : `data-v2/concepts/*.yml` gagne `produces:`, 7 détecteurs C# → 7 fichiers YAML, 3 mergers → règles YAML composables. |
| D | Bascule : `MathEngine.Resolve` délègue au `RewriteEngine`. Supprime `StackParser`, `PrecedenceClimber`, `ListCombinator`, `LatexEmitter`, `ICollisionDetector` et ses 7 détecteurs. Net attendu : `−2000 LOC, +500 LOC` = codebase moitié. |

## Plan en cours — état d'avancement

| # | Chantier | Statut |
|---|---|---|
| 1 | hardcoded FR → YAML | ✅ |
| 2 | Normalizer dédié | ✅ |
| 3 | Pre-passes → IPreResolver | ✅ |
| **4-A** | **POC RewriteEngine isolé** | ✅ acté ici |
| 4-B | Comparaison vs Resolve actuel | à faire |
| 4-C | Migration règles YAML | à faire |
| 4-D | Bascule + suppression legacy | à faire |
| 5 | (= absorbé par Ch4 — RuleBasedMerger devient règles YAML composables) | absorbé |
| 6 | Découper `SuggestionService` god class | à faire |
