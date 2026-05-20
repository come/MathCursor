# 2026-05-07 — Brief : `GlobalContext` & ranking contextuel multi-zoom

> Statut : **design / réflexion**. Pas d'ADR encore. Conditionne :
> (a) la stratégie d'extension massive de features maths candidates
> (cf. brief 2026-05-07-features-coverage à venir),
> (b) une simplification structurelle du pipeline de résolution.

## Contexte

### Le bug déclencheur

Un système multi-ligne du genre :

```
{ AB + BC = AC      ← ligne 1, l'user désambig en `\vec{AB}` etc. via popup, commit
{ AD + DE = AE      ← ligne 2, NE bénéficie PAS du choix précédent
```

Aujourd'hui ligne 2 ré-ouvre la popup d'ambig pour `AD`, `DE`, `AE` — alors que dans le contexte (système 2 lignes adjacentes, même ¶, même intention sémantique), la résolution `vec` est évidente.

### Le diagnostic

Le `ResolutionSidecar` actuel a un périmètre **strict zone OMath** : il propage les pins/votes au sein d'un cross-merge donné, mais pas vers la ligne suivante du même bloc multi-ligne, encore moins vers le ¶ voisin.

Plusieurs autres bugs latents tombent dans la même catégorie : "le système devrait savoir mais ne sait pas, parce que l'info reste cloisonnée à un périmètre trop étroit."

### La piste : ne pas rapiécer, généraliser

Plutôt qu'élargir le périmètre du sidecar à coup de patches ciblés (cas multi-ligne, cas ¶ voisin, cas section…), **un mécanisme cœur unique** où *"ce que je viens de faire"* n'est qu'**un signal contextuel parmi d'autres**, à différents niveaux de zoom. Le ranking interroge ce mécanisme avant de produire la popup. Si le contexte est fort, auto-résolu sans popup. Si faible, popup.

→ La désambig popup et la propagation auto sont **deux faces de la même pièce** : la popup est le fallback du contexte. Aujourd'hui on a 100% popup ; cible : popup uniquement quand le contexte est insuffisant.

## Concept : `GlobalContext`

Un objet vivant pendant une session de saisie, qui :

- Accumule des **signaux contextuels** produits par plusieurs sources
- Agrège ces signaux (somme pondérée + décay) pour chaque alternative possible
- Expose des `ScoringHints` consultés par le pipeline de résolution

```
                  ┌──────────────────────────────────────────────┐
                  │                GlobalContext                  │
                  │                                                │
ZoneResolver   ───▶│  IContextSignal[] ── Aggregator ──▶ ScoringHints
  (consomme)      │     ▲                                          │
                  │     │ produit                                   │
                  │     │                                           │
                  │  Sources : sidecar local, ¶ courant,            │
                  │  ¶ voisins, headings, doc freq                  │
                  └──────────────────────────────────────────────┘
```

## Niveaux de zoom

Du plus chaud (proximité immédiate) au plus froid (doc entier) :

| # | Zone | Sources de signal | Décay | Poids relatif |
|---|---|---|---|---|
| **L0** | Token courant | NER, voisins immédiats | aucun | très élevé |
| **L1** | Bloc multi-ligne courant (cases, équivalences, chaîne en cours) | sidecar local du bloc actif (= `ResolutionSidecar` étendu) | aucun (vivant) | élevé |
| **L2** | ¶ courant | mots-clés du ¶ + résolutions précédentes du ¶ | aucun | moyen-élevé |
| **L3** | ¶ voisins (N=3 précédents) | mots-clés agrégés + résolutions agrégées | linéaire ÷ distance | moyen |
| **L4** | Section (entre `Heading*` Word) | mots-clés section + résolutions de la section | linéaire | faible-moyen |
| **L5** | Document entier | choix utilisateur cumulés depuis ouverture, freq globale | logarithmique | faible |

**Choix de design** : on ne mélange pas tout dans une soupe globale. Chaque niveau est une **dimension explicite** avec sa source, son cache, sa stratégie de décay.

## Interface

