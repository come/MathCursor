# Refactor — S1 : `ScanUppercaseSequences` source-based + Mutations sur vec/paren

**Date :** 2026-05-13
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [`2026-05-13-Refactor-source-mutation-pins-sidecar.md`](2026-05-13-Refactor-source-mutation-pins-sidecar.md) (plan macro 3 sous-livraisons S1-S2-S3) + [`2026-05-13-Refactor-ambiguity-scanners-strategy.md`](2026-05-13-Refactor-ambiguity-scanners-strategy.md) (S0 Strategy)

## Citation acté

> « ok go S1 » — utilisateur, 2026-05-13 (lancement S1)
> « A » — utilisateur, 2026-05-13 (validation Option A : scan source-based modèle `ScanVAsForallEAsExists`)
> « go ! » — utilisateur, 2026-05-13 (validation suppression `Proof_*` + un-skip `Bug_*`)
> « yes MAJ puis commit » — utilisateur, 2026-05-13 (validation commit final S1)

## Contexte

Sous-livraison 1 du plan macro source-mutation des pins sidecar. État
pré-S1 :
- `RuleTwoUppercase` (`AB` → vec/paren/bracket) émettait des alternatives
  **sans `SourceMutation`** — la pref vec ne se propageait pas au top
  render quand l'utilisateur tapait `AddPreference(RuleTwoUppercase, 0)`.
- `ZoneResolver.ApplyPreferences` court-circuitait sur `alt.Mutation == null`.
- Tests `Bug_*` (`Skip = "..."`) documentaient le comportement souhaité,
  tests `Proof_*` documentaient le bug en l'asserant.

S0 (ADR `Refactor-ambiguity-scanners-strategy`) avait extrait les 10
scanners en `IAmbiguityScanner` mais conservé `ScanUppercaseSequences`
sur scan `topLatex`-based. S1 le bascule en source-based pour aligner
sur la doctrine ADR macro et activer la Mutation.

## Décision

### 1. `MakeUpperSpot` accepte un `int? sourceOffset` optionnel

```csharp
private static AmbiguitySpot MakeUpperSpot(string pair, int? sourceOffset = null)
{
    if (pair.Length == 2)
    {
        SourceMutation? vecMut = sourceOffset.HasValue
            ? new SourceMutation(sourceOffset.Value, 2, "vec " + pair)
            : null;
        SourceMutation? parenMut = sourceOffset.HasValue
            ? new SourceMutation(sourceOffset.Value, 2, "(" + pair + ")")
            : null;
        return new AmbiguitySpot(
            ruleId: RuleTwoUppercase,
            defaultLatex: pair,
            alternatives: new[]
            {
                new AmbiguityAlternative($"\\vec{{{pair}}}", vecMut),
                new AmbiguityAlternative($"\\left({pair}\\right)", parenMut),
                new AmbiguityAlternative($"\\left[{pair}\\right]"),
            });
    }
    // length == 3 — S1 garde le splice latex pour widehat/triangle.
    return new AmbiguitySpot(RuleThreeUppercase, pair,
        new[]
        {
            new AmbiguityAlternative($"\\widehat{{{pair}}}"),
            new AmbiguityAlternative($"\\triangle {pair}"),
        });
}
```

Bracket reste sans Mutation : `[AB]` parsé en intervalle FR, pas de
forme source naturelle équivalente.

