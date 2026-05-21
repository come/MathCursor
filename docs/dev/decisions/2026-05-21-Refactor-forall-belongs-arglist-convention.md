# Refactor — ForallBelongs : convention "args séparés par espaces" (P5R)

**Date :** 2026-05-21
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** [`2026-05-21-Feat-forall-belongs-pattern.md`](2026-05-21-Feat-forall-belongs-pattern.md) (P5) — partiellement
**Lié à :**
- ADR cadrage [`2026-05-21-Meta-pattern-templates-vs-ambig-closed.md`](2026-05-21-Meta-pattern-templates-vs-ambig-closed.md)
- P5 commit `417e373` (ForallBelongsTemplate v1 avec openers)

## Citation acté

> « je verrais plus le forall comme un pattern type limite avec des arguments facultatifs separés par des espaces » — utilisateur, 2026-05-21

Choix validés via AskUserQuestion :
- **Retrait total des openers** (app a / appartient / dans / in / (- / ∈)
- **Dernier arg = ensemble identifié → c'est le domain** (convention de discrimination)
- **Généraliser via `ArgListPatternBase`** abstrait, partagé avec futurs Lim/Sum/Int

## Contexte

P5 (commit `417e373`) avait défini `ForallBelongsTemplate` avec un slot
`opener` matchant 6 alias textuels : `app a`, `appartient`, `dans`,
`in`, `(-`, `∈`. Le user a observé après usage que ce modèle est **moins
naturel** que le moule head + args séparés par espaces qu'on aura pour
`Lim x 0 f(x)`, `sum k 0 n k²`, `int 0 1 f(x)`, etc.

> "Je verrais plus le forall comme un pattern type limite avec des arguments
> facultatifs séparés par des espaces."

Cohérent avec la doctrine projet "rapidité de saisie" (cf. mémoire
`project_positioning_speed`) : `V x R` (5 chars) < `V x app a R`
(11 chars) — gain de 6 chars par formule.

## Décision

### 1. Nouveau modèle args positionnels

L'utilisateur sépare les arguments par espaces (et/ou virgules pour les
vars en CSV inline) :

| Saisie | Args bruts | Interprétation | Résultat |
|---|---|---|---|
| `V` | [] | head seul, slot var vide | ∀▭ (hint avec carré) |
| `V x` | [x] | 1 var, pas de domain | ∀x |
| `V x R` | [x, R] | var + domain (R reconnu ensemble) | ∀x ∈ ℝ |
| `V x y` | [x, y] | 2 vars (y pas ensemble) | ∀x,y |
| `V x y R` | [x, y, R] | 2 vars + domain | ∀x,y ∈ ℝ |
| `V x [0,1]` | [x, [0,1]] | var + domain | ∀x ∈ [0,1] |
| `V x [0,1]U[3,4]` | [x, [0,1]U[3,4]] | var + domain interval-union | ∀x ∈ [0,1]∪[3,4] |
| `V x,y R` | [x,y, R] | 1 arg CSV "x,y" + domain | ∀x,y ∈ ℝ |
| `V x R*` | [x, R*] | var + domain modifié | ∀x ∈ ℝ* |

### 2. Convention discrimination var vs domain

Le **dernier** arg est classifié comme domain **si et seulement si** il
matche un sub-pattern `ensemble` (= R/N/Z/Q/C avec/sans modifier, ou
intervalle `[...]`/`[..]U[..]`). Cette classification passe par
`ctx.Registry.Get("ensemble").TryMatchHead`.

Si Registry absent ou pas de match : tous les args = vars.

Cas-piège résolu naturellement : `V x N` → ∀x ∈ ℕ (N reconnu ensemble),
`V x n` → ∀x,n (n pas reconnu).

### 3. Abstract `ArgListPatternBase`

Nouvelle classe abstraite dans `core-csharp/src/MathCursor.Core/Patterns/Templates/` :

