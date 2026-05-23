# Feat — Pattern Ranker : dédup + scoring + NMS overlap (P10)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-21-Feat-pattern-pipeline-integration-zone-resolver.md](2026-05-21-Feat-pattern-pipeline-integration-zone-resolver.md) (P7a), [2026-05-21-Meta-pattern-templates-vs-ambig-closed.md](2026-05-21-Meta-pattern-templates-vs-ambig-closed.md) (P2-P6)

## Citation acté

> « ce qui m'embete du coup c'est que le pattern matching est ptetre pourri.
> l'idée pour moi est de boucler sur toutes les regles pour voir si oui ou non
> il y'a pattern match (sans sortir de la boucle si c'est le cas) et avec tout
> ceux qui matchent on fait de la pondération.. c'est bien comme ca que c'est
> fait ? » + « oui j'aimerai faire le chantier » — utilisateur, 2026-05-21

Choix archi validés via `AskUserQuestion` :
- NMS overlap : **jeter complètement** les perdants (= pas de liste secondaire ni grisé)
- Score signals : `CompletenessScore` (base obligatoire) + bonus span complet + caret-aware
- Boundary fix `IntervalUnion` (= rejet `(` après prime) : **gardé** comme defense in depth

## Contexte

`PatternPipeline.Run` (P2) boucle sur tous les templates et **concatène** leurs
completions dans l'ordre `Order asc`. Pas de scoring, pas de dédup, pas de
filtrage par overlap.

Bug observé 2026-05-21 sur `F'(x)=1/x` : la popup affichait 3 completions :

```
[ (x,▭), (x,▭), F'(x) ]
```

Cause : `IntervalUnionTemplate` matchait le `(` à position 2 (boundary check
incomplet sur apostrophes/primes), et `EnsembleTemplate` déléguait à
`IntervalUnion` pour le même bracket → 2 completions sémantiquement identiques
de forme `(x, ▢)`, suivies du PrimedDerivative correct.

Le boundary fix (= rejeter `(` après `'`/`”`/`′`/…) corrige la **sémantique**
de IntervalUnion. Mais la question archi reste : si 2 templates produisent
légitimement des completions concurrentes sur le même span (= cas ambigus),
comment choisit-on ?

## Décision

Ajouter une **étape de ranking** entre `PatternPipeline.Run` (= matching) et
`ResolvedZone.PatternCompletions` (= consommation popup). Cette étape est
encapsulée dans un nouveau contrat `IPatternRanker` (= SRP, swap futur facile).

### Contrat `IPatternRanker`

```csharp
namespace MathCursor.Core.Patterns.Ranking;

public interface IPatternRanker
{
    IReadOnlyList<PatternCompletion> Rank(
        IReadOnlyList<PatternCompletion> raw,
        PatternScanContext ctx);
}
```

Pure fonction, idempotent (`Rank(Rank(x)) == Rank(x)`).

### Algo `DefaultPatternRanker`

3 étapes ordonnées :

1. **Dédup exact** — clé `(SourceStart, SourceEnd, PreviewLatex)`. 2 completions
   à clé identique → garder la 1ère (= templateId stable, ordre déterministe).

2. **Score composite** par completion :
   ```
   score = CompletenessScore                  // 0-100 (= base du template)
         + 30  si span couvre toute la source // bonus_span_total
         + 15  si caret ∈ [start, end]        // bonus_caret_aware
   ```
   Le PENALTY span partiel évoqué dans le plan n'est **pas retenu** (= déjà
   couvert implicitement par le bonus span total + caret).

3. **NMS overlap** — 2 completions overlapent si leurs intervalles
   `[SourceStart, SourceEnd]` se chevauchent. La perdante (score inférieur) est
   **jetée**. Si égalité de score, garder la 1ère (= déterminisme par
   templateId).

### Intégration `PatternPipeline`

Ctor étendu :
```csharp
public PatternPipeline(
    IEnumerable<IPatternTemplate> templates,
    IPatternRanker? ranker = null)
```

Si `ranker == null` → comportement legacy (= concat brut, rétro-compat).
Si fourni → `Run()` applique `ranker.Rank()` avant de retourner.

`DefaultPatternRegistry.BuildBoth()` wire `DefaultPatternRanker` par défaut.

## Tradeoff & alternatives écartées

- **Ranker statique dans PatternPipeline** : couple matching + ranking dans
  une seule classe. Rejeté car viole SRP, et complique le swap futur
  (= ranker bayésien, learned, etc.).
- **Score sur `IPatternTemplate.Expand`** (= chaque template normalise son
  score) : ne permet pas de comparer cross-pattern (= overlap, dédup).
- **NMS conservateur** (= garder perdants en liste secondaire) : UX
  charge cognitive plus lourde pour le PAP. Rejeté par user.
- **Penalty span partiel** (= -10 si reste consommable) : redondant avec
  bonus span total + caret-aware. Rejeté par user dans le scoring multi-select.

## Conséquences

- **Code touché** :
  - `core-csharp/src/MathCursor.Core/Patterns/Ranking/IPatternRanker.cs` (nouveau)
  - `core-csharp/src/MathCursor.Core/Patterns/Ranking/DefaultPatternRanker.cs` (nouveau)
  - `core-csharp/src/MathCursor.Core/Patterns/PatternPipeline.cs` (ctor étendu)
  - `core-csharp/src/MathCursor.Core/Patterns/DefaultPatternRegistry.cs` (wire le ranker)
- **Tests** : `DefaultPatternRankerTests.cs` (= dédup, score, NMS, composition
  via DefaultPatternRegistry sur `F'(x)=1/x`).
- **API publique** : ajout `IPatternRanker` (= nouveau contrat). Ctor
  `PatternPipeline` rétro-compat via param optionnel.
- **Règles MC impactées** : aucune.

## Validation post-fix

- 13 tests RED écrits dans `IntervalUnionTemplateTests` +
  `PrimedDerivativePopupBugTests` doivent rester verts (= boundary fix garde
  sa correction sémantique).
- Manuel Word : pour `F'(x)=1/x`, la popup doit afficher **1 seule** completion
  (= `F'(x)`). Pour `F' :x->1/x`, traitement séparé (= cas 1 non couvert par
  P10, demande un pattern function-definition `<func> : <var> -> <expr>` en
  P11).

## Plan en cours — état d'avancement

P10 — Pattern Ranker :
- [x] P10.0 ADR (= ce document)
- [x] P10.1 IPatternRanker contrat
- [x] P10.2 DefaultPatternRanker impl (dédup + score + NMS)
- [x] P10.3 Tests ranker (= 16 verts)
- [x] P10.4 Intégration PatternPipeline + DefaultPatternRegistry
- [x] P10.5 Non-régression Core 1266+ et adapter 393/393
