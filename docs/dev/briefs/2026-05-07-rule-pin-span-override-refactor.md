# 2026-05-07 — Brief : Refactor sidecar — RulePin + SpanOverride

> Statut : **design validé, implémentation à faire**.
> Suite directe du brief
> [`2026-05-07-global-context-multi-zoom-ranking.md`](2026-05-07-global-context-multi-zoom-ranking.md).

## Pourquoi

Le sidecar actuel utilise deux mécanismes parallèles :
- `SpanPin(rule, offset, len, altIdx)` — précis mais lourd : sensible aux
  changements d'offset au cross-merge, nécessite `SidecarMerger` pour
  recalibrer, matching `source.Substring(offset, len) == defaultLatex` à
  chaque résolution.
- `ZoneVotes[rule][altIdx] = count` — déjà rule-level mais représenté
  comme compteurs, sémantique du « gagnant » non explicite.

L'utilisateur a remonté (2026-05-07) :

> *« il faudrait plutôt piner la règle choisie que le choix final non ?
> ce sera plus facile de l'appliquer après d'ailleurs »*

> *« il faut avoir un identifier des règles et de leurs ambiguïtés pour
> pouvoir stocker span level, parce que si l'user veut changer en cours
> de route sur le même span et avoir par exemple AB + vec(AC)·(vec(DC))
> il va être coincé »*

→ Consolider en deux concepts explicites :
- **`RulePin`** par défaut (= choix global session) ;
- **`SpanOverride`** par exception (= override local pour un span précis),
  avec un identifier léger et stable (= la `MatchSignature`).

## Modèle cible

### `MatchSignature`

```csharp
public sealed class MatchSignature : IEquatable<MatchSignature>
{
    public string RuleId { get; }          // "two-uppercase"
    public string DefaultLatex { get; }    // "AB"
    public int RawSourcePos { get; }       // ancre dans le rawSource, stable au splice LaTeX
    public int OccurrenceIdx { get; }      // 0 = 1ʳᵉ occurrence de DefaultLatex dans la zone
}
```

**Justification des 4 champs** :
- `RuleId` + `DefaultLatex` : discrimine la nature (`two-uppercase:AB` ≠
  `two-uppercase:CD` ≠ `canonical-set:R`).
- `RawSourcePos` : ancre dans la source brute (positions LaTeX bougent au
  splice, le rawSource ne bouge pas).
- `OccurrenceIdx` : nécessaire pour `AB+CD=AB` (deux occurrences de "AB"
  distinctes à des positions différentes mais identifiables comme la 1ʳᵉ
  vs la 2ᵉ — robuste si l'user édite entre commits, là où `RawSourcePos`
  bougerait).

Note : `RawSourcePos` seul suffit dans la majorité des cas. `OccurrenceIdx`
est le filet de sécurité pour le rare cas où la position bouge mais
l'occurrence reste identifiable par son rang.

### `RulePin` et `SpanOverride`

```csharp
public sealed class RulePin
{
    public string RuleId { get; }
    public int AltIdx { get; }
}

public sealed class SpanOverride
{
    public MatchSignature Signature { get; }
    public int AltIdx { get; }   // -1 = revert au default (pas d'alt)
}
```

`SpanOverride.AltIdx = -1` représente explicitement le « revert au
defaultLatex », ce qui correspond exactement à la demande UX user
(pouvoir choisir « AD brut » dans la popup).

### `ResolutionSidecar` v2

```csharp
public sealed class ResolutionSidecar
{
    public IReadOnlyList<RulePin> RulePins { get; }
    public IReadOnlyList<SpanOverride> SpanOverrides { get; }

    // Legacy (gardé pour migration lazy depuis v1, retiré quand corpus migré)
    public IReadOnlyList<SpanPin> SpanPins { get; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> ZoneVotes { get; }
}
```

## Sémantique `ZoneResolver`

Pour chaque `AmbiguityMatch` (avec sa `Signature`) :

