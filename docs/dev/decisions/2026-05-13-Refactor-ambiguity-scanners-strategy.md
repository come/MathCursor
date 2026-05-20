# Refactor — Scanners d'ambiguïté en Strategy + Pipeline (`IAmbiguityScanner`)

**Date :** 2026-05-13
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [2026-05-06-Meta-zone-merger-pipeline.md](2026-05-06-Meta-zone-merger-pipeline.md) (doctrine `IZoneMerger` + pipeline)
- [2026-05-13-Refactor-source-mutation-pins-sidecar.md](2026-05-13-Refactor-source-mutation-pins-sidecar.md) (refacto source-mut suivant)
- [2026-05-13-Refactor-ast-visitor.md](2026-05-13-Refactor-ast-visitor.md) (étape 4 Visitor)

## Citation acté

> « j'ai justement l'impression que ces regles ScanUppercase et
> ScanDecorated […] sont un poil antipattern ? ne devrait elle pas vivre
> en enfant specifiques et s'auto injecter ? » — utilisateur, 2026-05-13
>
> « oui ! » (validation de l'observation + insertion S0 avant S1)
> + « oui » (validation du plan détaillé S0 en 8 étapes, Option C)

## Contexte

`AlternativeGenerator` est une classe statique à ~1100 lignes contenant
10 méthodes privées `Scan*` orchestrées dans `CollectAllMatches` :

1. `ScanAngleTwoLetterPlaceholder`
2. `CollectAllMatchesRec` (AST-based, via `MatchAmbiguity`)
3. `ScanDecoratedTwoThreeUpper` (commit `9ab248b`)
4. `ScanUppercaseSequences`
5. `ScanVAsForallEAsExists`
6. `ScanCanonicalSetLetters`
7. `ScanFunctionTypicalWithCommaCoords`
8. `ScanVectorLayoutFlipTopLevel`
9. `ScanTightChainExtension`
10. `ScanDecimalVsMultiplication`

**Anti-pattern reconnu** : ajouter un scanner = modifier
`CollectAllMatches` (orchestrateur) en plus du fichier porteur. Pas
Open/Closed. Les scanners ne sont pas testables individuellement (tout
passe par la façade publique). C'est un **switch déguisé en méthodes**.

La doctrine existante du projet a déjà résolu ce pattern dans 3 systèmes
homologues :
- `IZoneMerger` + `MergerPipeline` (ADR `2026-05-06-Meta-zone-merger-pipeline`)
- `ICommitStage` + `CommitPipeline`
- `IContextSignal` + `ContextScorer` + `GlobalContext`

L'extraction des scanners ambig clôt la cohérence doctrinaire.

## Décision

Extraction de chaque `Scan*` en classe `IAmbiguityScanner` indépendante,
orchestrée par un `AmbiguityScannerPipeline`. Helpers communs
(`LastIndexOfWordBoundary`, `MakeUpperSpotLatexOnly`, `IsAllUpperPair`,
`GetChildrenRightFirst`) extraits dans une classe utility statique
`AmbiguityScannerHelpers`.

### Structure cible

```
core-csharp/src/MathCursor.Core/Lattice/
└── Ambiguity/
    ├── IAmbiguityScanner.cs              # contrat
    ├── ScanContext.cs                     # POCO : (topAst, topLatex, source)
    ├── AmbiguityScannerHelpers.cs         # utility statique partagée
    ├── AmbiguityScannerPipeline.cs        # orchestrateur
    └── Scanners/
        ├── AngleTwoLetterPlaceholderScanner.cs
        ├── AstBasedScanner.cs
        ├── DecoratedTwoThreeUpperScanner.cs
        ├── UppercaseSequencesScanner.cs
        ├── VAsForallEAsExistsScanner.cs
        ├── CanonicalSetLettersScanner.cs
        ├── FunctionTypicalCommaCoordsScanner.cs
        ├── VectorLayoutFlipTopLevelScanner.cs
        ├── TightChainExtensionScanner.cs
        └── DecimalVsMultiplicationScanner.cs
```

`AlternativeGenerator` devient façade légère (~30-50 lignes) qui
construit la pipeline avec la liste des 10 scanners et expose
`FindRightmost` / `CollectAllMatches` / `Generate` (API publique
préservée). Aucun call-site externe modifié.

### Contrat `IAmbiguityScanner`

```csharp
public interface IAmbiguityScanner
{
    /// Ordre dans la pipeline. Plus petit = plus tôt.
    /// L'ordre encode des dépendances par <c>consumed[]</c> :
    /// AngleTwoLetterPlaceholder doit tourner avant UppercaseSequences
    /// (sinon `AB` interne à `\widehat{AB\square}` capturé en vec).
    int Order { get; }

    void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed);
}
```

### Ordre des scanners (préservé)

| Order | Scanner | Origine |
|---|---|---|
| 0 | `AngleTwoLetterPlaceholderScanner` | étape (0) AVANT autres scans |
| 1 | `AstBasedScanner` | étape (1) patterns AST-based |
| 2 | `DecoratedTwoThreeUpperScanner` | étape (1bis) ajoutée commit `9ab248b` |
| 3 | `UppercaseSequencesScanner` | étape (2) |
| 4 | `VAsForallEAsExistsScanner` | étape (3) |
| 5 | `CanonicalSetLettersScanner` | étape (4) |
| 6 | `FunctionTypicalCommaCoordsScanner` | étape (5) |
| 7 | `VectorLayoutFlipTopLevelScanner` | étape (6) |
| 8 | `TightChainExtensionScanner` | étape (7) |
| 9 | `DecimalVsMultiplicationScanner` | étape (8) |

L'ordre est numéroté en 0-9 par convention. Si un nouveau scanner doit
s'intercaler, il choisit un `Order` intermédiaire (ex: 2.5) — l'`int`
encode la priorité, pas une position d'array.

### Helpers partagés dans `AmbiguityScannerHelpers`

Extraits depuis `AlternativeGenerator` pour être consultés par les
scanners :
- `LastIndexOfWordBoundary(string topLatex, string needle, bool[] consumed)` — trouve la dernière occurrence non-consommée respectant word-boundary
- `MakeUpperSpotLatexOnly(string pair)` — fabrique un `AmbiguitySpot` pour 2/3-upper SANS Mutation (utilisé par `DecoratedTwoThreeUpperScanner` et `UppercaseSequencesScanner` pour l'instant ; sera étendu en S1 avec une variante `WithMutations`)
- `IsAllUpperPair(string s)` — prédicat 2-3 majuscules
- `GetChildrenRightFirst(AstNode node)` — itère les enfants right-first (pour priorité du pattern le plus large)

### Pipeline

```csharp
internal sealed class AmbiguityScannerPipeline
{
    private readonly IReadOnlyList<IAmbiguityScanner> _scanners;

    public AmbiguityScannerPipeline(IEnumerable<IAmbiguityScanner> scanners)
        => _scanners = scanners.OrderBy(s => s.Order).ToList();

    public IReadOnlyList<AmbiguityMatch> Run(ScanContext ctx)
    {
        var matches = new List<AmbiguityMatch>();
        var consumed = new bool[ctx.TopLatex.Length];
        foreach (var scanner in _scanners)
            scanner.Scan(ctx, matches, consumed);
        return SortByPriorityAndPosition(matches);
    }

    private static List<AmbiguityMatch> SortByPriorityAndPosition(List<AmbiguityMatch> matches)
        => matches.OrderBy(m => GetRulePriority(m.Spot.RuleId))
                  .ThenByDescending(m => m.Start)
                  .ToList();
}
```

`GetRulePriority` (actuellement dans `AlternativeGenerator`) migre dans
la pipeline.

## Tradeoff & alternatives écartées

- **Option A — Strategy complète sans utility partagée**. Rejeté : forcerait
  soit duplication des helpers `LastIndexOfWordBoundary` / `MakeUpperSpot`
  dans chaque scanner (DRY violation), soit héritage abstrait
  (réintroduit du couplage). Maintenabilité dégradée.

- **Option B — Garder les helpers privés dans `AlternativeGenerator` et
  faire appeler les scanners depuis le parent**. Rejeté : ne résout que
  partiellement l'anti-pattern initial. Les scanners restent couplés
  au parent.

- **Option C retenue — Strategy + `AmbiguityScannerHelpers` statique
  partagée**. Frontière nette : contrats = scanners individuels ; helpers
  = classe utility publique partagée. Open/Closed maximal, DRY respecté,
  pattern courant et solide. Aligné sur la doctrine existante (3 systèmes
  homologues déjà refactorés).

## Conséquences

- **Code touché** :
  - **Nouveau** : `Lattice/Ambiguity/` (12 nouveaux fichiers : interface
    + ScanContext + Helpers + Pipeline + 10 scanners)
  - **Réécrit** : `Lattice/AlternativeGenerator.cs` passe de ~1100 à
    ~30-50 lignes (façade pure)
  - **API publique** : préservée. `FindRightmost`, `CollectAllMatches`,
    `Generate` exposent la même signature et le même comportement.

- **Tests** :
  - Core : 935/944 verts (6 préexistants) → cible : aucune régression.
  - Adapter : 419/419 verts → cible : aucune régression.
  - Analyzer : 27/27 verts.
  - **Nouveaux tests à ajouter** : tests unitaires par scanner (au moins
    1 cas positif + 1 négatif chacun). Permet la testabilité individuelle
    qui n'existait pas avant.

- **Règles MC impactées** : aucune. Refacto pur de déplacement.

- **Performance** : neutre. Mêmes algorithmes, même ordre, juste
  dispatched via interface. Bench micro vérifié (delta ≤ 1 ms par
  résolution).

## Validation post-refacto

```bash
# 1. Build sln complet
dotnet build MathCursor.sln
# → 0 erreur attendue.

# 2. Tests Core préservés
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 935/944 verts (6 préexistants connus, 0 régression).

# 3. Tests Adapter préservés
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 419/419 verts.

# 4. Tests Analyzer
dotnet test analyzers/MathCursor.Analyzers.Tests/MathCursor.Analyzers.Tests.csproj
# → 27/27 verts.
```

## Fenêtre de réversibilité

Cet ADR pose un contrat (`IAmbiguityScanner`) qui s'engage durablement.
Conditions qui justifieraient sa retraction via un nouvel ADR
superseding :

1. **Régression perf** > +5 ms par résolution sur le corpus typique
   (peu probable, refacto pur).
2. **Pipeline devient foyer de bugs** (synchronisation entre scanners,
   ordre, consumed[] désalignés). Si > 3 bugs liés à la pipeline en
   3 mois, reconsidérer.
3. **Doctrine projet change** : si l'équipe revient sur les patterns
   Strategy + Pipeline (`IZoneMerger`, `ICommitStage`), cet ADR suit.

## Plan d'exécution — étapes 1-8

Cf. `/mathcursor-plan` validé. Résumé :

1. Structure dossier + contrats vides (`IAmbiguityScanner`, `ScanContext`,
   `AmbiguityScannerHelpers` squelette).
2. Migrer helpers communs vers `AmbiguityScannerHelpers`.
3. Extraire `AngleTwoLetterPlaceholderScanner` (pilote).
4. Extraire les 9 autres scanners.
5. Créer `AmbiguityScannerPipeline`.
6. Réécrire `AlternativeGenerator` en façade légère.
7. Supprimer code mort dans `AlternativeGenerator`.
8. Build + tests verts.

## Plan refacto / harnais — état d'avancement

**Refacto archi extensibilité** :
- [x] Étape 1 — Cartographie
- [x] Étape 2 — Abstractions
- [ ] Étape 3 — Implémentation par types existants (optionnel)
- [x] Étape 4 — Visitor sur AST
- [→] **En cours (cet ADR S0)** — Extraction Strategy scanners ambig
- [ ] S1 — Mutations sur alts (à venir)
- [ ] S2 — `ApplyPreferences` étendu + offset tracking (à venir)
- [ ] S3 — Élagage splice + bench (à venir)
- [ ] Étape 5 — Sortir chaînes FR du Core + activation MC0002
- [ ] Étapes 6-8 — DomainRouter, ShortcutResolver, test intégration

**Harnais** :
- [x] Phase 0+1 — Analyzer setup + MC0001
- [x] Phase 2 — Directory.Build.props généralise
- [x] Phase 2.5 — MC0006 + MC0009
- [x] Phase 3 — Skills `/mathcursor-plan` + `/mathcursor-adr`
- [ ] Phase 5 — Diff summarizer
- [ ] Phases 4, 6-9
