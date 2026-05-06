# Brief — Décomposition L4 : Pipeline déclaratif + Session avec cycle de vie

**Date :** 2026-05-06
**Statut :** rédigé, en attente d'ADR
**Lié à :** [ADR 06-05 sidecar-and-layers](../decisions/2026-05-06-Feat-resolution-sidecar-and-layers.md),
[ADR 06-05 zone-merger-pipeline](../decisions/2026-05-06-Meta-zone-merger-pipeline.md)

## Objectif

**Simplifier la lecture du flow d'exécution**, pas juste réduire le
nombre de LoC. Aujourd'hui le flow `user tape → commit → OMath inséré`
est éclaté dans 250 lignes de `OnPopupCommitRequested` + 5 méthodes
privées de plusieurs centaines de lignes chacune, avec 10 champs
privés mutables qu'on doit reset à des moments précis (sinon bug 06-05).

Cible : **lire le commit en 8 lignes**, chaque étape avec UN propriétaire
clair, plus aucun état mutable distribué.

## Cadre architectural cible

Deux modèles combinés (validés par l'utilisateur : *« ok A+B »*) :

### A. Pipeline déclaratif du commit

Le commit user déclenche une suite d'étapes explicites composées via un
`CommitPipeline`, chaque étape étant une classe avec une méthode pure
`Apply(CommitContext) → CommitContext`. Le pipeline se lit comme une
recette :

```csharp
// CommitPipeline.Run()
var ctx = new CommitContext(_session);
ctx = _merger.Apply(ctx);      // intra/cross/cases/marker chain
ctx = _resolver.Apply(ctx);    // ZoneResolver + sidecar fusionné
ctx = _renderer.Apply(ctx);    // top LaTeX → OMML
ctx = _inserter.Apply(ctx);    // Build XML / fallback API
ctx = _store.Apply(ctx);       // CustomXMLPart + sidecar JSON
ctx = _layout.Apply(ctx);      // alignement + ¶ vide + list-mode
_caret.PlaceAfter(ctx);        // caret position post-insert
```

`CommitContext` est un POCO immutable (chaque étape produit un nouveau
ctx, ne mute pas le précédent) qui transporte tout ce dont les étapes
suivantes ont besoin :

```csharp
sealed record CommitContext(
    int AbsStart, int AbsEnd,
    string Source, string Latex,
    ResolutionSidecar Sidecar,
    IReadOnlyList<string> RemovedHandles,
    EquationHandle? NewHandle,
    string? CrossMergeMarker,    // null si intra ou pas de merge
    bool WasCrossParagraphMerge);
```

**Bénéfice** : flow d'exécution lisible en 8 lignes. Ajouter une étape
= ajouter une ligne au pipeline (vs. trouver le bon endroit dans 3500 LoC
aujourd'hui). Tester une étape = mock du `CommitContext` d'entrée et
assertion sur le `CommitContext` de sortie. Plus aucune méthode de
250 LoC qui « fait tout ».

### B. Session avec cycle de vie explicite

Une `EquationSession` modélise « l'utilisateur a une popup ouverte sur
une zone math en cours de résolution ». Cycle de vie formel :

```
[Idle]
  → ZoneDetected → [Open]
  → AltSelected (×N) → [Open]
  → CommitTriggered → [Committing]
  → InsertedSuccess → [Closed] → [Idle]
                ↘
                  EditModeEntered → [Editing]
                  → RevertRequested → [Idle]
                  → CommitTriggered → [Committing] → [Closed]
```

La session **encapsule l'état mutable** qui aujourd'hui vit dans 10
champs `_lastZoneAbsStart`, `_iterativeSpanStart`,
`_revertedMultiLineZoneStart`, `_listModeAnchorPara`, etc.) :

```csharp
sealed class EquationSession
{
    public SessionState State { get; private set; }    // FSM
    public int ZoneAbsStart { get; private set; }
    public int ZoneAbsEnd { get; private set; }
    public string Source { get; private set; }
    public ResolutionSidecar PendingSidecar { get; private set; }
    public ListModeAnchor? ListMode { get; private set; }
    public RevertedMultiLineZone? RevertedZone { get; private set; }
    public EquationHandle? EditingHandle { get; private set; }
    public IterativeExpansion Expansion { get; private set; }

    public void TransitionTo(SessionState newState) { /* validates */ }
    public void Reset() { /* clears everything for next session */ }
}
```

Transitions invalides → throw (vs. aujourd'hui : on appelle des resets
épars et on espère qu'on n'a rien oublié — cf. cause racine bug 06-05).

**Bénéfice** : un seul propriétaire par invariant. `HidePopup` n'a plus
à reset 5 trucs : `_session.Reset()` reset tout en un appel, et la
session sait ce qu'elle doit nettoyer.

## Architecture finale

```
SuggestionService (orchestrateur ~200 LoC)
├── _session : EquationSession                  (état)
├── _detector : IZoneDetector                   (NER + tracking + iterative)
├── _popup : IPopupController                   (Show/Hide/events)
└── _pipeline : CommitPipeline                  (les 7 étapes)
       ├── _merger : IMergerStage               (existant via IZoneMerger)
       ├── _resolver : IResolverStage           (wrap ZoneResolver)
       ├── _renderer : IRendererStage           (LaTeX → OMML)
       ├── _inserter : IInserterStage           (Build XML / fallback)
       ├── _store : IStoreStage                 (CustomXMLPart)
       ├── _layout : ILayoutStage               (alignement, ¶, list-mode)
       └── _caret : ICaretStage                 (position post-insert)
```

`SuggestionService` ne contient plus que :
1. La composition (constructeur)
2. Le wiring des events (zone détectée → popup show ; popup commit →
   pipeline run ; revert → session transition)
3. Les méthodes d'entrée publique (`Install`, `Dispose`, `HidePopup`)

Estimation finale : **~200 LoC** (vs 3506 aujourd'hui).

## Phases (incrémentales, une PR/ADR par phase)

L'ordre est dicté par **risque croissant + bénéfice décroissant** :
finir solide d'abord, attaquer le risqué après.

### Phase 1 — Définir `CommitContext` + `EquationSession`

POCOs immutables (`CommitContext`) et mutables-mais-encapsulées
(`EquationSession`). Pas encore d'extraction de logique — juste les
contracts. `SuggestionService` reste tel quel mais utilise désormais
`_session.ZoneAbsStart` au lieu de `_lastZoneAbsStart` (rename
mécanique + propriété qui delegue).

**Bénéfice** : socle sur lequel toutes les phases s'appuient.
**Risque** : faible, c'est du rename. **Tests** : la session a ses
propres tests xUnit sur les transitions FSM (state invalide → throw).

### Phase 2 — `CommitPipeline` squelette + extraction `_resolver` et `_merger`

Crée `CommitPipeline` avec ses 7 stages comme **délégants** vers les
méthodes existantes (même approche que `IZoneMerger` posé hier).
Extrait `ResolverStage` (wrap `ZoneResolver.Resolve(source, sidecar)`)
et finalise `MergerStage` (qui consomme `IZoneMerger` déjà posé).

**Bénéfice** : le flow du commit devient lisible (8 lignes), même si
3 stages sur 7 sont encore des wrappers vers SuggestionService.
**Risque** : faible. **Tests** : pipeline orchestration (déjà fait
côté merger pipeline, on étend).

### Phase 3 — Extraction `_inserter` (OOXML)

Stage le plus complexe (~750 LoC). On extrait `BuildOMathXmlIsolated`,
le fallback API `OMaths.Add + BuildUp`, et le patch alignement
(`SyncOMathJustificationToParagraph`) dans `InserterStage`. Le service
publie `Insert(int absStart, int absEnd, string latex) → InsertResult`.

**Risque** : élevé (manipulation OOXML, transplant XML, fallbacks).
**Stratégie** : extraire en 2 sous-étapes si nécessaire (XML build
d'abord, fallback ensuite). Ajouter tests xUnit sur les transformations
XML pures avant l'extraction.

### Phase 4 — Extraction `_store`, `_layout`, `_caret`

Stages plus petits, déjà partiellement extraits côté helpers
(`OMathParaJcPatcher`, `CaretPositionCalculator`,
`ListModeStateMachine`). On les compose en stages explicites dans le
pipeline.

**Bénéfice** : le pipeline devient 100% des stages réels (plus de
wrappers). **Risque** : faible (la logique pure est déjà extraite).

### Phase 5 — Extraction `_detector` et `_popup`

Hors pipeline (ces deux services s'exécutent **avant** que le commit
soit déclenché). Extrait :
- `ZoneDetector` : NER tick + zone tracking + iterative expansion
- `PopupController` : compose `SuggestionPopupWindow` + `EditModePopupWindow`,
  expose les events au service hôte

**Bénéfice** : `SuggestionService` perd les ~600 LoC de detection +
~150 LoC de popup orchestration. C'est la dernière étape vers le
~200 LoC final.

## Risques (transversaux)

1. **Régression invisible** : le god-object marche aujourd'hui, on doit
   préserver le comportement à l'octet près. Stratégie : 191+ tests
   adapter doivent rester GREEN à chaque commit, et test manuel du
   scenario `AB+AC = AD` après chaque phase.

2. **Sur-conception du `CommitContext`** : tentation d'y mettre TOUT
   « au cas où ». Discipline : on n'ajoute un champ que quand une
   étape en a besoin. Quitte à élargir au fil des phases.

3. **FSM trop rigide** : si la session refuse une transition légitime
   (ex. revert pendant editing), bug user. Stratégie : modéliser le
   diagramme d'états avant Phase 1, tester chaque transition documentée.

## Coût estimé

| Phase | Description | Coût |
|-------|-------------|------|
| 1 | Session + Context POCOs + rename | 1 sprint |
| 2 | Pipeline squelette + 2 stages | 1-2 sprints |
| 3 | InserterStage (OOXML) | 2-3 sprints |
| 4 | Store + Layout + Caret stages | 1-2 sprints |
| 5 | Detector + PopupController | 1-2 sprints |

Total : **6 à 10 sprints**, étalés. Chaque phase shippable
indépendamment, pas de big-bang.

## Validation utilisateur du cadre architectural

Après proposition de 4 modèles (A pipeline, B session, C event-driven,
D pure functions) avec tradeoffs :

> **« ok A+B »**

→ Modèles A (pipeline) + B (session) actés comme cadre. Modèles C et
D explicitement écartés (C = flow invisible, D = trop ambitieux pour
phase 1 VSTO).

## Prochaine étape

ADR `2026-05-XX-Meta-l4-pipeline-and-session` qui acte :
1. Le cadre A+B
2. Les contracts `CommitContext` et `EquationSession` (signatures)
3. Le plan en 5 phases (ordre validé)
4. Les invariants à préserver (régression `AB+AC = AD` etc.)

Puis démarrage **Phase 1**.
