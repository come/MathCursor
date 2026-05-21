# Meta — Séparation des concepts « Pattern Template » et « Ambiguïté Closed » + désambig caret-aware

**Date :** 2026-05-21
**Kind :** Meta
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-13-Refactor-ambiguity-scanners-strategy.md`](2026-05-13-Refactor-ambiguity-scanners-strategy.md) — S0, contrat `IAmbiguityScanner` (qui reste pour les ambig closed)
- [`2026-05-13-Refactor-source-mutation-pins-sidecar.md`](2026-05-13-Refactor-source-mutation-pins-sidecar.md) — plan S0-S3 pour les pins via mutation source (orthogonal, peut paralléliser)
- [`2026-05-13-Refactor-s1-twoupper-source-mutations.md`](2026-05-13-Refactor-s1-twoupper-source-mutations.md) — S1, mécanisme `SourceMutation` réutilisé par les templates
- [`2026-05-13-Meta-extensibility-axes-abstractions.md`](2026-05-13-Meta-extensibility-axes-abstractions.md) — 5 axes A/B/C/D/E, ce cadrage rentre dans l'axe A (constructions notationnelles)

## Citation acté

> « A » — utilisateur, 2026-05-21
> (validation Option α : refonte en 2 contrats séparés `IAmbiguityScanner` (closed) + `IPatternTemplate` (compositionnel))
>
> « V x app a [0,1]U[3,4] sur ce cas, j'aimerai aussi decouper le sous bloc [0,1]U[3,4] qu'on doit desambiguiser aussi mais de maniere isolée parce que ca peut etre un truc à part tout seul, c'est pas "lié" au V x » — utilisateur, 2026-05-21
> (fonde la compositionnalité : un slot peut référencer un autre pattern qui se désambiguïse indépendamment)
>
> « et dans la regle tout le app à Interval => c'est facultatif » — utilisateur, 2026-05-21
> (fonde l'optionalité d'un slot complet, opener compris)
>
> « la desambig est sur le sous pattern le plus proche du curseur » — utilisateur, 2026-05-21
> (fonde la résolution caret-aware : le `Spot` actif est le match-le-plus-profond contenant le caret)
>
> « V (forall-belongs) + ensemble comme pilote » — utilisateur, 2026-05-21
> (validation du choix de pilote : couvre les 3 nouveaux concepts — pattern, slot optionnel, sub-pattern)
>
> « Remplacer complètement » — utilisateur, 2026-05-21
> (le scanner V legacy `VAsForallEAsExistsScanner` disparaît, pas de coexistence)
>
> « C# pur pour le pilote, YAML ensuite » — utilisateur, 2026-05-21
> (validation : les templates pilote sont écrits en C# impératif ; migration YAML après validation bout-en-bout)
>
> « Attendre que le WIP popup soit figé » — utilisateur, 2026-05-21
> (sequencing : finir `PopupAltFilter` + `BuildSidecar` + `RemovePreference` avant de toucher au `ZoneResolver` caret-aware)
>
> « c'est bon on y va » — utilisateur, 2026-05-21
> (validation finale du plan d'organisation en 9 étapes)

## Contexte

Le pipeline d'ambiguïté actuel (post-S0/S1) traite **toutes** les règles via `IAmbiguityScanner` + `AmbiguityAlternative { Latex, Mutation? }`. Ce modèle est **plat** : chaque alternative = une chaîne LaTeX figée + optionnellement une mutation source.

Le modèle marche bien pour les **ambiguïtés fermées** où la décision est un choix entre N rendus d'une **même source** :

- `AB` → vec / paren / bracket (`ScanUppercaseSequences`)
- `1/x+1` → `(1/x)+1` / `1/(x+1)` (`tight-chain-extension`)
- `2,3` → décimal / multiplication (`ScanDecimalVsMultiplication`)

Il marche **mal** pour les **patterns structurés** où :

1. L'expression a une **forme idiomatique** avec slots (`∀x ∈ E`, `lim_{x \to a} f(x)`, `∑_{k=p}^{n} terme`).
2. L'utilisateur n'a peut-être pas encore tapé toute la forme — la popup devrait **guider la suite** (carrés visuels pour les slots vides).
3. Les « alternatives » ne sont pas alternatives entre elles, ce sont des **extensions progressives** d'une même structure.
4. La structure peut **contenir des sous-structures** qui sont elles-mêmes des patterns autonomes (le `[0,1]∪[3,4]` à l'intérieur de `V x app a [0,1]U[3,4]` peut exister seul, avec ses propres ambigs).

Le scanner `VAsForallEAsExistsScanner` traite `V` atomiquement (V suivi d'espace) et propose `V identity / ∀ / √` à l'aveugle, sans regarder ce qui suit. C'est l'illustration de la dette : un pattern structuré (∀ + var + ∈ + ensemble) déguisé en ambig fermée (V → autre lettre). La popup ne **guide pas** la saisie ; elle propose des substitutions de lettre.

## Décision

**Séparer les deux concepts en deux contrats distincts**, qui coexistent dans le pipeline du `ZoneResolver` mais ne sont jamais mélangés au niveau code :

### 1. `IAmbiguityScanner` (existant, inchangé)

Pour les **ambig closed**. Décision binaire/ternaire sur une source figée. Modèle actuel `AmbiguityAlternative { Latex, Mutation? }` préservé. Les 10 scanners actuels qui satisfont ce critère restent en place. Concernés : `AB/ABC two-uppercase`, `tight-chain-extension`, `decimal-vs-multiplication`, `angle-two-letter-placeholder`, `vector-layout-flip-top-level`, `function-typical-comma-coords`, `decorated-two-three-upper`, `ast-based`.

### 2. `IPatternTemplate` (nouveau, à créer)

Pour les **patterns structurés compositionnels**. Un pattern = une grammaire idiomatique avec slots, opérant en plusieurs passes au fur et à mesure que l'utilisateur tape.

```csharp
public interface IPatternTemplate
{
    string TemplateId { get; }              // "forall-belongs", "ensemble", "interval-union"
    int Order { get; }