```csharp
public abstract class ArgListPatternBase : IPatternTemplate
{
    public abstract string TemplateId { get; }
    public virtual int Order => 0;
    protected abstract IReadOnlyList<QuantifierVariant> Heads { get; }

    // TryMatchHead implémenté en commun (head detection avec boundary)
    public PatternMatch? TryMatchHead(PatternScanContext ctx) { ... }

    // Expand abstrait — chaque sub-class fait son rendu propre
    public abstract IReadOnlyList<PatternCompletion> Expand(...);

    // Helpers protected pour les sub-classes :
    protected static IReadOnlyList<ArgSpan> ParseArgs(string src, int pos);
    protected static ArgClassification ClassifyArgs(IReadOnlyList<ArgSpan>, PatternScanContext);
}
```

`ArgListPatternBase.ParseArgs` :
- Split par whitespace
- Mais un block crocheté/parenthésé `[...]` ou `(...)` = 1 arg atomique
- Extension chaîne union/inter : `[0,1]U[3,4]` reste 1 seul arg (= permet la composition interval-union dans un seul slot domain)

`ArgListPatternBase.ClassifyArgs` :
- Dernier arg testé via `Registry.Get("ensemble").TryMatchHead`
- Match exact des bornes source → c'est le domain
- Sinon → tous les args = vars

### 4. ForallBelongsTemplate hérite de la base

```csharp
public sealed class ForallBelongsTemplate : ArgListPatternBase
{
    public override string TemplateId => "forall-belongs";
    
    private static readonly QuantifierVariant[] _variants = new[] {
        new QuantifierVariant("V", "\\forall", "forall", weight: 100),
        new QuantifierVariant("E", "\\exists", "exists", weight: 100),
        new QuantifierVariant("∀", "\\forall", "forall", weight: 100),
        new QuantifierVariant("∃", "\\exists", "exists", weight: 100),
    };
    protected override IReadOnlyList<QuantifierVariant> Heads => _variants;

    public override IReadOnlyList<PatternCompletion> Expand(...)
    {
        var rawArgs = ParseArgs(ctx.Source, state.SourceEnd);
        var classification = ClassifyArgs(rawArgs, ctx);
        // ... build state final + completion ...
    }
}
```

### 5. Retrait `OpenerAlias.cs`

Le fichier `core-csharp/src/MathCursor.Core/Patterns/OpenerAlias.cs`
n'a plus aucun caller après le refacto. Supprimé.

Si plus tard P9+ a besoin d'un mécanisme d'aliases pour des patterns
autres (ex. `sum` / `somme` / `Σ`), on créera une abstraction
équivalente — mais probablement directement intégrée à `QuantifierVariant`
ou similaire, pas un type séparé.

### 6. Tests réécrits

- `ForallBelongsTemplateTests.cs` : 15 tests (vs 24 pré-refacto). Retrait
  des 6 cas `Each_opener_recognized_*` (theory) + cas individuels par opener.
- `ForallBelongsCompositionTests.cs` : 17 tests réécrits avec convention
  espace. Test pilote `V x [0,1]U[3,4]` → ∀x ∈ [0,1]∪[3,4] préservé.
- `ZoneResolverPatternsIntegrationTests.cs` : 4 sources mises à jour
  (`V x app a R` → `V x R`, `E x app a N` → `E x N`, etc.).

## Tradeoff & alternatives écartées

- **Coexistence openers + convention espace** : rejetée. « Tu pollues le
  modèle pour rien » + brouille la doc. La convention espace est
  suffisante.
- **Retrait des mots openers mais garder `∈` unicode** : rejetée. La
  convention espace fait le travail ; ajouter `∈` comme cas spécial
  rallonge la doc sans gain mesurable.
- **Discrimination basée sur typage explicite par token** (ex. lettres
  majuscules = vars, minuscules + nombres = bornes) : rejetée. Trop
  rigide pour des bornes `x` (= identifier minuscule mais souvent var).
- **Chaque template implémente sa logique args sans base abstraite** :
  rejetée. Lim/Sum/Int feront face aux mêmes patterns args → factoriser
  vaut le coup.

## Conséquences

### Code touché

