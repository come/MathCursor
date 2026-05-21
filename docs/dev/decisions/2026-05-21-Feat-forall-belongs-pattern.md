# Feat — ForallBelongsTemplate : cœur du pilote compositionnel (P5)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Meta-pattern-templates-vs-ambig-closed.md`](2026-05-21-Meta-pattern-templates-vs-ambig-closed.md) — ADR de cadrage (P5 du plan)
- [`2026-05-21-Refactor-pattern-pipeline-skeleton.md`](2026-05-21-Refactor-pattern-pipeline-skeleton.md) — P2 (squelette)
- [`2026-05-21-Feat-ensemble-pattern.md`](2026-05-21-Feat-ensemble-pattern.md) — P3 (commit `7af2b37`)
- [`2026-05-21-Feat-interval-union-pattern.md`](2026-05-21-Feat-interval-union-pattern.md) — P4 (commit `121b092`)

## Citation acté

> « oui allons y doucement.. » — utilisateur, 2026-05-21
> (cadre de la session P5)
>
> « choix 1. ce meme genre de cas vas se poser regulierement .. on ne peux pas traiter un cas generique et le peupler avec des yml plutot ?
> choix 3. on est d'accord que c'est pareil, on est sur une regle de mutation pour moi, et le pipeline doit ajouter ca en desambiguité si il la voit. eventuellement rajouter des "hint" dans les classe d'interface (surcheargeable en yml) pour muscler le poid de desambiguité. donc regle generique et du yml pour piloter. si plusieurs regles match ca passe dans la desambiguité + poids
> sinon ok sur tes reco » — utilisateur, 2026-05-21
> (vision archi data-driven + multi-completion par poids)
>
> Validation des choix de cadrage P5 via AskUserQuestion :
> - Stratégie : **γ — C# data-ready maintenant, YAML quand 3+ templates**
> - Slot var : **FilledSlotAtom "x,y,z"** (pas de sub-template dédié)
> - Désambig poids : **P5 émet N completions par poids**

## Contexte

P5 est le **cœur du pilote** validé par l'ADR cadrage P0. C'est ce qui
met en place **bout-en-bout** :

1. **Composition parent↔enfant** via `PatternRegistry`
2. **Slots optionnels avec opener** (slot `domain`)
3. **Identifier-list CSV** (slot `var`)
4. **Head polysémique V/E** (polarity)
5. **Composition de SourceMutation** (head + var + opener + sub-mutation)

Le test pilote nommé dans l'ADR cadrage — `V x app a [0,1]U[3,4]` →
`∀x ∈ [0,1]∪[3,4]` — est désormais **vert bout-en-bout** (cf. test
`ForallBelongsCompositionTests.PILOT_V_x_app_a_interval_union_end_to_end`).

## Décision

### 1. Architecture data-ready (option γ)

Au lieu d'hardcoder les heads V/E et les openers dans le template, on
les déclare comme `static readonly` arrays de **structures C# typées** :

```csharp
private static readonly QuantifierVariant[] Variants = new[]
{
    new QuantifierVariant("V", "\\forall", "forall", weight: 100),
    new QuantifierVariant("E", "\\exists", "exists", weight: 100),
    new QuantifierVariant("∀", "\\forall", "forall", weight: 100),
    new QuantifierVariant("∃", "\\exists", "exists", weight: 100),
};

private static readonly OpenerAlias[] Openers = new[]
{
    new OpenerAlias("∈",          "in", weight: 100, requiresWordBoundary: false),
    new OpenerAlias("appartient", "in", weight: 90,  requiresWordBoundary: true),
    new OpenerAlias("dans",       "in", weight: 85,  requiresWordBoundary: true),
    new OpenerAlias("(-",         "in", weight: 80,  requiresWordBoundary: false),
    new OpenerAlias("app a",      "in", weight: 70,  requiresWordBoundary: true),
    new OpenerAlias("in",         "in", weight: 60,  requiresWordBoundary: true),
};
```

`QuantifierVariant` et `OpenerAlias` sont créés comme types publics dans
`MathCursor.Core/Patterns/`. Leur forme reflète celle d'un futur YAML
(`groups/quantifier.yaml`, `groups/belonging.yaml`). Migration P9+ ne
touchera **pas** le code du template — seulement la source de ces
arrays.

Chaque variant/alias porte un `Weight` + un champ optionnel `Hints`
(dictionary string→string) **réservé** pour des annotations contextuelles
futures (locale, domaine, etc.).

### 2. Composition via `PatternRegistry` dans le contexte

`PatternScanContext` est étendu (rétro-compat) avec 2 nouveaux champs
optionnels :

- `int StartPos` (default 0) — position de scan dans la source
- `PatternRegistry? Registry` (default null) — pour la composition

Les sub-templates honorent `StartPos` (modif minimale dans
`EnsembleTemplate` et `IntervalUnionTemplate`). `ForallBelongsTemplate`
appelle `ctx.Registry?.Get("ensemble")` pour parser le slot `domain`,
qui à son tour peut déléguer à `interval-union`.