Les callers existants de `MakeUpperSpot` dans `ScanDecoratedWalk`
(décorations déjà posées par l'user en source) appellent toujours sans
`sourceOffset` → alts émises sans Mutation, comportement préservé
(splice latex no-op = identité quand l'alt == décoration courante,
fix double-wrap intact).

### 2. `ScanUppercaseSequences` réécrit en source-based

```csharp
internal static void ScanUppercaseSequences(string source, string topLatex,
    List<AmbiguityMatch> output, bool[] consumed)
{
    int i = 0;
    while (i < source.Length)
    {
        if (!char.IsUpper(source[i])) { i++; continue; }
        if (i > 0 && char.IsLetter(source[i - 1])) { i++; continue; }
        int j = i;
        while (j < source.Length && char.IsUpper(source[j])) j++;
        int len = j - i;
        if (j < source.Length && char.IsLetter(source[j])) { i = j; continue; }
        if (len != 2 && len != 3) { i = j; continue; }

        var pair = source.Substring(i, len);

        // Map source pos → topLatex pos (trivial si pair non décalée).
        int topPos;
        bool trivialMap = i + len <= topLatex.Length
                          && string.CompareOrdinal(topLatex, i, pair, 0, len) == 0;
        if (trivialMap) topPos = i;
        else
        {
            topPos = LastIndexOfWordBoundary(topLatex, pair, consumed);
            if (topPos < 0) { i = j; continue; }
        }

        bool free = topPos + len <= consumed.Length;
        for (int k = topPos; free && k < topPos + len; k++)
            if (consumed[k]) free = false;
        if (!free) { i = j; continue; }

        var spot = MakeUpperSpot(pair, sourceOffset: i);
        for (int k = topPos; k < topPos + len; k++) consumed[k] = true;
        output.Add(new AmbiguityMatch(spot, topPos, topPos + len));
        i = j;
    }
}
```

Word boundaries calculées sur la **source** (vérité user), mapping
topLatex utilisé uniquement pour le `Start`/`End` du `AmbiguityMatch`
(consommé par le splice latex de fallback / le tracking d'ambig active).

### 3. Wrapper Scanner

```csharp
public void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed)
    => AlternativeGenerator.ScanUppercaseSequences(ctx.Source, ctx.TopLatex, output, consumed);
```

### 4. Tests adaptés

- Suppression des 2 `Proof_*` qui assertaient le bug (devenu non-pertinent).
- Un-skip de 2 `Bug_*` single-line qui passent désormais
  (`VecPreference_on_uppercase_pair_propagates_to_top`,
  `SingleLine_uppercase_chain_with_vec_pref`).
- Re-skip ciblé du `Bug_*` cross-merge multi-ligne avec annotation
  explicite : « S1 fixe le single-line, le cross-merge multi-ligne
  demande l'extension `ApplyAllMutations` (sous-livraison S2) ».
- Ajout `UppercaseSequencesSourceMutationTests` (4 tests) qui verrouille
  les invariants de scope S1 :
  - `S1_TwoUpperPair_VecAlt_HasSourceMutation`
  - `S1_TwoUpperPair_ParenAlt_HasSourceMutation`
  - `S1_TwoUpperPair_BracketAlt_HasNoSourceMutation`
  - `S1_ThreeUpperTriplet_AltsHaveNoSourceMutation`

## Tradeoff & alternatives écartées

Évaluation faite sur qualité + perf + maintenabilité (jamais sur temps
d'implémentation, cf. principe utilisateur 2026-05-13).

- **Option B — Scan topLatex + IndexOf source en post**. Rejeté : le
  topLatex peut contenir des décorations (`\left(`, `\right)`) qui
  perturbent la word-boundary, alors que la source est la vérité user
  intacte. Aligne aussi avec `ScanVAsForallEAsExists` qui scanne déjà
  source.

- **Ajout du keyword `triangle` au Vocabulary**. Reporté à S2 : pour
  S1 on ne consomme pas de Mutation triangle (RuleThreeUppercase reste
  sur splice latex), donc ajouter la clé serait du code mort (YAGNI).
  Sera ajouté quand l'op `triangle` aura un consumer en aval.

- **Étendre la Mutation à `ScanDecoratedWalk` (Group/Vec/Angle)**.
  Rejeté pour S1 : `ScanDecoratedWalk` détecte les pairs **déjà décorées
  en source** (`(AB)`, `\vec{AB}`, etc.). Pour ces cas, le splice latex
  avec l'alt = décoration courante est déjà no-op (identité), donc
  l'absence de Mutation n'a aucun effet utilisateur visible. Le bug
  double-wrap (fix commit `9ab248b`) reste résolu par construction.

## Conséquences

### Code touché

- `core-csharp/src/MathCursor.Core/Lattice/AlternativeGenerator.cs`
  - `MakeUpperSpot(string pair, int? sourceOffset = null)` — nouveau
    paramètre, attache Mutations sur vec et paren si `sourceOffset`
    fourni
  - `ScanUppercaseSequences(string source, string topLatex, …)` —
    signature et corps réécrits en source-based
- `core-csharp/src/MathCursor.Core/Lattice/Ambiguity/Scanners/UppercaseSequencesScanner.cs`
  - Wrapper passe `ctx.Source`
- `core-csharp/tests/MathCursor.Core.Tests/Lattice/MultiLineVecPreferenceBugTests.cs`
  - Allègement : 2 `Proof_*` supprimés, 3 `Bug_*` un-skippés
    (1 re-skip ciblé pour S2)
- `core-csharp/tests/MathCursor.Core.Tests/Lattice/UppercaseSequencesSourceMutationTests.cs`
  - Nouveau : 4 tests verrouillant les invariants S1

### Tests

- Core : **939/946 verts** (baseline 935/944), 6 fails préexistants
  inchangés, 1 ignoré (cross-merge S2).
  → +4 tests verts nets vs baseline = mes 4 nouveaux S1.
- Adapter / Analyzer : non touchés directement.

### API publique

- `LatticeEngine.ConvertWithAmbiguity` : signature inchangée,
  comportement enrichi (alts vec/paren portent désormais une Mutation
  quand origine = `ScanUppercaseSequences`).
- `AmbiguityAlternative.Mutation` : pas de changement de type, plus
  souvent non-null pour les pairs deux-upper.

### Règles MC impactées

- **MC0006** : inchangé sur `ZoneResolver:205` — l'élimination est
  pour S3 (`Refactor-source-mutation-pins-sidecar` étape « Élagage
  splice loop »).
- **MC0001 / MC0009** : aucun impact.

## Validation post-fix

```bash
# Tests Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 939/946 verts (6 préexistants hors scope, 1 ignoré S2)

# Effet user single-line : pref vec sur AB
# (test S1_TwoUpperPair_VecAlt_HasSourceMutation + Bug_VecPreference…)
# → Resolve("AB") avec pref RuleTwoUppercase=0 rend "\\vec{AB}"
```

## Plan macro source-mutation — état d'avancement

- [x] **S0** — Strategy `IAmbiguityScanner` + Pipeline (commit `e5b8ee7`)
- [x] **S1** — Mutations vec/paren sur `ScanUppercaseSequences` source-based (cet ADR)
- [ ] **S2** — `ApplyAllMutations` étendu (cross-merge multi-ligne + sidecar)
- [ ] **S3** — Élagage splice loop + bench perf + retrait MC0006 du `WarningsNotAsErrors`
