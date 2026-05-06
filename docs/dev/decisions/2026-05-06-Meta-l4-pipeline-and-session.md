# Meta — Décomposition L4 : Pipeline déclaratif + Session avec cycle de vie

**Date :** 2026-05-06
**Kind :** Meta
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [ADR 06-05 sidecar-and-layers](2026-05-06-Feat-resolution-sidecar-and-layers.md),
[ADR 06-05 zone-merger-pipeline](2026-05-06-Meta-zone-merger-pipeline.md),
[Brief L4 décomposition](../briefs/2026-05-06-l4-suggestion-service-decomposition.md)

## Décision

Décomposer le god-object `SuggestionService` (3506 LoC) selon deux
modèles combinés :

- **A. Pipeline déclaratif du commit** — le commit user devient une
  composition explicite de 7 stages (`merger → resolver → renderer →
  inserter → store → layout → caret`), chacun étant une classe avec
  `Apply(CommitContext) → CommitContext`. Le flow se lit en 8 lignes.

- **B. Session avec cycle de vie explicite** — une `EquationSession`
  encapsule les ~10 champs mutables actuellement distribués, avec une
  FSM formelle (`Idle → Open → Resolving → Committing → Closed`).
  Transitions invalides → throw, plus d'invariants implicites épars.

Cible finale : `SuggestionService` ≈ 200 LoC d'orchestration pure.

## Pourquoi

- **Bug 06-05 vec-empilé récurrent** : reproduction du même pattern
  3 fois (cross-merge → intra-merge → empilement). Cause racine = les
  invariants (calcul sidecar fusionné, reset des sessions popup) sont
  distribués dans 3500 LoC sans propriétaire clair. Un nouveau site
  d'appel oublie systématiquement.

- **Flow d'exécution illisible** : `OnPopupCommitRequested` fait
  ~250 LoC qui orchestrent merge + resolve + insert + store + layout
  + caret en imbrication libre. Comprendre ce qui se passe demande
  de naviguer dans tout le fichier.

- **Test asymétrique** : les helpers déjà extraits (`CasesCascadeMerger`,
  `IntraMergeSidecarBuilder`, `OMathParaJcPatcher`,
  `ListModeStateMachine`) ont des tests xUnit purs. Le reste n'est
  testable qu'en Word, donc pas testé en pratique.

- **Doctrine 06-05 (sidecar)** déjà actée mais inappliquée à L4. Cet
  ADR finalise le projet doctrinal de séparation des responsabilités.

## Contracts (signatures)

### `CommitContext` (POCO immutable, propagé entre stages)

```csharp
public sealed record CommitContext
{
    // Coordonnées de la zone à insérer (mises à jour par mergerStage)
    public int AbsStart { get; init; }
    public int AbsEnd { get; init; }

    // Source brute + LaTeX rendu (mis à jour par resolver/renderer)
    public string Source { get; init; }
    public string Latex { get; init; }

    // Sidecar fusionné des handles absorbés + popup courante
    public ResolutionSidecar Sidecar { get; init; }

    // Handles supprimés au merge (à effacer du store après insert)
    public IReadOnlyList<string> RemovedHandles { get; init; }

    // Handle créé à l'insertion (renseigné par inserterStage)
    public EquationHandle? NewHandle { get; init; }

    // Marker dominant si cross-paragraphe merge (active list-mode)
    public string? CrossMergeMarker { get; init; }
    public bool WasCrossParagraphMerge { get; init; }

    // Mode édition (handle existant à mettre à jour vs. nouveau commit)
    public EquationHandle? EditingHandle { get; init; }
}
```

Chaque stage produit un nouveau `CommitContext` (record `with`), ne
mute pas le précédent. Tests xUnit triviaux : mock l'input, assertion
sur l'output.

### `EquationSession` (état mutable encapsulé, FSM)