- **Nouveau** :
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/ArgListPatternBase.cs` (~225 lignes)
- **Modifié** :
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/ForallBelongsTemplate.cs` — refactor complet, hérite de la base, ~220 → ~250 lignes (similaire mais structure différente)
- **Supprimé** :
  - `core-csharp/src/MathCursor.Core/Patterns/OpenerAlias.cs` (~75 lignes — code mort)
- **Tests** :
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/ForallBelongsTemplateTests.cs` — réécrit
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/Templates/ForallBelongsCompositionTests.cs` — réécrit
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/ZoneResolverPatternsIntegrationTests.cs` — sources mises à jour

### Tests

- **Core** : 1093/1100 verts (post-P7d = 1098/1105). Delta : -5 tests (= tests "opener individuel" retirés au profit de tests "args espace" plus génériques). 6 préexistants rouges idem.
- **Adapter** : 393/393 inchangé.

### API publique

- **Nouveau type public** : `ArgListPatternBase`, `ArgSpan`, `ArgClassification`.
- **Type retiré** : `OpenerAlias`. Si un consumer externe l'avait utilisé : breaking. Aucun connu (= projet privé, aucune référence trouvée par grep).
- **`ForallBelongsTemplate`** : signature publique inchangée (hérite maintenant d'`ArgListPatternBase`, totalement transparent).

### Régression UX user-visible

Pour les utilisateurs habitués à `V x app a R` : **rupture**. Doivent
adopter la nouvelle convention `V x R`. Pour les nouveaux utilisateurs
(= PAP cible, lycéens) : la convention espace est plus naturelle et
plus rapide.

Doctrine "rapidité de saisie" respectée.

### Règles MC impactées

Aucune.

## Validation post-fix

```bash
# Tests Forall
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~ForallBelongs"
# → 32/32 verts

# Test pilote bout-en-bout
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~PILOT_V_x"
# → 1/1 vert

# Suite Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1093/1100 verts (6 préexistants rouges)

# Adapter
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts
```

## Fenêtre de réversibilité

Température **forte** — rupture user-visible. Conditions qui
justifieraient un revert via ADR superseding :

1. **Confusion ergo** confirmée en P8 (PAP-friendly) : si les lycéens
   tapent naturellement `V x app a R` et s'attendent à ce que ça marche,
   on réintégrera les openers comme alias secondaires (= option
   coexistence rejetée maintenant peut revenir).
2. **Ambiguïté pratique** non anticipée : si dans le corpus FR/EN, on
   trouve des cas où `V x N` (= 1 lettre canonique) doit être interprété
   comme `∀x,N` (= 2 vars) plutôt qu'`∀x ∈ ℕ`, on devra réviser la règle
   de discrimination.

## Plan ArgListPattern — futurs templates qui en hériteront

- **`LimTemplate`** (P9+) : `Lim x 0 f(x)` = head `Lim` + 3 args
  (var, limit_value, expression). Discrimination différente (= pas de
  domain mais 3 slots positionels). Probable extension de la base avec
  un hook `ClassifyArgs` override.
- **`SumTemplate`** (P9+) : `sum k 0 n k²` = head `sum` + 4 args.
- **`IntegralTemplate`** (P9+) : `int 0 1 f(x)` ou `int 0 1 f(x) dx`.
- **`DerivativeTemplate`** (P9+) : `d/dx f(x)` ou `derive f(x) x`.

La base `ArgListPatternBase` factorise déjà :
- Head detection (cohérente avec boundaries)
- ParseArgs (= whitespace + brackets atomiques + chains union/inter)
- ClassifyArgs (= dispatch via Registry)

Chaque sub-class fait son `BuildCompletion` propre (rendu LaTeX +
mutation source). Au fur et à mesure des templates ajoutés, des
helpers supplémentaires pourront monter dans la base si patterns
réutilisables.

## Plan Patterns — état d'avancement

- [x] **P7d** — Popup rendering définitif (commit `1e8f54a`)
- [x] **P5R** — ForallBelongs convention args espace (cet ADR) — Supersedes P5 partiellement
- [ ] **P8** — Validation manuelle PAP-friendly via `/build-iss` + Word
- [ ] **P9+** — `LimTemplate`, `SumTemplate`, etc. (hériteront d'ArgListPatternBase) + migration YAML