### 3. EnsembleTemplate étendu (P4.5 intégré)

`EnsembleTemplate` détecte maintenant `[` ou `(` comme heads **avant**
les lettres canoniques R/N/Z/Q/C. Si `Registry.Get("interval-union")`
retourne un template, il délègue : le PatternMatch produit contient un
slot `delegated` = `FilledSlotSubPattern(intervalMatch)`. Expand forwarde
à `intervalTemplate.Expand`.

Si `Registry` est `null` (test isolé P3), le head bracket est ignoré
(fallback transparent vers le scan R/N/Z/Q/C → null si aucune lettre).

### 4. IntervalUnionTemplate : `TryMatchHead` eager

Pour que les parents (`ForallBelongsTemplate`) puissent connaître la
fin réelle d'un sub-pattern interval **sans devoir appeler Expand**,
`IntervalUnionTemplate.TryMatchHead` fait désormais un **eager parse**
de toute la chaîne d'intervals. Le state retourné a son `SourceEnd`
étendu jusqu'à la fin de `[0,1]U[3,4]...`. `Expand` devient un pur
rendu LaTeX depuis le state final (idempotent).

Refactor invisible côté tests P4 — seul `Complete_match_has_all_4_fixed_slots_filled`
est renommé en `TryMatchHead_returns_eager_parsed_state_with_filled_slots`
pour refléter le nouveau comportement.

### 5. ForallBelongsTemplate

Algorithme d'`Expand` :

```
pos = state.SourceEnd  # après head V/E
pos = SkipWhitespace
varAtom = ParseIdentifierList(pos)  # x ou x,y ou x,y,z
  → state.var = FilledSlotAtom
pos = SkipWhitespace
matchedOpeners = FindAllMatchingOpeners(pos)  # 0 ou + (tri par weight desc)

if matchedOpeners.Count == 0:
    return [BuildCompletion(state, no_opener)]

for openerInfo in matchedOpeners:
    state' = state.WithSlot("opener", ...)
    if ctx.Registry has "ensemble":
        domainSub = ensemble.TryMatchHead(subCtx with StartPos = posAfterOpener)
        if domainSub: state'.domain = FilledSlotSubPattern(domainSub)
    completions.Add(BuildCompletion(state', openerInfo, domainSub))

return completions
```

`BuildCompletion` compose :
- `PreviewLatex` : `\forall x \in \mathbb{R}` (slots remplis seulement)
- `HintLatex` : `\forall x \in \square` si domain absent
- `Description` Unicode : `∀x∈ℝ` (carré `▭` pour empty)
- `Mutation` : **composite** — reconstitue la zone source mutée
  (`V x app a R` → `forall x in bbR`)
- `CompletenessScore` : 25 par slot rempli + pondération opener weight

### 6. Composition de SourceMutation

Décision clé : la `PatternCompletion.Mutation` du parent est **une seule
mutation qui couvre toute la zone du pattern parent**. Elle absorbe les
sub-mutations en concaténant les replacements :

```
"V x app a R" → "forall x in bbR"
                  ^         ^^^
              head→forall   sub-mutation ensemble R→bbR
```

Pour les intervals, sub-mutation null → on reproduit la source telle quelle :

```
"V x app a [0,1]U[3,4]" → "forall x in [0,1]U[3,4]"
```

Le caller (P7 ou plus tard) applique **une seule mutation** par
PatternCompletion. Pas besoin d'un `MutationCollector` récursif.

### 7. Multi-completion par poids (préparation désambig)

Le mécanisme `FindAllMatchingOpeners` retourne **tous** les openers
qui matchent à la position, triés par `Weight` desc. Le template émet
**une PatternCompletion par opener**.

En pratique avec les 6 aliases actuels, les premiers chars sont tous
distincts → 1 match maximum. Mais le mécanisme est prêt pour quand
des aliases ambigus seront ajoutés (ex. `in` vs `intersect` qui se
chevauchent partiellement).

## Tradeoff & alternatives écartées

Évaluation sur qualité + perf + extensibilité.

- **YAML now (option α)**. Rejeté pour P5 : invente le DSL en même
  temps qu'on découvre le concept. Reportée à P9+ quand 3+ templates
  similaires auront confirmé le schéma. Migration C# → YAML restera 1
  commit (les structures C# `QuantifierVariant`/`OpenerAlias` ont déjà
  la forme YAML cible).

- **Hardcoded inline (option β)**. Rejeté : dette technique immédiate.
  Reproduit le piège V→∀ legacy.

- **`PatternMatch.FinalState` retourné par Expand**. Rejeté : casse le
  contrat IPatternTemplate P2. Solution alternative retenue : `TryMatchHead`
  eager (parse complet, state pre-extended). Permet aux parents de
  connaître `SourceEnd` sans appeler Expand.

- **`PatternCompletion.Mutations` liste**. Rejeté : casse le contrat
  P2/P3/P4. Solution alternative : mutation composite (la mutation
  parent englobe toutes les sub-mutations dans son string replacement).

