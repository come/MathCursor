# Feat — POC moteur de détection isolé (`MathCursor.Engine`)

**Date :** 2026-05-22
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-21-Feat-pattern-ranker.md](2026-05-21-Feat-pattern-ranker.md) (P10 — ranker gaté), [2026-05-21-Feat-pattern-pipeline-integration-zone-resolver.md](2026-05-21-Feat-pattern-pipeline-integration-zone-resolver.md) (P7a — intégration patterns dans ZoneResolver)

## Citation acté

> « j'aimerai qu'on "discute" d'un nouvel algo plus leger et extensible » +
> « ok prépare toi à le faire mais en mode bien isolé ! je corrige le brief,
> mais globalement du coup le plus gros changement sera sur la desambiguité,
> en gros, elle saute au profit de plusieurs choix finaux comme dans un
> autocompleter standard (editeur de code) » + « on garde le NER bien sur!
> et les mutation, isole bien ca mais voila le nouveau brief, il faudra
> rebrancher j'imagine [...] on fait le plan » — utilisateur, 2026-05-22

Choix validés via `AskUserQuestion` :
- **Scope POC** : 2 concepts minimum (= `limites.yml` + `sommes.yml` couvrant sum+prod et classiques)
- **Switch ZoneResolver** : feature flag = param ctor optionnel `IEngineFrontend? engine = null`. Si fourni → Engine en premier, fallback legacy. Tests adapter inchangés.
- **Data layout** : `data-v2/` à la racine projet (= isolation totale du wildcard MSBuild legacy)
- **ADR maintenant** (= avant la moindre ligne de code)

## Contexte

Le cœur de détection actuel cumule ~2 500 LOC sur 2 fichiers monolithiques :
- `core-csharp/src/MathCursor.Core/Lattice/Parser.cs` (1 392 LOC) — parser top-down du token-graph
- `core-csharp/src/MathCursor.Core/Lattice/AlternativeGenerator.cs` (1 062 LOC) — source des règles ambig closed