```csharp
public sealed class EquationSession
{
    public SessionState State { get; private set; } = SessionState.Idle;

    // Coordonnées zone courante (remplace _lastZoneAbsStart/End/Source)
    public int ZoneAbsStart { get; private set; }
    public int ZoneAbsEnd { get; private set; }
    public string Source { get; private set; } = string.Empty;

    // État iterative expansion (remplace _iterativeSpanStart, etc.)
    public IterativeExpansion Expansion { get; private set; }

    // Reverted multi-line zone (remplace _revertedMultiLineZoneStart/End)
    public RevertedMultiLineZone? RevertedZone { get; private set; }

    // List-mode anchor (remplace _listModeAnchorPara, _lastListModeMarker)
    public ListModeAnchor? ListMode { get; private set; }

    // Edit mode (remplace _editHandle, _editingOMathStart)
    public EquationHandle? EditingHandle { get; private set; }

    // Transitions validées
    public void OpenOnZone(int absStart, int absEnd, string source);
    public void EnterEditing(EquationHandle handle, int omathStart);
    public void StartCommitting();
    public void Close(); // succès commit
    public void Reset(); // cancel ou Esc

    // Throw si transition invalide depuis State courant
}

public enum SessionState
{
    Idle,
    Open,         // popup ouverte sur zone, user résout
    Editing,      // mode édition d'un OMath existant
    Committing,   // commit en cours (mergers + insert + store)
    Closed,       // post-commit (transitoire avant retour à Idle)
}
```

## Plan en 5 phases (incrémental)

Ordre : risque croissant, bénéfice décroissant. Finir solide avant
d'attaquer le risqué.

| Phase | Scope | Risque | Coût estimé |
|-------|-------|--------|-------------|
| **1** | POCOs `CommitContext` + `EquationSession` + tests FSM. Pas de rename SuggestionService encore. | Faible | 1 sprint |
| **2** | `CommitPipeline` squelette (7 stages délégants) + extraction `MergerStage` + `ResolverStage` | Faible-moyen | 1-2 sprints |
| **3** | Extraction `InserterStage` (OOXML, transplant XML, fallback API) — la plus risquée | Élevé | 2-3 sprints |
| **4** | Extraction `StoreStage`, `LayoutStage`, `CaretStage` (helpers déjà partiellement extraits) | Moyen | 1-2 sprints |
| **5** | Extraction `ZoneDetector` + `PopupController` (hors pipeline, pre-commit) | Moyen | 1-2 sprints |

Total : **6 à 10 sprints** étalés. Chaque phase shippable
indépendamment.

## Conséquences

- **Code source** : disparition progressive du god-object. Chaque
  phase laisse `SuggestionService` plus petit + plus focalisé. À la
  fin : ~200 LoC d'orchestration.

- **Tests** : chaque stage est testable en xUnit pur (mock du
  `CommitContext`). La session a ses propres tests FSM. Les helpers
  Word-side (transplant XML, fallback) restent testables sur leurs
  parties pures.

- **Doctrine** : applique « no if-pile, interfaces only » de l'ADR
  06-05 sidecar à L4 entier (pas seulement les mergers).

- **Régression** : aucune attendue. Le comportement utilisateur reste
  identique. Garde-fou : 235+ tests adapter GREEN à chaque commit,
  et test manuel `AB+AC = AD` après chaque phase.

- **Évolutivité** : ajouter une étape au commit = ajouter un stage
  dans `CommitPipeline` (vs. trouver le bon endroit dans 3500 LoC).
  Ajouter un état à la session = ajouter un état à la FSM.

## Alternatives écartées

- **Modèle C — Event-driven decoupled** : chaque service écoute des
  events typés, plus de dépendance directe. Rejeté car *le flow
  d'exécution devient invisible* (« où ça se passe » = grep sur les
  handlers). Contraire à l'objectif simplification du flow.

- **Modèle D — Pure functions + side-effects isolés** : le commit =
  pure function `(DocState, UserAction) → DocMutation`. Trop ambitieux
  pour la phase 1 VSTO (Word.Document n'est pas mockable en pure).
  À reconsidérer pour la phase 2 Office.js.

- **Refacto big-bang** : trop risqué vu la base utilisateur (PAP +
  beta-testeurs profs). Chaque release doit rester stable.

- **Ne rien faire** : le bug 06-05 vient de juste se reproduire pour
  la 3e fois. Coût continu d'oublier les invariants distribués.

## Validé par l'utilisateur

Brief proposé puis 4 modèles présentés (A pipeline, B session, C
event-driven, D pure functions) avec tradeoffs :

> **« ok A+B »**

Validation du plan complet (ADR + démarrage Phase 1) :

> **« vas y go adr puis phase 1 »**

## Statut

acté