- **Slot var = sous-template `IdentifierListTemplate`**. Rejeté pour P5
  : YAGNI pour des identifiers simples. `FilledSlotAtom` avec texte CSV
  "x,y,z" suffit. Migration possible si P9 a un besoin granulaire.

- **Coexistence `VAsForallEAsExistsScanner` + `ForallBelongsTemplate`**.
  Rejeté (cf. ADR cadrage) : 2 sources d'ambig sur le même token V.
  Le retrait du scanner legacy est planifié en P6.

## Conséquences

### Code touché

- **Nouveau** (~370 lignes) :
  - `core-csharp/src/MathCursor.Core/Patterns/QuantifierVariant.cs` (~60 lignes)
  - `core-csharp/src/MathCursor.Core/Patterns/OpenerAlias.cs` (~70 lignes)
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/ForallBelongsTemplate.cs` (~310 lignes)
- **Modifié** :
  - `core-csharp/src/MathCursor.Core/Patterns/PatternScanContext.cs` — ajout `StartPos`, `Registry`, ctor étendu, helper `WithStartPos`
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/EnsembleTemplate.cs` — head `[`/`(` qui délègue à interval-union
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/IntervalUnionTemplate.cs` — `TryMatchHead` eager + `Expand` simplifié
- **Nouveau tests** (~47 nouveaux verts) :
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/ForallBelongsTemplateTests.cs` (~24 tests unitaires)
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/ForallBelongsCompositionTests.cs` (~14 tests bout-en-bout)

### Tests

- **Core** : 1108/1115 verts (post-P4 = 1061/1068). Delta : **+47 nouveaux verts**, 0 régression, 6 préexistants rouges idem.
- **Adapter** : 393/393 inchangé.

**Test pilote** :
```
PILOT_V_x_app_a_interval_union_end_to_end
  Source : "V x app a [0,1]U[3,4]"
  Result PreviewLatex : @"\forall x \in \left[0,1\right] \cup \left[3,4\right]"
  Result Description  : "∀x∈[0,1]∪[3,4]"
  → ✓ Vert
```

### API publique

- 3 nouveaux types publics : `QuantifierVariant`, `OpenerAlias`, `ForallBelongsTemplate`.
- 2 nouveaux champs publics : `PatternScanContext.StartPos`, `PatternScanContext.Registry`.
- 1 nouvelle méthode publique : `PatternScanContext.WithStartPos`.
- Rétro-compat : ctor 4-args existant préservé, default null pour `Registry`, default 0 pour `StartPos`.

### Règles MC impactées

- Aucune. Les templates émettent `SourceMutation` (par construction MC0006-conformes), pas de splice.

### Performance

- `ForallBelongsTemplate.TryMatchHead` : O(n × variants) ≤ O(4n) sur source, early-exit.
- `Expand` : O(openers + 1 sub-parse). 1 allocation par MatchedOpener.
- `EnsembleTemplate` étendu : 1 dispatch supplémentaire si Registry présent (Get O(1)).
- `IntervalUnionTemplate.TryMatchHead` eager : O(n) total (same complexity, juste timing différent).

### Compositionnalité validée

L'ADR cadrage demandait la validation de 3 nouveaux concepts via le
pilote :
1. **Pattern compositionnel** ✓ (forall-belongs → ensemble → interval-union)
2. **Slot optionnel avec opener** ✓ (`app a` / `∈` / `dans` / ...)
3. **Sub-pattern autonome** ✓ (ensemble + interval-union testables indépendamment)

Le pilote est **bout-en-bout fonctionnel** au niveau Core. Reste à
brancher au popup (P7) pour validation user.

## Validation post-fix

```bash
# Build
dotnet build core-csharp/src/MathCursor.Core/MathCursor.Core.csproj
# → 8 warnings préexistants, 0 erreur

# Tests ForallBelongs (unitaires + composition)
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~ForallBelongs"
# → 38/38 verts (24 unit + 14 composition)

# Test pilote nommé dans ADR cadrage
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~PILOT_V_x_app_a_interval_union_end_to_end"
# → 1/1 vert

# Suite Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1108/1115 verts (6 préexistants rouges)

# Adapter
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts
```

## Plan Patterns — état d'avancement (ROADMAP Chantier 6)

- [x] **P0** — Attendre commit stable du WIP popup (commits `817c4d3`/`8477602`/`538f61e`)
- [x] **P1** — Caret-aware `ZoneResolver` + `CaretLocator` (commit `607a6f8`)
- [x] **P2** — Squelette `Patterns/` (commit `023e03d`)
- [x] **P3** — `EnsembleTemplate` (commit `7af2b37`)
- [x] **P4** — `IntervalUnionTemplate` (commit `121b092`)
- [x] **P5** — `ForallBelongsTemplate` + P4.5 intégré (cet ADR)
- [ ] **P6** — Retrait scanners V + canonical-set legacy
- [ ] **P7** — Popup consomme `PatternCompletion` + carrés
- [ ] **P8** — Test bout-en-bout dans Word
- [ ] **P9+** — `LimTemplate`, `SumTemplate`, etc. + migration YAML