1. **`SpanOverride`** sur cette signature → applique l'alt. Si `AltIdx == -1`
   → revert au defaultLatex (pas de splice).
2. **Sinon `RulePin`** sur cette rule → applique l'alt.
3. **Sinon `ScoringHints` contextuels** (via `GlobalContext.Scorer`) → si
   `BestAltForRule(rule)` retourne un score > seuil → applique l'alt. C'est
   le path qui consomme `ParagraphResolutionsSignal` (L2) etc.
4. **Sinon** laisse le default (popup s'ouvre).

Le pin span-level reste prioritaire (= choix explicite local domine), mais
plus besoin du matching `source.Substring == defaultLatex` lourd : la
`Signature` capture cette information une fois pour toutes.

## Migration des sidecars persistés (option B — lazy convertor)

Validée par l'utilisateur : on ne peut pas casser les anciens documents
qui ont un sidecar v1 dans `CustomXMLPart` par OMath.

Au `load` du sidecar v1 (= `SidecarSerializer.Read`), on convertit :
- Pour chaque `SpanPin(rule, offset, len, altIdx)`, on génère un
  `SpanOverride(MatchSignature(rule, source.Substring(offset, len),
  RawSourcePos=offset, OccurrenceIdx=count_occurrences_before(offset)),
  altIdx)`.
- Les `ZoneVotes` sont **abandonnés** au load (mécanisme remplacé par
  `RulePin` + signaux contextuels — ils ne sont plus représentés
  explicitement, mais leur effet est régénéré par le scoring contextuel
  au prochain Resolve via le `ParagraphResolutionsSignal`).

Edge cases :
- Si `source.Substring(offset, len)` ne match pas le `defaultLatex` attendu
  (rawSource modifié depuis le commit), on conserve le SpanPin v1 dans la
  liste legacy et on log un warning. Pas de perte de donnée.
- Au prochain commit, le sidecar est ré-écrit en v2 (= legacy SpanPins
  vide, RulePins/SpanOverrides remplis).

## Suppression `SidecarMerger` / `IntraMergeSidecarBuilder`

Ces deux composants existaient pour recalibrer les `SpanPin.Offset` au
cross-merge multi-OMath. Avec :
- `RulePin` (rule-level, scope session, pas d'offset) → trivial à fusionner
  (set union).
- `SpanOverride` (signature stable au cross-merge, le `RawSourcePos` est
  dans le rawSource fusionné qu'on construit anyway) → fusion simple.

→ Suppression possible une fois la migration faite.

## Sérialisation `CustomXMLPart`

Format v2 :

```xml
<sidecar version="2">
  <rule-pins>
    <pin rule="two-uppercase" alt="0" />
  </rule-pins>
  <span-overrides>
    <override rule="two-uppercase" default="AB" pos="3" occ="0" alt="-1" />
  </span-overrides>
</sidecar>
```

Format v1 toléré au load (lazy convert), jamais écrit.

## Plan d'exécution (commits séquencés)

### Étape 1 — Types nouveaux + tests
- `Resolution/MatchSignature.cs` (POCO immutable + Equals/GetHashCode)
- `Resolution/RulePin.cs`
- `Resolution/SpanOverride.cs`
- Tests unitaires equality, sérialisation simple
- Pas branché. Tests existants intacts.

### Étape 2 — Calcul `Signature` au `AlternativeGenerator`
- `AmbiguityMatch` reçoit un nouveau champ `Signature`.
- L'`AlternativeGenerator` populate ce champ pour chaque match émis
  (compte les occurrences du `DefaultLatex` au fur et à mesure du scan).
- Tests sur les rules principales (two-uppercase, canonical-set).

### Étape 3 — `ResolutionSidecar v2` + sérialisation
- Ajout des props `RulePins` / `SpanOverrides` (legacy `SpanPins` /
  `ZoneVotes` gardés temporairement).
- `SidecarSerializer.Read` détecte v1/v2 et convertit lazy.
- `SidecarSerializer.Write` écrit toujours v2.
- Tests round-trip + tests de migration v1 → v2.

### Étape 4 — `ZoneResolver` consomme RulePins/SpanOverrides
- Nouvelle logique : SpanOverride → RulePin → ScoringHints → default.
- `SpanPins` legacy traités comme SpanOverrides via convertor au load
  (jamais émis directement).
- Tests existants `ZoneResolverWithSidecarTests` adaptés.

### Étape 5 — `SuggestionService` + popup produisent RulePins/SpanOverrides
- Popup : commit produit un `RulePin` (choix explicite session) +
  optionnellement un `SpanOverride` si l'user choisit "revert" pour un
  span précis.
- `ParagraphResolutionsSignal` : lit les `RulePins` du `_globalCtx` au
  lieu des `SpanPins`.

### Étape 6 — Suppression `SidecarMerger` + `IntraMergeSidecarBuilder`
- Remplace par fusion triviale (set union des RulePins, concat des
  SpanOverrides).
- Suppression effective des fichiers + tests devenus obsolètes.

### Étape 7 — UX popup : option "revert" explicite
- La popup affiche le `defaultLatex` comme première option (= revert /
  AltIdx=-1) en plus des Alternatives de la rule.
- Le commit popup envoie un `SpanOverride{altIdx: -1}` si l'user choisit
  cette option.

## Risques

1. **Migration silencieuse v1→v2 imparfaite** : si rawSource a été modifié
   depuis le commit, la conversion peut produire des SpanOverrides erronés.
   Mitigation : log + fallback legacy SpanPin + tests sur des fixtures.
2. **`OccurrenceIdx` mal calculé** : nécessite un scan pour compter les
   occurrences. Erreur si le scan ne respecte pas le même ordre que le
   parser. Tests fixtures critiques.
3. **Sérialisation cross-version** : un user qui downgrade lit du v2,
   ne sait pas le parser. À documenter — pas de support downgrade.
4. **Étapes 4-5 invasives** : surface de code touchée (ZoneResolver +
   SuggestionService + popup). Filet de sécurité = les 336+834 tests
   existants.

## Décisions

1. **`OccurrenceIdx` indispensable** (validé user 2026-05-07 « 1
   occurenceID »). Coût : un compteur dans le scan AlternativeGenerator.

## Décisions ouvertes

2. **`SpanOverride.AltIdx = -1` pour revert** ou un type `RevertOverride`
   séparé ? Reco : `-1` (sentinel) — plus simple, sérialisation directe.
3. **Garder `ZoneVotes` au load** pour ne pas perdre l'historique des
   sessions interrompues ? Reco : non, les sessions ne sont pas
   persistées de toute façon (vide au load), l'effet est régénéré par
   le scoring contextuel.
4. **Étape 7 (UX popup)** dans la même PR ou séparée ? Reco : séparée
   (la migration sidecar est déjà gros, l'UX popup peut suivre).

## Liens

- Brief parent :
  [`2026-05-07-global-context-multi-zoom-ranking.md`](2026-05-07-global-context-multi-zoom-ranking.md)
- Types existants :
  - [`core-csharp/src/MathCursor.Core/Resolution/SpanPin.cs`](../../../core-csharp/src/MathCursor.Core/Resolution/SpanPin.cs)
  - [`core-csharp/src/MathCursor.Core/Resolution/ResolutionSidecar.cs`](../../../core-csharp/src/MathCursor.Core/Resolution/ResolutionSidecar.cs)
  - [`core-csharp/src/MathCursor.Core/Resolution/SidecarSerializer.cs`](../../../core-csharp/src/MathCursor.Core/Resolution/SidecarSerializer.cs)
- Composants à supprimer :
  - [`core-csharp/src/MathCursor.Core/Resolution/SidecarMerger.cs`](../../../core-csharp/src/MathCursor.Core/Resolution/SidecarMerger.cs)
  - [`adapter-vsto/src/MathCursor/Host/IntraMergeSidecarBuilder.cs`](../../../adapter-vsto/src/MathCursor/Host/IntraMergeSidecarBuilder.cs)