```csharp
namespace MathCursor.Core.Resolution
{
    /// <summary>Snapshot du contexte au moment d'une requête de résolution.</summary>
    public sealed class ContextSnapshot
    {
        public string RawSource { get; }      // la zone math en cours
        public string ParaText { get; }       // ¶ courant, déjà extrait
        public IReadOnlyList<string> NeighborParas { get; }  // L3
        public string SectionHeading { get; } // L4 ; null si hors section
        public string DocumentTitle { get; }  // L5 (heuristique)
        // ...
    }

    /// <summary>Une source qui contribue au scoring contextuel.</summary>
    public interface IContextSignal
    {
        /// <summary>Identifiant lisible (logging, debug, tests).</summary>
        string Name { get; }

        /// <summary>Niveau de zoom auquel ce signal opère (info pour Aggregator).</summary>
        ZoomLevel Level { get; }

        /// <summary>Retourne des deltas additifs sur les alternatives.
        /// Clé = identifiant `Rule + AltIdx` (ex: "two-uppercase:0" pour "vec").
        /// Valeur = delta de coût (négatif = muscle, positif = démuscle).</summary>
        IReadOnlyDictionary<string, double> Score(ContextSnapshot ctx);
    }

    public enum ZoomLevel { L0_Token, L1_Block, L2_Para, L3_NeighborParas, L4_Section, L5_Document }

    public sealed class ContextScorer
    {
        private readonly IReadOnlyList<IContextSignal> _signals;
        private readonly IReadOnlyDictionary<ZoomLevel, double> _levelWeights;

        public ScoringHints Aggregate(ContextSnapshot ctx);
    }

    /// <summary>Résultat consommé par ZoneResolver / AlternativeGenerator.</summary>
    public sealed class ScoringHints
    {
        public IReadOnlyDictionary<string, double> CostDeltas { get; }
        public IReadOnlyList<string> Trace { get; }  // pour debug : pourquoi ce score
    }
}
```

## Modèle de poids et décay

**Pondération par niveau** (valeurs initiales à calibrer empiriquement) :

