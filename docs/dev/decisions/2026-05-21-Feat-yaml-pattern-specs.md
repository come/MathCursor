# Feat — Pattern specs en YAML + auto-discovery (P9e)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** partiellement
[`2026-05-21-Feat-lim-pattern.md`](2026-05-21-Feat-lim-pattern.md),
[`2026-05-21-Feat-sum-pattern.md`](2026-05-21-Feat-sum-pattern.md),
[`2026-05-21-Feat-integral-and-derivative-patterns.md`](2026-05-21-Feat-integral-and-derivative-patterns.md)
(implémentations .cs remplacées par YAML, comportement préservé)
**Lié à :**
- [`2026-05-21-Refactor-forall-belongs-arglist-convention.md`](2026-05-21-Refactor-forall-belongs-arglist-convention.md) — ArgListPatternBase (P5R)
- [`2026-05-21-Meta-pattern-templates-vs-ambig-closed.md`](2026-05-21-Meta-pattern-templates-vs-ambig-closed.md) — cadrage P0

## Citation acté

> « le yaml pour de nouvelle regles ? » — utilisateur, 2026-05-21

Choix validés via AskUserQuestion :
- **Niveau 2** : nouveau pattern via YAML sans C#
- **YamlDotNet** (déjà dans les deps) plutôt que custom parser
- **`data/patterns/*.yaml` embedded** dans Core (cohérent avec `data/*.json`)
- **Migration immédiate des 5 templates** existants en YAML
- **Probabilité P(event)** comme premier cas test YAML-only

> Note : ForallBelongs reste C# (logique custom ClassifyArgs + composition var/domain). Migration concerne les 4 templates "args positionnels purs" : Lim/Sum/Integral/Derivative.

## Contexte

Après P9d, le projet avait 5 templates héritant d'`ArgListPatternBase`
(forall-belongs, lim, sum, integral, derivative), tous suivant le moule
"head + args espace". Forte duplication entre les .cs (mêmes structures
BuildLatex/BuildDescription/BuildMutation paramétrées différemment).

P9e introduit un **système data-driven** :
- Spec YAML par pattern (`data/patterns/*.yaml`)
- Template C# générique `YamlArgListPatternTemplate` qui consomme la spec
- Auto-discovery au build (wildcard MSBuild) + auto-load au runtime (reflection)

**Résultat** : ajouter un nouveau pattern "args espace" = créer 1 YAML.
Aucun .cs à écrire, aucun enregistrement à faire.

## Décision

### 1. Schéma YAML

```yaml
template_id: lim                  # identifiant du pattern
order: 0                          # ordre dans le PatternPipeline

heads:                            # variants de head
  - { source: Lim, latex: '\lim', mutation: lim, weight: 100 }
  - { source: lim, latex: '\lim', mutation: lim, weight: 95 }

slots:                            # slots positionnels
  - { position: 0, name: var }
  - { position: 1, name: limit, convert: infinity }
  - { position: 2, name: expression, multi_token: true }

scoring:                          # CompletenessScore progressif
  base: 25
  per_slot: 25

render:                           # templates avec placeholders
  preview: '\lim_{<var> \to <limit>} <expression>'
  hint:    '\lim_{<var|\square> \to <limit|\square>} <expression|\square>'
  description: 'lim_<var|▭>→<limit|▭> <expression|▭>'
```

#### Placeholders dans render

- `<name>` : valeur du slot (vide en preview si non rempli)
- `<name|fallback>` : valeur ou fallback si vide (utilisé pour hint et description)

#### Slot specs

- `position` : index 0-based dans la liste d'args
- `name` : nom du placeholder (= `<name>` dans render)
- `convert: infinity` : applique `ArgListPatternBase.ConvertInfinityToken`
  (Latex) ou `ConvertInfinityToUnicode` (description) — pour les bornes
  qui peuvent être `+oo`/`-oo`/`∞`
- `multi_token: true` : consomme tous les args restants depuis cette
  position (= expression multi-tokens)

### 2. Stack technique

| Composant | Rôle |
|---|---|
| `PatternSpec` (POCO) | Représente la spec deserializée |
| `PatternSpecLoader` | Charge le YAML embedded via YamlDotNet |
| `YamlArgListPatternTemplate` | Hérite `ArgListPatternBase`, ctor reçoit `PatternSpec`, implémente `Expand` génériquement |
| `DefaultPatternRegistry.LoadAllTemplates` | Itère sur `ListEmbeddedPatternFiles()`, charge chaque YAML, crée `YamlArgListPatternTemplate` |

