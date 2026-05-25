# Refactor — Chantier 3 : extraction pre-resolvers (multi-line + prefix-match)

**Date :** 2026-05-25
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- ADR [2026-05-25-Refactor-chantier2-normalizer-extract](2026-05-25-Refactor-chantier2-normalizer-extract.md) (= Chantier précédent).
- ADR [2026-05-23-Feat-engine-v2-multiline-port](2026-05-23-Feat-engine-v2-multiline-port.md) (= port initial du multi-line dans `MathEngine.Resolve`).
- Plan simplification du Resolve 2026-05-25.

## Citation acté

> « go continue » — utilisateur, 2026-05-25 (= validation du Chantier 3 après Ch2 livré, dans la suite du plan 1-6 validé « oui on fait 1-6 je valide tes decisions 1-2-3 »)

## Contexte

`MathEngine.Resolve` accumulait au fil des features 2 pre-passes inlinées + 1 main loop dans un même monolithe de 700 LOC :

1. **Pre-pass multi-line align*/cases** : ~135 LOC pour `TryBuildMultiLineBlock` + `TryBuildCasesBlock` + `TryBuildAlignBlock` + `IsCasesLineStart` + `MapAlignMarkerToLatex` + `ParseTokenRange` + `RenderTokens`. Cohérent en soi mais noyé dans le moteur.
2. **Pre-pass prefix-match** : `IsSingleWordStandalone` + struct `PrefixMatch` + `FindPrefixMatches` (~80 LOC). Réutilisé aussi depuis le closure anchor matcher du ctor.
3. **Main loop** : tokenize → top-level loop → operand/anchor/composition → collisions.

Le main loop est plus difficile à lire car les pre-passes lui sont accolées. Chaque pre-pass a sa propre logique de match + emission + construction `EngineResult`, ce qui est dupliqué sous formes différentes.

## Décision

Introduire un contrat `IPreResolver` + 2 implémentations dédiées + boucle minimale dans `Resolve`.

### `Resolution/IPreResolver.cs`
```csharp
public interface IPreResolver
{
    EngineResult? TryResolve(IReadOnlyList<Token> tokens);
}
```
Contract minimal : retourne `null` si pas match (= main loop continue) ou un `EngineResult` complet (= short-circuit).

### `Resolution/MultiLineBlockResolver.cs`
- Owns `TryBuildBlock`, `TryBuildCasesBlock`, `TryBuildAlignBlock`, `IsCasesLineStart`, `MapAlignMarkerToLatex`, `ParseTokenRange`.
- Construit `MultiLineBlockNode` + emit via `LatexEmitter`. Retourne `EngineResult` avec `ruleId` `multiline-align` / `multiline-cases`.

### `Resolution/PrefixMatchResolver.cs`
- Owns struct `PrefixMatch`, méthodes `IsSingleWordStandalone` (`public static`), `FindMatches` (`public`).
- `TryResolve` reproduit la logique exacte : 1 match unique → topLatex + ruleId `prefix-match:<source>`, ≥ 2 → collisions + ruleId `prefix-match:multi`.
- `FindMatches` reste réutilisable depuis le closure anchor matcher du ctor `MathEngine`.

### `MathEngine.Resolve`
```csharp
foreach (var preResolver in _preResolvers)
{
    var pre = preResolver.TryResolve(tokens);
    if (pre != null) return pre;
}
// main loop ci-dessous…
```
- Le main loop est désormais accolé direct au tokenize, sans pre-passes inline qui le masquent.
- `MathEngine.cs` : −150 LOC, devient lisible d'un coup d'œil.

## Tradeoff & alternatives écartées

- **Transformer le multi-line en rule YAML** : rejetée pour V1. La shape multi-line exigerait un slot type `{line:expr}+` avec gestion des `\n` token-level — ça mérite son propre Chantier (= une fois le ShapeMatcher étendu). Pour l'instant, l'isolation dans un pre-resolver dédié est suffisante.

- **Transformer le prefix-match en rule YAML** : rejetée. Le prefix-match n'est PAS un pattern de shape, c'est un comportement de découverte (= UX as-you-type). Logiquement il appartient à un module distinct des règles structurelles. Le mécanisme reste déjà data-driven (= il itère `_vocab.Anchors`, `_vocab.Functions`, `_vocab.Relations`).

- **Garder les pre-passes inline mais les nommer/commenter mieux** : rejetée. Le brief utilisateur 2026-05-25 demande explicitement de simplifier le Resolve (= « le Resolve peut et doit être grandement simplifié »). L'isolation matérialise cette simplification.

- **Implémenter `IPreResolver` aussi par le main loop final** : rejetée pour V1. Le main loop a un état mutable plus riche (= operandLatex/operandTokens/opTokens/collisions composées par détecteurs), forcer le contrat `IPreResolver` minimal y serait contraint. À envisager seulement quand le main loop sera lui-même décomposé (= Chantier 5/6 si on aboutit à un `RuleBasedMerger` séparé).

## Conséquences

- **Code touché** :
  - `core-csharp/src/MathCursor.Engine/Resolution/IPreResolver.cs` (+26 lignes, nouveau).
  - `core-csharp/src/MathCursor.Engine/Resolution/MultiLineBlockResolver.cs` (+150 lignes, nouveau).
  - `core-csharp/src/MathCursor.Engine/Resolution/PrefixMatchResolver.cs` (+150 lignes, nouveau).
  - `core-csharp/src/MathCursor.Engine/MathEngine.cs` (−195 lignes net : −300 helpers + 105 boucle/ctor).

- **Tests** :
  - `Resolution/PreResolverPipelineTests.cs` (+5 cas : multi-line align, multi-line cases, single-line fallthrough, prefix-match unique, helper `IsSingleWordStandalone`).
  - 302/302 engine v2 verts (= +5 vs Ch2) + 3 skipped.
  - 393/393 adapter VSTO verts (= identique avant Ch3, refactor pur).

- **API publique** : 3 nouveaux types `public` (`IPreResolver`, `MultiLineBlockResolver`, `PrefixMatchResolver`). Pas de breaking change. Méthodes `MathEngine` privées supprimées (= `IsSingleWordStandalone`, `FindPrefixMatches`, `TryBuildMultiLineBlock` et helpers, `RenderTokens` mort).

- **Règles MC impactées** : aucune.

## Validation post-fix

1. `dotnet test core-csharp/tests/MathCursor.Engine.Tests/` → 302/302 + 3 skipped.
2. `dotnet test adapter-vsto/tests/MathCursor.Tests/` → 393/393.
3. Smoke test functional preservé : `a+b\n= c+d` → align block, `som` → prefix-match, `a+b` → main loop fallthrough.

## Plan en cours — état d'avancement

Chantier 3 / 6 du plan simplification du Resolve.

| # | Chantier | Statut |
|---|---|---|
| 1 | hardcoded FR → YAML | ✅ |
| 2 | Normalizer dédié | ✅ |
| **3** | **Pre-passes → IPreResolver dédiés** | ✅ acté ici |
| 4 | Collisions C# → règles YAML | à faire |
| 5 | RuleBasedMerger data-driven | à faire |
| 6 | Découper `SuggestionService` god class | à faire |
