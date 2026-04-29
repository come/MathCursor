# Meta — Refactor : ZoneResolver, point d'entrée unique pour la résolution de zone

**Date :** 2026-04-28
**Kind :** Meta
**Température :** forte
**Statut :** acté

## Décision

Extraire dans `core-csharp/src/MathCursor.Core/ZoneResolver.cs` une classe
`ZoneResolver` qui devient le **point d'entrée unique** pour transformer une
source brute (`V x R`) en représentation résolue (LaTeX rendu + ambig spot +
flag d'incomplétude). Elle porte l'état des préférences de désambiguïsation
de la session.

Le `ZoneResolver` expose une API minimale :

```csharp
public sealed class ZoneResolver
{
    public ZoneResolver(LatticeEngine engine);

    // Résout une source brute en appliquant les préférences accumulées.
    public ResolvedZone Resolve(string rawSource);

    // Mémorise un choix d'alt pour les futures résolutions de la session.
    public void AddPreference(string ruleId, int altIdx);

    // Reset (Esc, commit, sortie zone).
    public void Clear();
}

public sealed class ResolvedZone
{
    public string RawSource { get; }
    public string MutedSource { get; }   // post-application des prefs
    public string TopLatex { get; }
    public AmbiguitySpot? Spot { get; }
    public IReadOnlyList<AmbiguityMatch> AllMatches { get; }
    public int? SpotStart { get; }
    public int? SpotEnd { get; }
    // True si la zone est en attente d'un input (Hole non rempli OU
    // dernier char non-whitespace = opérateur binaire en attente d'opérande).
    public bool IsIncomplete { get; }
}
```

## Pourquoi

### Constat : 9 règles unitaires éparpillées

Avant ce refactor, la résolution de zone est étalée sur 9 mécanismes :

| Mécanisme | Fichier | Rôle |
|-----------|---------|------|
| `ExtendZoneBackwardWithKeyword` | SuggestionService | Hack NER : absorber keywords amont si manqué |
| `TryExtendForwardWhitespace` | SuggestionService | Étendre à droite si caret au-delà avec whitespace |
| `ShouldExtendZoneForward (a)` | SuggestionService | Étendre si dernier char = opérateur |
| `ShouldExtendZoneForward (b)` | SuggestionService | Étendre si AST contient un `\square` |
| `ApplySessionMutationPrefs` | SuggestionService | Appliquer les prefs source-mutation avant pipeline |
| `_resolvedSubstitutions` | Popup | Cache des sub LaTeX par defaultLatex (legacy AB→\vec) |
| `_rulePreferences` | Popup | Pref par ruleId pour auto-apply LaTeX-sub |
| `_sessionMutationPrefs` | SuggestionService | Pref par ruleId pour source-mutation |
| 5 sites d'appel à `ConvertWithAmbiguity` | SuggestionService | Mêmes flow dupliqué (parfois muté, parfois brut) |

Trois axes de désordre :
- **Deux dicts de prefs en parallèle** (popup vs service), à risque de désync.
- **Extension de zone = mosaïque** sans concept unifié "zone est-elle finie ?".
- **Pipeline de résolution dupliqué** sur 5 sites, chacun avec son wrapper.

### Le concept unifié manquant : "résoudre une zone"

Toutes ces règles tentent de répondre à des sous-questions du même problème :
**partir de la source brute tapée par l'utilisateur, en tenant compte des
choix qu'il a déjà faits, et produire ce que la popup doit afficher (+ savoir
si la zone est complète).**

Une seule classe avec un seul état (les préférences) et un seul point d'entrée
(`Resolve`) est plus simple à tester, à étendre et à raisonner.

### Bénéfice futur (ensembles canoniques, intervalles, U/inter)

Les briefs à venir (R/N/Z → `\mathbb{X}`, `[a,b[`, `U`/`inter`) vont introduire
de nouvelles règles source-mutation. Sans le refactor, chacune dupliquera le
pattern actuel : nouveau dict de prefs, nouveau site à instrumentaliser.
Avec le `ZoneResolver`, chaque nouvelle règle se branche dans le générateur
d'alternatives et profite automatiquement du résolveur.

## Conséquences

### Périmètre de cette PR (Phase 1)

- **Nouveau fichier** `core-csharp/src/MathCursor.Core/ZoneResolver.cs` (couche
  1, logique pure, testable). Contient la classe `ZoneResolver` et le record
  `ResolvedZone`.
- **SuggestionService** détient un `_resolver: ZoneResolver`. Tous les
  `_engine.ConvertWithAmbiguity(...)` passent par `_resolver.Resolve(...)`.
- **`ApplySessionMutationPrefs` supprimé** (intégré dans `Resolve`).
- **`_sessionMutationPrefs` supprimé** (état déplacé dans le `ZoneResolver`).
- **`ShouldExtendZoneForward`** simplifié à `_resolver.Resolve(zone.Text).IsIncomplete`.
- **Event popup `SourceMutationRequested`** : son handler appelle
  `_resolver.AddPreference(ruleId, altIdx)` puis `_resolver.Resolve(rawSource)`
  pour mettre à jour la popup.
- **Tests** : `ZoneResolverTests` (nouveaux) + run complet existant pour
  non-régression.

### HORS scope de cette PR (Phase 2 ultérieure)

- Migration des **LaTeX-subs popup** (`_resolvedSubstitutions`,
  `_rulePreferences` côté popup) vers le résolveur. Ces subs concernent les
  alts SANS mutation source (AB→\vec{AB}, x²→x_2). Elles fonctionnent
  différemment (post-pipeline, pas pré). À unifier dans une PR dédiée.
- Repenser `ExtendZoneBackwardWithKeyword` : ce hack NER restera tant que le
  modèle ne capte pas les keywords en début de zone. Brief NER v4 résout ça
  côté corpus.

### Risques

- Refactor structurel sans nouvelle feature → tests doivent rester verts.
  Stratégie : 1 commit par étape (extraction → migration des sites → cleanup
  ancien code), tests à chaque step.
- Le `ZoneResolver` vit dans le core (couche 1) qui ne connaît pas Word. OK
  car il ne touche que des strings, pas de COM.

## Validé par l'utilisateur

Constat partagé sur l'accumulation de règles unitaires :
> "ca sent les regles unitaires dans tous les sens plutot qu'une belle
> factorisation de code ?"

Validation pour partir sur le refactor :
> "oui on part sur ce refacto go"

## Statut

acté