### 3. Auto-discovery au build (MSBuild)

```xml
<EmbeddedResource Include="..\..\..\data\patterns\*.yaml">
  <Link>data\patterns\%(Filename)%(Extension)</Link>
</EmbeddedResource>
```

Le wildcard `*.yaml` capte automatiquement tout nouveau fichier au build.
Pas de mise à jour manuelle de la liste.

### 4. Auto-discovery au runtime (reflection)

```csharp
public static IReadOnlyList<string> ListEmbeddedPatternFiles()
{
    var assembly = typeof(PatternSpecLoader).Assembly;
    const string prefix = "MathCursor.Core.data.patterns.";
    var result = new List<string>();
    foreach (var name in assembly.GetManifestResourceNames())
    {
        if (!name.StartsWith(prefix) || !name.EndsWith(".yaml")) continue;
        result.Add(name.Substring(prefix.Length));
    }
    return result;
}
```

### 5. Migration des 4 templates en YAML

| Pattern | .cs supprimé | YAML créé |
|---|---|---|
| `lim` | `Patterns/Templates/LimTemplate.cs` | `data/patterns/lim.yaml` |
| `sum` | `Patterns/Templates/SumTemplate.cs` | `data/patterns/sum.yaml` |
| `integral` | `Patterns/Templates/IntegralTemplate.cs` | `data/patterns/integral.yaml` |
| `derivative` | `Patterns/Templates/DerivativeTemplate.cs` | `data/patterns/derivative.yaml` |

**Tests préservés** : les 60 tests Lim/Sum/Integral/Derivative passent
**inchangés** (modulo le remplacement de `new XxxTemplate()` par
`new YamlArgListPatternTemplate(PatternSpecLoader.LoadEmbedded("xxx.yaml"))`).
Validation que le YAML reproduit fidèlement le comportement C#.

### 6. Premier pattern nouveau YAML-only : `probability`

```yaml
template_id: probability
heads:
  - { source: P,    latex: P, mutation: P, weight: 100 }
  - { source: Prob, latex: P, mutation: P, weight: 90 }
slots:
  - { position: 0, name: event, multi_token: true }
render:
  preview: 'P(<event>)'
  hint:    'P(<event|\square>)'
  description: 'P(<event|▭>)'
```

Aucun .cs créé. Le pattern est entièrement défini en YAML. **Validation que le DSL YAML supporte un nouveau pattern sans aucun code C#**.

### 7. Templates qui restent C# (= hors scope migration)

| Pattern | Raison |
|---|---|
| `forall-belongs` | Logique custom : classification var/domain via `ClassifyArgs`, hint trailing-space, composition slot domain via `PatternRefSlot`. Hors moule "args positionnels purs". |
| `ensemble` | Logique custom : délégation `[` → interval-union, modifiers tight (R/R*/R+), word boundary spécifique. |
| `interval-union` | Eager parse récursif (= state retourné par TryMatchHead a déjà SourceEnd étendu), opérateurs U/∪/inter/∩ entre brackets. |

Ces 3 templates restent en .cs et coexistent avec les YAML. Aucun
problème — `DefaultPatternRegistry` instancie les 2 types et les ajoute
au même `PatternPipeline`.

## Tradeoff & alternatives écartées

- **Custom parser YAML** (~150 lignes) : rejeté quand on a découvert
  que YamlDotNet est déjà dans les deps. YamlDotNet est battle-tested.
- **JSON au lieu de YAML** : rejeté. Moins lisible que YAML pour ce
  cas (chaînes avec backslashes LaTeX `\square` → nécessite escape JSON).
- **Migrer aussi ForallBelongs/Ensemble/IntervalUnion en YAML** : rejeté
  pour P9e. Ces 3 templates ont des logiques **custom** qui ne rentrent
  pas dans le moule générique. Forcer leur YAML demanderait des hooks
  custom dans le DSL → mini-DSL programmable → sur-engineered. Approche
  pragmatique : C# pour custom, YAML pour purement déclaratif.
- **Locator de YAML user-side** (`~/.mathcursor/patterns/`) : rejeté
  pour P9e. Embedded uniquement. P10+ pourra ajouter un overlay user
  pour permettre la customisation sans rebuild.

## Conséquences

### Code touché