    PatternMatch? TryMatchHead(ScanContext ctx);
    IReadOnlyList<PatternCompletion> Expand(PatternMatch state, ScanContext ctx);
}

public sealed class PatternMatch
{
    public string TemplateId { get; }
    public int SourceStart { get; }
    public int SourceEnd { get; }           // peut grandir au fil des frappes
    public IReadOnlyDictionary<string, SlotValue> Slots { get; }
    public bool IsComplete { get; }
}

public abstract class SlotValue { }
public sealed class EmptySlot : SlotValue { }
public sealed class FilledSlotAtom : SlotValue { public string Text { get; } }
public sealed class FilledSlotSubPattern : SlotValue { public PatternMatch Sub { get; } }

public sealed class SlotSpec
{
    public string Name;
    public SlotType Type;                   // Identifier, IdentifierList, Expression, Pattern("ensemble"), ...
    public bool Required;
    public string? Opener;                  // "app a" / "(-" pour le slot domain de forall-belongs
}

public sealed class PatternCompletion
{
    public string Description { get; }      // "∀x ∈ ℝ"
    public string PreviewLatex { get; }     // \forall x \in \mathbb{R}
    public string HintLatex { get; }        // \forall x \in \square  (slots vides matérialisés)
    public SourceMutation? Mutation { get; }
    public int CompletenessScore { get; }
}
```

### 3. Compositionnalité (sous-patterns isolables)

Un `SlotSpec` peut référencer un autre pattern par nom (`Type = PatternRef("ensemble")`). Quand l'expansion atteint ce slot, le `PatternPipeline` délègue au pattern référencé qui produit ses propres `PatternCompletion`. Conséquence : `[0,1]∪[3,4]` peut être tapé seul OU comme sous-bloc de `V x app a [0,1]U[3,4]`, dans les deux cas le même `IntervalUnionTemplate` répond.

### 4. Slots optionnels avec opener

`Required = false` + `Opener = "app a"` : le slot existe si et seulement si l'utilisateur tape le token-opener. Sinon le pattern parent reste valide avec ce slot absent. Permet `∀x` (sans domaine) ou `∀x ∈ E` (avec domaine).

### 5. Désambig caret-aware

Le `ZoneResolver` reçoit un nouveau paramètre `caretOffset`. Si fourni, le `Spot` exposé à la popup est le **match-le-plus-profond contenant le caret** (recherche dans `AmbiguityMatch[]` + `PatternMatch[]` confondus). Si non fourni, comportement legacy = rightmost (compat préservée).

```csharp
public static AmbiguityMatch? FindDeepestMatchAtCaret(
    IReadOnlyList<AmbiguityMatch> allMatches, int caretOffset)
{
    // matches dont [Start..End) contient caretOffset, le plus petit span
}
```

### 6. Pilote

`forall-belongs` (V x [app a <ensemble>]) + `ensemble` (R, R*, N, Z, Q, C, intervals) + `interval-union` ([a,b]∪[c,d], récursif). Ce trio couvre les 3 nouveaux concepts (pattern compositionnel, slot optionnel, sub-pattern autonome). Si validé bout-en-bout, `Lim`, `Sum`, `Integral`, `Derivative` suivent le même moule.

### 7. Implémentation impérative en C# pour le pilote

Les 3 templates pilote sont écrits en C# pur dans `core-csharp/src/MathCursor.Core/Patterns/Templates/`. **Pas** de YAML au démarrage : on évite d'inventer le DSL en même temps que le concept. Migration des patterns triviaux vers YAML après validation bout-en-bout (P9+).

### 8. Retrait du scanner V legacy

Le `VAsForallEAsExistsScanner` et le `CanonicalSetLettersScanner` sont **retirés** au moment où `forall-belongs` + `ensemble` sont opérationnels (étape P6 du plan). Pas de coexistence — éviter 2 sources d'ambig sur le même token.

### 9. Localisation du code

```
core-csharp/src/MathCursor.Core/
└── Patterns/                                ← nouveau
    ├── IPatternTemplate.cs
    ├── PatternMatch.cs
    ├── PatternSlot.cs
    ├── PatternCompletion.cs
    ├── PatternPipeline.cs
    ├── PatternRegistry.cs                   ← ref parent↔enfant par nom
    ├── CaretLocator.cs                      ← FindDeepestMatchAtCaret
    └── Templates/
        ├── ForallBelongsTemplate.cs
        ├── EnsembleTemplate.cs
        └── IntervalUnionTemplate.cs