Le brief v4 (= [brief-moteur-reconnaissance.md](../../../../../Users/wanadev/Downloads/brief-moteur-reconnaissance%20(1).md), local au user — non versionné) propose un cœur **passe-pile déterministe O(n)** avec :
- Précédence 5-6 tiers explicite (vs précédence implicite parser-spaghetti)
- Combinateur `liste(X, sep)` + force croissante `expr < colsep < rowsep` (vs heuristique diviseurs MatrixTemplate)
- Vocabulaire centralisé par locale (`fr.yml`, `en.yml`) (vs JSON éparpillés)
- Scoring **gaté** : tourne uniquement sur collision ≥ 2 candidats (= identique au P10 d'hier, terminologie différente)
- Collision → candidats locaux affichés comme autocomplete IDE (= `.` + IntelliSense)
- Sortie partielle via `\square` sur cadres ouverts (= live preview)
- Vérificateur de collisions au chargement (= absent chez nous, cause du bug `F'(x)=1/x` réglé hier)

Drop-in derrière le contrat `ZoneResolver` existant. **NER (= L2 adapter) et mutations source (= existant Core) restent intacts.**

## Décision

Créer un **projet C# physiquement séparé** `core-csharp/src/MathCursor.Engine/` avec :
- Namespace `MathCursor.Engine` (= aucun couplage avec `MathCursor.Core`)
- Tests dans `core-csharp/tests/MathCursor.Engine.Tests/`
- Data dans `data-v2/` racine projet (= séparé du wildcard MSBuild legacy)
- Golden cases co-localisés YAML
- Aucune référence depuis l'adapter VSTO durant le POC

### Contrat interne `IEngineFrontend`

```csharp
namespace MathCursor.Engine;

public interface IEngineFrontend
{
    EngineResult Resolve(string source, EngineOptions options);
}

public sealed class EngineResult
{
    public string TopLatex { get; }
    public bool IsComplete { get; }
    public IReadOnlyList<EngineCandidate> Collisions { get; }
}
```

### Drop-in `ZoneResolver`

`ZoneResolver` ctor étendu avec `IEngineFrontend? engine = null`. Si fourni → tente Engine, fallback legacy. Si null → comportement actuel intact. Aucun changement adapter.

Adapter `EngineToResolvedZone` (= dans `MathCursor.Engine.Adapter/` séparé) mappe `EngineResult` → `ResolvedZone` legacy. C'est lui qui orchestre le branchement.

### Étapes (= 15 jalons)

| # | Étape | Phase | Test |
|---|---|---|---|
| P11.0 | Cet ADR | doc | — |
| P11.1 | Coquille `MathCursor.Engine.csproj` + tests + solution | Coquille | smoke |
| P11.2 | `Vocabulary/LocaleVocabulary.cs` + `data-v2/locale/fr.yml` | Vocab | `VocabularyTests` |
| P11.3 | `Tokenization/Tokenizer.cs` (whitespace, symboles, virgule décimale, glue) | Token | `TokenizerTests` |
| P11.4 | Types `Frame` (OP/DELIM/LIST) + `ParseStack` | Parse | — |
| P11.5 | Table précédence 5-6 tiers chargée depuis vocab | Parse | — |
| P11.6 | Algo dispatch (opérande/ancre/délim/infixe/sep) | Parse | `ParserPlatTests` |
| P11.7 | Combinateur `liste(X, sep)` + types `line`/`matrix` | List | `ListCombinatorTests` |
| P11.8 | Sortie partielle `\square` pour cadres ouverts | Live | `IncrementalTests` |
| P11.9 | Parseur YAML `shape`/`emit` + fragment-vanish `[...]` | Rule | `RuleSpecTests` |
| P11.10 | `data-v2/concepts/limites.yml` + golden cases | Limites | `LimitGoldenTests` |
| P11.11 | `data-v2/concepts/sommes.yml` (sum + prod) + golden | Sommes | `SumProdGoldenTests` |
| P11.12 | Collision → candidats + ranker gaté (= tourne ssi ≥ 2) | Collision | `CollisionTests` |
| P11.13 | Validateur cross-rules au chargement | Validate | `LoadTimeCollisionTests` |
| P11.14 | Adapter `EngineToResolvedZone` + feature flag `ZoneResolver` | Drop-in | — |
| P11.15 | Harness shadow `LimAmbigBugTests` Engine vs legacy + rapport parité | Shadow | rapport |

### Mesures de succès POC

| Métrique | Cible |
|---|---|
| LOC `MathCursor.Engine` (sans data, sans tests) | < 2 000 |
| Golden cases brief `limites` + `sommes` | 100 % verts |
| `LimAmbigBugTests` shadow via Engine | ≥ 90 % verts |
| Re-parse `n=50 tokens` | < 1 ms |
| Couplage code↔Core legacy | **0** dépendance |

POC concluant → ADR de bascule + extension concept par concept (analyse, géométrie, logique, ensembles). Non concluant → suppression du projet, le legacy n'a jamais bougé.

## Tradeoff & alternatives écartées

- **Sous-dossier `Core/Engine/`** : risque de couplage transitif (namespace partagé, dépendances IDE invisibles). Rejeté pour isolation.
- **Worktree git** : casse l'IDE + duplication CI. Rejeté pour ergonomie dev.
- **Big-bang remplacement** : risque de perte de couverture (= `LimAmbigBugTests`, multi-contexte commit). Rejeté par doctrine MathCursor.
- **POC concept unique limites seul** : ne stresse pas le combinateur liste. Rejeté par user (= choix multi-concepts).

## Conséquences

- **Code touché** :
  - `core-csharp/src/MathCursor.Engine/` (nouveau projet)
  - `core-csharp/tests/MathCursor.Engine.Tests/` (nouveau projet)
  - `core-csharp/src/MathCursor.Engine.Adapter/` (nouveau projet, P11.14)
  - `data-v2/` (nouveau dossier racine)
  - `core-csharp/src/MathCursor.Core/ZoneResolver.cs` : +1 param ctor optionnel (P11.14)
  - `MathCursor.sln` : +3 projets
- **Tests** : projet dédié `MathCursor.Engine.Tests`, golden cases co-localisés YAML. Harness shadow pour mesurer parité.
- **API publique** : `IEngineFrontend` nouveau contrat. `ZoneResolver` rétro-compat (= param optionnel).
- **Règles MC impactées** : aucune (= moteur pur, pas de XML/OMath/splice).

## Validation post-POC

POC validé si :
1. Tous les golden cases du brief (limites + sommes) passent (= 100%)
2. ≥ 90% des `LimAmbigBugTests` shadow via Engine
3. LOC engine < 2 000
4. Zéro dépendance vers Core legacy

Sinon : décision documentée dans un ADR `Retracted-engine-poc` avec analyse des écarts.

## Plan en cours — état d'avancement

P11 — Engine POC :
- [x] P11.0 ADR (= ce document)
- [ ] P11.1 Coquille projet
- [ ] P11.2 Vocabulary + fr.yml
- [ ] P11.3 Tokenizer
- [ ] P11.4 Frame + ParseStack
- [ ] P11.5 Précédence multi-tiers
- [ ] P11.6 Dispatch passe-pile
- [ ] P11.7 Combinateur liste
- [ ] P11.8 Sortie partielle `\square`
- [ ] P11.9 Parseur YAML shape/emit
- [ ] P11.10 limites.yml + golden
- [ ] P11.11 sommes.yml + golden (sum + prod)
- [ ] P11.12 Collision + candidats + ranker gaté
- [ ] P11.13 Validateur cross-rules load-time
- [ ] P11.14 Adapter `EngineToResolvedZone` + feature flag
- [ ] P11.15 Harness shadow + rapport parité