| Niveau | Poids initial | Justification |
|---|---|---|
| L0 | 1.0 | référence (déjà ce qu'on fait) |
| L1 | 0.9 | bloc multi-ligne courant = très lié sémantiquement |
| L2 | 0.7 | ¶ courant = très probable même contexte |
| L3 | 0.4 (× ÷distance) | ¶ voisin, dégrade avec distance |
| L4 | 0.3 | section = contexte large mais cohérent |
| L5 | 0.15 | doc = très large, juste tendance |

**Décay temporel** (pour L4, L5) : poids halve toutes les *N* résolutions de distance, ou *T* paragraphes selon le niveau. Évite que les vieux choix polluent indéfiniment.

**Cap par signal** : aucun signal seul ne peut faire passer une alt < 50% à > 50% si elle n'avait pas déjà du soutien d'un autre niveau. Évite les biais aveuglants (ex: doc-titre "Probabilités" qui fait gagner P(A) sur 1+1=2).

**Seuil pour "auto-résolu sans popup"** : la 1ʳᵉ alt après scoring doit dominer la 2ᵉ avec écart > seuil S. Sinon popup avec les top-3.

## Cas d'étude

### Cas 1 : AB/AD système 2 lignes (le déclencheur)

**Avant** : ligne 1 résolue manuellement, ligne 2 ré-ambig → frustration.

**Après** :
- Ligne 1 commit → `SidecarSignal` enregistre pin + vote `two-uppercase:vec`. Le **bloc multi-ligne actif** (L1) le sait. `ParagraphResolutionSignal` (L2) le sait aussi.
- Ligne 2 query → L1 muscle vec (vote +0.9), L2 muscle vec (résolution voisine +0.7) → cumulé, vec gagne sur l'alternative `produit_AB` avec marge → **auto-résolu, pas de popup**.

### Cas 2 : Section "Probabilités" complète

**Avant** : à chaque `P(...)`, le user voit la popup même s'il a fait ce choix 5 fois dans la même section.

**Après** :
- 1ʳᵉ occurrence : popup, choix = "proba conditionnelle".
- 2ᵉ-3ᵉ occurrences : L2 (¶ courant + résolutions du ¶) + L3 (¶ voisins) musclent, déjà résolu auto.
- 4ᵉ+ : L5 (doc freq) + L4 (heading "Probabilités" si détecté) consolident.

### Cas 3 : Conjugué complexe vs moyenne stat (`x barre`)

Cas vraiment ambigu sémantiquement. La popup reste utile la 1ʳᵉ fois. Mais après :
- Si le doc est un cours de stats (heading "Statistiques", ¶ contiennent "moyenne", "écart-type") → muscle `\bar{x}` interprétation moyenne.
- Si le doc est un cours d'analyse complexe → muscle `\overline{z}` conjugué.

### Cas 4 : Dérivée `f'`

- 1ʳᵉ occurrence : popup `f^{\prime}` (dérivée) vs `f_1` (indice).
- Si ¶ contient "dériver", "tangente", "vitesse" : L2 muscle dérivée → auto-résolu.
- Si section = "Suites" : pourrait muscler `f_1` indice ; à arbitrer.

## Code simplifié / supprimé

Le mécanisme central absorbe plusieurs chemins ad-hoc actuels. Inventaire :

### À supprimer ou réduire fortement

| Fichier | LOC actuel | Devenir |
|---|---|---|
| `core-csharp/src/MathCursor.Core/Resolution/SidecarMerger.cs` | 69 | **supprimé** (la fusion offset-shift au cross-merge devient un cas particulier de propagation L1) |
| `core-csharp/src/MathCursor.Core/Resolution/SidecarSerializer.cs` | 350 | **réduit ~50%** : la sérialisation du sidecar par OMath reste pour persister L5 (choix du doc), mais la complexité multi-format peut diminuer si on standardise via `IContextSignal` |
| `adapter-vsto/src/MathCursor/Host/IntraMergeSidecarBuilder.cs` | 66 | **supprimé** (probablement, à confirmer après lecture détaillée — son rôle "propager le sidecar au cross-merge" devient implicite via L1) |
| `core-csharp/src/MathCursor.Core/ZoneResolver.cs` overload `Resolve(rawSource, sidecar)` (l. 136-250) | ~115 | **réduit à ~30** : remplace splice manuel right-to-left + matching pin par un seul appel `Aggregate(ctx)` qui retourne les hints, puis splice générique |

**Total dégagé brut estimé : 350-500 LOC.**

### À transformer (refactor, pas suppression)

| Fichier | LOC actuel | Transformation |
|---|---|---|
| `core-csharp/src/MathCursor.Core/Resolution/ResolutionSidecar.cs` | 72 | reste comme **structure de données** (pins + votes), mais devient consommé par un `SidecarSignal` (~50 LOC) plutôt que directement par le resolver |
| `core-csharp/src/MathCursor.Core/Lattice/AlternativeGenerator.cs` | 1072 | **génération** des alternatives reste. La **priorisation** (les blocs `GetRulePriority`, l. 248) sort vers `ContextScorer`. Estimé -100 à -200 LOC |
| `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` `_sessionSpanPins` + `CurrentSidecar` | dispersé | flux signal standardisé via `GlobalContext.Push(pin)` |

### Nouveau code à écrire

| Composant | LOC estimé |
|---|---|
| `GlobalContext` + `ContextSnapshot` + `IContextSignal` interface | 80 |
| `ContextScorer` (aggregator + cap + décay) | 120 |
| 4 implémentations initiales (`SidecarSignal`, `ParagraphResolutionsSignal`, `NeighborParasSignal`, `DocumentFreqSignal`) | 4 × ~80 = 320 |
| Tests unitaires (signaux + aggregator + cas bout-à-bout) | ~400 |
| Adaptation `ZoneResolver` pour consommer `ScoringHints` | 60 |

**Total ajouté : ~1000 LOC (dont 400 de tests)**

### Bilan LOC indicatif

Code production : **+580 ajoutés / −350 à −500 supprimés** = ~0 à +230 net.
Tests : **+400** mais remplace des tests existants éparpillés sur sidecar/cross-merge (à éliminer aussi en parallèle).

**Le gain n'est pas en LOC mais en lisibilité et extensibilité** : ajouter une feature contextuelle = un nouveau `IContextSignal` (50-100 LOC isolé), pas modifier 4 fichiers.

## Migration : big bang vs progressif

L'utilisateur a noté (citation 2026-05-07) :
> *« peu de gens dessus donc le big bang reste potentiellement ok.. peu de risque réel »*

Pondération :

### Option A — Big bang (1 PR, ~2-3 jours)

- Implémenter `GlobalContext` + 4 signaux + tests
- Réécrire `ZoneResolver.Resolve(sidecar)` overload
- Supprimer `SidecarMerger`, `IntraMergeSidecarBuilder`
- Tests existants passent (oracle de non-régression)

**Pour** : pas de période hybride confuse, design propre d'un coup, gain mental immédiat.
**Contre** : si un test casse, debug plus large. Mais avec 336 tests adapter + 790 core, surface couverte large.

### Option B — Progressif en 5 étapes (~1-2 semaines, étapes commitable séparément)

1. Ajouter `GlobalContext` + 1 signal `SidecarSignal` qui **wrap** le sidecar actuel à iso-comportement.
2. Tests passent inchangés.
3. Ajouter `ParagraphResolutionsSignal` (L2) → débloque cas AB/AD.
4. Ajouter `NeighborParasSignal` + `DocumentFreqSignal`.
5. Déprécier puis supprimer `SidecarMerger` / `IntraMergeSidecarBuilder` / overloads obsolètes.

**Pour** : commits granulaires, rollback facile, debug ciblé.
**Contre** : période hybride où deux mécanismes coexistent ; risque de fatiguer la motivation et laisser la migration à mi-chemin.

**Reco** : **Option A (big bang)** étant donné le filet de tests + base utilisateurs limitée + la valeur mentale d'avoir un seul modèle propre. Faire le big bang sur une branche dédiée, avec validation par run complet de la suite avant merge.

## Risques

1. **Performance** : cache + lazy obligatoires sur L3/L4 (lecture Word COM). Cible : invisible en frappe live (< 5 ms par token).
2. **Calibration des poids** : les valeurs (0.9 / 0.7 / 0.4...) sont a priori — à ajuster empiriquement avec un corpus de validation. Risque : sous-calibré → popup trop fréquente ; sur-calibré → choix imposé contre la volonté user.
3. **Feedback loops** : un mauvais choix au début d'un doc renforce le mauvais choix → spirale. Mitigation : *décay* + détection contradiction (user choisit l'opposé d'un signal fort → réduire son poids, marquer "incertain").
4. **Tests adversariaux** : doc qui parle de "dérivation linguistique" muscle à tort `f'`. Corpus à enrichir explicitement avec ces cas.
5. **Faux positifs L4 heading** : si l'user a un Heading "Maths" englobant tout le doc, L4 ne discrimine plus rien. Mitigation : pondération basse pour heading très large.
6. **Storage L5** : persistance par doc nécessite extension du sidecar actuel. À designer (schéma sérialisation, taille, GC).

## Spike d'évaluation (si on choisit Option B ou avant Option A)

Implémenter **uniquement L1 (étendre `SidecarSignal` au bloc cross-merge actif) + L2 (`ParagraphResolutionsSignal`)** sur 3 patterns :
- `two-uppercase` (vec / produit) — couvre le cas AB/AD
- `canonical-set` (R / `\mathbb{R}`)
- `letter-sup-number` (a^2 / a_2)

Mesurer empiriquement sur un corpus de saisie représentatif :
- Avant : N popups d'ambig sur 100 tokens types
- Après L1+L2 : N' popups → calcul du % popup-évitée

Cible : **>= 60%** de réduction des popups sur les patterns instrumentés. Si on n'arrive pas là, c'est que le scoring est mal calibré ou les niveaux mal choisis.

## Décisions ouvertes

1. **Storage L5** persistance : `CustomXMLPart` Word ? IsolatedStorage ? Lié au sidecar actuel par OMath handle ?
2. **Calibration initiale** : on part avec les poids ci-dessus et on ajuste, ou on construit un mécanisme d'apprentissage léger (genre A/B test interne sur le corpus de tests) ?
3. **Inversion / contradiction** : quand l'user choisit l'opposé d'un signal fort, on diminue le poids du signal pour cette session, ou on l'inverse explicitement ?
4. **Visibilité dev** : exposer la trace de scoring (`ScoringHints.Trace`) dans un panneau debug ribbon, ou seulement en logs `%TEMP%` ?
5. **Migration big bang vs progressif** : reco big bang. Confirme avant d'attaquer.
6. **Périmètre L1** : est-ce que "bloc multi-ligne" inclut les cases / équivalences / chaînes d'égalités, ou on traite ces 3 cas séparément ?
7. **Couplage NER** : le NER produit déjà des signaux contextuels au niveau token. On l'absorbe en L0 dans `IContextSignal` ou on le garde séparé ?

## Références

- Brief connexe : [`2026-05-06-typeahead-discoverability.md`](2026-05-06-typeahead-discoverability.md) (mécanique typeahead pourrait consommer le `GlobalContext` aussi)
- Sidecar actuel (point de départ) :
  - [`core-csharp/src/MathCursor.Core/Resolution/ResolutionSidecar.cs`](../../../core-csharp/src/MathCursor.Core/Resolution/ResolutionSidecar.cs)
  - [`core-csharp/src/MathCursor.Core/Resolution/SpanPin.cs`](../../../core-csharp/src/MathCursor.Core/Resolution/SpanPin.cs)
  - [`core-csharp/src/MathCursor.Core/ZoneResolver.cs`](../../../core-csharp/src/MathCursor.Core/ZoneResolver.cs) (overload `Resolve(rawSource, sidecar)`)
- Pipeline lattice (consommateur du scoring) :
  - [`core-csharp/src/MathCursor.Core/Lattice/AlternativeGenerator.cs`](../../../core-csharp/src/MathCursor.Core/Lattice/AlternativeGenerator.cs) (1072 l, RuleId + GetRulePriority)
  - [`core-csharp/src/MathCursor.Core/Lattice/AmbiguityDetector.cs`](../../../core-csharp/src/MathCursor.Core/Lattice/AmbiguityDetector.cs)
- Mémoire `project_positioning_speed.md` — ce système sert la rapidité (moins de popup = moins de friction).

## Suite prévue

Si validation : ADR `Feat — GlobalContext et ranking contextuel multi-zoom` (Température : forte — refactor structurel), puis implémentation Option A. Pas avant validation explicite.