- **Nouveau** :
  - `core-csharp/src/MathCursor.Core/Patterns/Yaml/PatternSpec.cs` (~85 lignes)
  - `core-csharp/src/MathCursor.Core/Patterns/Yaml/PatternSpecLoader.cs` (~65 lignes)
  - `core-csharp/src/MathCursor.Core/Patterns/Yaml/YamlArgListPatternTemplate.cs` (~165 lignes)
  - `data/patterns/lim.yaml`, `sum.yaml`, `integral.yaml`, `derivative.yaml`, `probability.yaml`
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/ProbabilityYamlPatternTests.cs` (11 tests)
- **Modifié** :
  - `core-csharp/src/MathCursor.Core/MathCursor.Core.csproj` — ajout `EmbeddedResource` wildcard `data/patterns/*.yaml`
  - `core-csharp/src/MathCursor.Core/Patterns/DefaultPatternRegistry.cs` — refactor pour itérer sur les YAML découverts + auto-inscription
  - `core-csharp/tests/.../LimTemplateTests.cs`, `SumTemplateTests.cs`, `IntegralTemplateTests.cs`, `DerivativeTemplateTests.cs` — remplacement de `new XxxTemplate()` par `new YamlArgListPatternTemplate(PatternSpecLoader.LoadEmbedded("xxx.yaml"))`
- **Supprimé** :
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/LimTemplate.cs`
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/SumTemplate.cs`
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/IntegralTemplate.cs`
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/DerivativeTemplate.cs`

Net : ~480 lignes de C# remplacées par ~315 lignes (Yaml/ + tests probability) + 5 fichiers YAML déclaratifs.

### Tests

- **Core** : 1174/1181 verts (post-P9d = 1163/1170). Delta : **+11 nouveaux verts** (ProbabilityYamlPatternTests), 0 régression, 6 préexistants rouges idem.
- **60 tests Lim/Sum/Integral/Derivative** passent sans modifier les assertions (= comportement YAML = comportement C# précédent).
- **Adapter** : 393/393 inchangé.

### API publique

- **Nouveau public** : `PatternSpec`, `HeadSpec`, `PatternSlotSpec`, `ScoringSpec`, `RenderTemplates` (POCO), `PatternSpecLoader`, `YamlArgListPatternTemplate`.
- **Types retirés** : `LimTemplate`, `SumTemplate`, `IntegralTemplate`, `DerivativeTemplate`. Breaking change si consumer externe les référençait. Aucun connu (projet privé).
- **`DefaultPatternRegistry`** : signature publique inchangée, comportement enrichi (auto-discovery YAML).

### Régression UX

Aucune. Les 4 templates migrés produisent un comportement identique
(tests le prouvent). Les nouveaux patterns YAML (probability) sont
ajoutés. L'utilisateur Word peut désormais aussi taper `P A` →
popup `P(A)`.

### Règles MC impactées

Aucune.

## Validation post-fix

```bash
# Tests Yaml-driven (Lim/Sum/Integral/Derivative/Probability)
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~LimTemplate|FullyQualifiedName~SumTemplate|FullyQualifiedName~IntegralTemplate|FullyQualifiedName~DerivativeTemplate|FullyQualifiedName~ProbabilityYamlPattern"
# → 71/71 verts (60 ex-cs + 11 nouveaux probability)

# Suite Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1174/1181 verts (6 préexistants rouges)

# Adapter
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts
```

## Workflow utilisateur pour ajouter un pattern via YAML

1. Créer `data/patterns/mon-pattern.yaml`
2. Rebuilder MathCursor.Core (`dotnet build`)
3. C'est tout — la règle est active au prochain boot

Le wildcard MSBuild `data/patterns/*.yaml` capte automatiquement le
nouveau fichier au build. La reflection runtime
(`assembly.GetManifestResourceNames()`) découvre la nouvelle ressource
embedded. `DefaultPatternRegistry` l'inscrit automatiquement dans le
`PatternPipeline`.

**0 ligne de C# à modifier. 0 enregistrement explicite.**

## Plan Patterns — état d'avancement

- [x] **P9a** — LimTemplate (commit `154e947`, **superseded par P9e**)
- [x] **P9b** — SumTemplate (commit `57a8b6c`, **superseded par P9e**)
- [x] **P9c** — IntegralTemplate (commit `1a962ba`, **superseded par P9e**)
- [x] **P9d** — DerivativeTemplate (idem)
- [x] **P9e** — Pattern specs en YAML + auto-discovery (cet ADR) ✨
- [ ] **P8** — Validation manuelle PAP-friendly via `/build-iss` + Word
- [ ] **P10+** — Overlay user-side `~/.mathcursor/patterns/` ; migration ForallBelongs/Ensemble/IntervalUnion si pattern custom hooks DSL