```

`Lattice/Ambiguity/` reste inchangé pour les ambig closed.

## Tradeoff & alternatives écartées

Évaluation sur qualité + perf + extensibilité uniquement (le temps d'implémentation n'est pas un critère, cf. principe utilisateur 2026-05-13).

- **Option β — Enrichir `AmbiguityAlternative`** avec `HintLatex`, `Description`, `SlotState`, scanners V/Lim/Sum plus riches. Rejeté : maintient un seul contrat qui fait deux choses (ambig closed + structure). La dette conceptuelle observée sur V→∀ se reproduirait dès qu'on ajouterait Lim/Sum. Maintenabilité dégradée long terme.

- **Option γ — Patch local du scanner V** (lookahead sur source dans `VAsForallEAsExistsScanner`). Rejeté : couvre seulement V, ne résout pas Lim/Sum qui restent muets côté popup. Reproduit exactement le piège du patch S0 actuel — dette accumulée scanner après scanner.

- **Popup hiérarchique (parent + sous-pattern affichés ensemble)**. Rejeté au profit du modèle « focused sur sous-pattern le plus proche du curseur » : l'utilisateur a explicitement choisi de naviguer par caret position pour activer la popup pertinente. Moins de bruit visuel, latence popup mieux contrôlée.

- **YAML dès P3 (ensemble en data déclarative)**. Rejeté : double l'inconnu (concept pattern + format YAML simultanés). On valide le concept avec C# pur d'abord, on migre les patterns triviaux ensuite avec connaissance de cause sur ce que le DSL doit exprimer.

- **Coexistence scanner V legacy + template forall-belongs**. Rejeté : deux sources d'ambig sur le même token V = source de bugs (priorité, double-émission, sidecar pin appliqué au mauvais canal).

## Conséquences

### Code à créer (suit le plan d'organisation Pn validé)

- **P1** — Caret-aware ZoneResolver : ajout paramètre `caretOffset` à `ZoneResolver.Resolve` + service `CaretLocator.FindDeepestMatchAtCaret`. Ajout pur, comportement legacy préservé quand `caretOffset = null`.
- **P2** — Squelette `Patterns/` : contrats vides + `PatternPipeline` qui tourne à vide (build vert).
- **P3** — `EnsembleTemplate` (heads R / R* / N / Z / Q / C / delegate à `IntervalUnionTemplate` pour `[`).
- **P4** — `IntervalUnionTemplate` (récursif, U / union / inter).
- **P5** — `ForallBelongsTemplate` (head V / E, slot var csv-of-identifiers, slot domain optionnel ref `EnsembleTemplate`).
- **P6** — Retrait `VAsForallEAsExistsScanner` + `CanonicalSetLettersScanner` du `AmbiguityScannerPipeline`.
- **P7** — `SuggestionPopupWindow` consomme `PatternCompletion[]` + `AmbiguityMatch[]` ; rendu HintLatex avec `\square` ou Unicode `▭`.
- **P8** — Test bout-en-bout `V x app a [0,1]U[3,4]` dans Word (PAP-friendly).
- **P9+** — `LimTemplate`, `SumTemplate`, `IntegralTemplate`, `DerivativeTemplate` + migration YAML des patterns triviaux (`EnsembleTemplate` candidat éligible).

### Tests

- Core baseline pré-décision : 939/946 verts (6 préexistants).
- **Nouveaux tests par étape** :
  - P1 : `CaretAwareResolverTests` — caret au milieu de `AB+AC=AD` → spot = AC (pas AD)
  - P3 : `EnsembleTemplateTests` — `R`, `R*`, `N`, `Z`, `Q`, `C`, intervals
  - P4 : `IntervalUnionTemplateTests` — `[0,1]U[3,4]`, `(0,1)∩[2,3]`, récursion 3 intervals
  - P5 : `ForallBelongsTemplateTests` — V seul, V x, V x,y, V x app a R, V x app a [0,1]U[3,4]
- **Fixtures partagées** : `specs/test-fixtures/patterns/*.json` (anticipe Office.js phase 2)
- Couverture cible : 935+/946 préservés + 30-50 nouveaux verts à la fin de P5

### API publique

- **`ZoneResolver.Resolve`** : signature étendue avec `int? caretOffset = null`. Rétro-compatible (default null = legacy rightmost).
- **`AmbiguityMatch[]`** : inchangé (les ambig closed continuent à passer par là).
- **`PatternMatch[]`** : nouveau, exposé via `ZoneResolver.ResolveResult` pour la popup.
- **`SuggestionPopupWindow.Show`** : accepte les deux flux. Adaptation à venir en P7.

### Règles MC impactées

- **MC0006** (splice LaTeX sur texte rendu) : la doctrine `IPatternTemplate` produit **toujours** une `SourceMutation` plutôt qu'un splice (la mutation source est le mode d'application natif). Conséquence : les futurs templates n'ajoutent **jamais** de hits MC0006. Cohérent avec le plan S2/S3 source-mut.
- **MC0001 / MC0009** : aucun impact direct.
- **Anticipé** : une future MC0011 « pattern template qui modifie l'AST plutôt que de muter la source » pourrait être ajoutée à la phase 4 du harnais.

### Couplage avec WIP en cours

- Le WIP `PopupAltFilter` + `BuildSidecar` + `RemovePreference` (en cours par un autre agent) continue indépendamment. **P1 démarre seulement après commit stable** de ce WIP, sur base saine.
- Le plan source-mut S2/S3 (élagage splice loop, offset tracking) peut **paralléliser** avec P1-P5 (zones de code différentes). S3 idéalement après P6 pour ne pas redéplacer du code.

### `WarningsNotAsErrors` Core.csproj

Inchangé par cette ADR. S3 retirera MC0006 ; cette ADR n'agit pas dessus directement.

## Validation post-fix

Pas de validation immédiate — cette ADR pose le cadre, le code arrive en P1+.

Critères de succès agrégés (à vérifier au fil de P1→P8) :

```bash
# Build vert à chaque étape
dotnet build MathCursor.sln

# Tests Core préservés + nouveaux verts
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 939+ verts à P1, 945+ à P3, 960+ à P5, 970+ à P8

# Tests adapter préservés
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 419/419 verts au moins jusqu'à P6
```

**Critère utilisateur (PAP)** : à P8, tape `V x app a [0,1]U[3,4]` dans Word, valide via Ctrl+Espace, OMath rendu = `\forall x \in [0,1]\cup[3,4]`. Popup affichée pendant la saisie avec carrés pour les slots non remplis.

## Fenêtre de réversibilité

Température **forte** — engagement structurel sur un nouveau contrat. Conditions qui justifieraient une retraction via ADR superseding :

1. **Le pilote P3-P5 révèle une impasse conceptuelle** : par exemple, la compositionnalité ne s'exprime pas proprement dans `PatternRegistry` (foyer de bugs, dépendances cycliques). Reconsidérer Option β.
2. **Régression perf** : ajout de la pipeline pattern + caret-aware coûte > +20 ms par résolution. Reconsidérer le découpage (peut-être pattern pipeline en pré-pass uniquement, pas par frappe).
3. **L'UX caret-aware est confuse** : l'utilisateur ne sait plus où il en est dans le pattern. Reconsidérer Option « popup hiérarchique » ou retour à rightmost.
4. **Lim/Sum demandent un modèle radicalement différent** de `forall-belongs` : si la généralisation à P9+ échoue, c'est qu'on a sur-fitté le pilote.

Aucune de ces conditions n'est attendue sur la base du diagnostic actuel — `Parser.cs` sait déjà produire l'AST imbriqué proprement, et `ApplyPreferences` éprouvé pour la source-mutation.

## Plan d'exécution — 9 étapes

```
0. [WIP en cours]    PopupAltFilter / BuildSidecar / RemovePreference (autre agent)
                     → attendre commit stable

1. P1                Caret-aware ZoneResolver + CaretLocator
2. P2                Squelette Patterns/ (contrats vides, build vert)
3. P3                EnsembleTemplate
4. P4                IntervalUnionTemplate
5. P5                ForallBelongsTemplate
6. P6                Retrait scanners V + canonical-set legacy
7. P7                Popup consomme PatternCompletion + carrés ▭
8. P8                Test bout-en-bout V x app a [0,1]U[3,4] dans Word
9. P9+               Lim, Sum, Integral, Derivative + migration YAML
```

Chaque étape = 1 commit + 1 ADR dédié (sauf P0 qui est un commit du WIP existant).

## Plan refacto / harnais — état d'avancement

**Refacto archi extensibilité (5 axes)** :
- [x] Étape 1 — Cartographie
- [x] Étape 2 — Abstractions
- [ ] Étape 3 — Implémentation par types existants (optionnel)
- [x] Étape 4 — Visitor sur AST
- [→] **En cours** — Source-mutation pour les pins (S2/S3 reste)
- [→] **En cours (cet ADR)** — Pattern templates (P1-P9+, axe A enrichi)
- [ ] Étape 5 — Sortir chaînes FR du Core + activation MC0002
- [ ] Étapes 6-8 — DomainRouter, ShortcutResolver, test intégration

**Harnais** :
- [x] Phase 0+1, 2, 2.5, 3
- [ ] Phase 4 — règles MC additionnelles (MC0002 + futur MC0011 pattern→source-mut)
- [ ] Phase 5 — Diff summarizer
- [ ] Phases 6-9
