# Refactor — Visitor sur AST (étape 4 du refacto extensibilité)

**Date :** 2026-05-13
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-13-Meta-extensibility-axes-abstractions.md](2026-05-13-Meta-extensibility-axes-abstractions.md)
+ [docs/dev/architecture/cartography.md](../architecture/cartography.md)
(dette Niveau 1 : `LatexRenderer.cs` switch exhaustif sur 18 types AST)

## Décision

Le `switch (node)` exhaustif de `LatexRenderer.Render` est remplacé par un
pattern **Visitor typé** sur l'AST :

1. Nouvelle interface `IAstVisitor<TResult>` dans
   `core-csharp/src/MathCursor.Core/Lattice/Ast/IAstVisitor.cs` avec **une
   méthode `Visit` par type concret** (18 méthodes : Atom, Hole, Const,
   Unary, Bin, Sup, Sub, Group, Frac, Sqrt, Vec, Angle, Func, Sum, Lim,
   Int, Interval, FuncDef, VectorCoordinates, MultiLineBlock).

2. `AstNode` devient abstrait avec :
   ```csharp
   public abstract TResult Accept<TResult>(IAstVisitor<TResult> visitor);
   ```
   Chacune des 18 sous-classes override avec
   `=> visitor.Visit(this);`.

3. Nouvelle classe `LatexRenderingVisitor : IAstVisitor<string>` dans
   `core-csharp/src/MathCursor.Core/Lattice/LatexRenderingVisitor.cs` qui
   reprend la logique métier des switches (RenderBin, RenderAngle,
   RenderFunc, RenderSum, RenderInterval, RenderFuncDef,
   RenderVectorCoordinates, RenderMultiLineBlock, helpers Cases/Align).

4. `LatexRenderer` devient **façade légère** : conserve l'API publique
   `Render(AstNode? node)` et `GlobalOptions`, délègue à un nouveau
   visiteur instancié par appel.

## Pourquoi

- **Identifié comme dette Niveau 1** dans la cartographie 2026-05-13 :
  toute construction AST future (matrice, dérivée, futurs nœuds) nécessitait
  d'éditer le switch exhaustif. Risque réel de drift silencieux : un `case`
  manquant retombait sur `_ => string.Empty` sans erreur compile.
- **Anti-pattern n°1 du brief extensibilité** : "`switch (node.Type)`
  dans le code partagé → Visitor / Strategy / dispatch par interface".
  Cet ADR matérialise la doctrine pour l'AST côté Core.
- **Bénéfice mécanique immédiat** : ajouter un nouveau type AST (ex.
  `Matrix` pour l'extension axe A future) déclenche une compile error
  dans TOUS les visiteurs implémenteurs tant qu'ils n'ont pas fourni
  leur méthode `Visit(Matrix)`. Le drift devient impossible.

## Tradeoff & alternatives écartées

- **Garder le switch et juste ajouter un test exhaustif "tous les types
  AST ont un case"**. Rejeté : ce test serait fragile (parsing de la
  méthode `Render` à coup d'analyzer custom), et n'empêche pas le drift
  dans les AUTRES consommateurs d'AST (LatexToUnicodeMath, normaliseurs
  futurs). Le pattern Visitor scale à N consommateurs.

- **Mettre `IAstVisitor` dans `MathCursor.Core.Abstractions`**. Rejeté
  cf. ADR 2026-05-13-Meta-extensibility-axes-abstractions §"IAstVisitor"
  — il dépend des types Core, son point d'extension naturel est
  `Core/Lattice/Ast/`, pas Abstractions.

- **`TResult` comme `out`** dans `IAstVisitor<out TResult>`. Adopté
  (variance covariante) — un `IAstVisitor<DerivedString>` est assignable
  à `IAstVisitor<string>`. Permet la composition typée future.

- **Visiteur cached statique** dans `LatexRenderer` plutôt qu'un nouveau
  par appel. Rejeté : `LatexRenderingVisitor` capture les `RenderOptions`
  en constructeur. Si on cache, les changements de `GlobalOptions`
  post-init seraient ignorés. L'allocation est négligeable (constructeur
  trivial, pas de state lourd).

## Conséquences

- **+1 fichier** : `Lattice/Ast/IAstVisitor.cs` (51 lignes).
- **+1 fichier** : `Lattice/LatexRenderingVisitor.cs` (~290 lignes,
  reprend la logique).
- **`Lattice/Ast/AstNode.cs`** : ajout `abstract Accept<TResult>`.
- **`Lattice/Ast/AstNodes.cs`** : ajout d'`Accept` override sur les 18
  sous-classes (1 ligne chacune).
- **`Lattice/LatexRenderer.cs`** : passe de ~370 lignes à ~40 lignes
  (façade pure). Préserve l'API publique pour zéro impact externe.
- **API publique inchangée** : `LatexRenderer.Render(node)` +
  `LatexRenderer.GlobalOptions` toujours là, sémantiquement identique.
- **0 régression test** : 935/944 Core conservés (6 préexistants
  Corpus×2 + CrossMerge×4), 419/419 Adapter.

## Conséquences pour les extensions futures

- **`LatexToUnicodeMath`** : utilise actuellement des Regex + scan
  string. Pourrait devenir un `IAstVisitor<string>` à son tour si on
  veut le brancher sur l'AST directement (au lieu du LaTeX rendu). Hors
  scope de cet ADR — refacto ciblé futur.
- **Nouveau type AST `Matrix`** (extension future axe A) : ajout dans
  `AstNodes.cs` + `Accept` override + 1 méthode dans `IAstVisitor` +
  implémentation dans `LatexRenderingVisitor`. Compile error tant que
  ce dernier point n'est pas fait — drift impossible.
- **Nouveau format de sortie** (ex. `MathJaxRenderingVisitor` pour
  Obsidian) : créer une classe `: IAstVisitor<string>` avec ses 18
  Visit. Aucune modification de l'AST ni du LatexRenderer existant.

## Validé par l'utilisateur

> « c b a » — ordre validé : commit Phase 2 (c), puis étape 4 archi
> Visitor AST (b), puis MC0009 + MC0006 (a).

## Plan refacto — état d'avancement

- [x] **Étape 1** — Cartographie
- [x] **Étape 2** — Interfaces Abstractions
- [ ] **Étape 3** — Implémentation par types existants (LatticeEngine
  implémente IDomainParser, etc.) — reportée
- [x] **Étape 4** — Visitor sur AST (cet ADR)
- [ ] **Étape 5** — Sortir chaînes FR du Core → `locales/fr/keywords.yaml`
  + activation MC0002 — 0.5j
- [ ] **Étape 6** — DomainRouter (placeholder math-only) — 0.5j
- [ ] **Étape 7** — ShortcutResolver — 0.5j
- [ ] **Étape 8** — Test d'intégration extensibilité — 0.5j

Note : l'étape 3 (faire implémenter les contrats par les types existants)
est conservée comme **optionnelle** — le Visitor (étape 4) est ce qui
débloque l'extensibilité concrète. L'étape 3 sera traitée si/quand on
ajoute le premier nouveau parseur de domaine (Chimie) ou sérialiseur
(MathJax).
