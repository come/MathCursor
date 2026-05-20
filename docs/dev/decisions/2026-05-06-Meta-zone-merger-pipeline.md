# Meta — Pipeline de mergers L4 via interface `IZoneMerger` (no if-pile)

**Date :** 2026-05-06
**Kind :** Meta
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-06-Feat-resolution-sidecar-and-layers.md](2026-05-06-Feat-resolution-sidecar-and-layers.md)
(applique enfin la doctrine "no if-pile, interfaces only" au L4 Orchestration)

## Décision

Les 5 mergers de zone actuellement appelés en cascade dans
`SuggestionService.OnPopupCommitRequested` (intra-OMaths même paragraphe,
cross-paragraphe au-dessus, reverted multi-line, marker chain cascade,
cases chain cascade) sont extraits dans des classes dédiées qui
implémentent une interface unique `IZoneMerger`. Un `MergerPipeline`
itère la liste injectée et renvoie le premier `MergeResult` non-null —
ça remplace les `if (merged == null)` empilés par une dispatch propre.

Le contrat de `IZoneMerger.TryMerge` impose que tout `MergeResult`
retourné contient un `MergedSidecar` calculé (pas Empty par défaut quand
des handles sont absorbés) — c'est la garantie qui empêche le bug 06-05
intra-merge de se reproduire au 6e merger qu'on ajouterait.

## Pourquoi

- **Bug 06-05 intra-merge** : la même classe de bug (sidecar perdu au
  merge) avait déjà été corrigée en cross-merge multi-ligne lors de la
  Phase 1.6 du sidecar. Elle est réapparue côté `TryMergeWithAdjacentOMaths`
  parce que le calcul du sidecar fusionné est dispersé dans 5 endroits
  différents (cross-marker chain, cases chain, intra-merge, etc.) et
  qu'un nouveau merger doit y penser à chaque fois. C'est un bug
  d'architecture, pas d'oubli ponctuel.

- **Doctrine ADR 06-05** : "L4 sans if-pile, interfaces only". On l'a
  posée pour le sidecar (L1) mais L4 reste un `SuggestionService`
  god-object 2400+ LoC avec 5 `Try...` méthodes appelées en cascade.
  Cet ADR applique la doctrine au L4.

- **Pattern existant validé** : `CasesCascadeMerger`, `RevertedZoneMerger`
  et l'extraction du jour `IntraMergeSidecarBuilder` montrent qu'on peut
  sortir la logique pure de merge dans des classes statiques testables
  en xUnit pur (sans Word). On généralise.

- **Évolutivité** : ajouter un 6e merger demain = nouvelle classe qui
  implémente `IZoneMerger` + ajout dans la liste injectée. Pas de
  modification de `SuggestionService`. Pas d'oubli sidecar possible
  (le contrat le force).

## Périmètre

**Inclus** :
1. Interface `IZoneMerger` côté adapter (`Host/Merging/IZoneMerger.cs`),
   contrat avec `MergeResult? TryMerge(MergeContext ctx)`. Signature de
   `MergeResult` étendue pour rendre `MergedSidecar` non-nullable
   (default `ResolutionSidecar.Empty` mais documenté comme "à calculer
   si des handles sont absorbés").
2. `MergeContext` : DTO neutre (absStart, absEnd, middleSource, accès
   abstrait au `Word.Document` et au store).
3. `MergerPipeline` : composé d'une `IReadOnlyList<IZoneMerger>` injectée,
   méthode `Run(MergeContext) → MergeResult?` qui itère et renvoie le
   premier match.
4. Extraction de 5 classes de merger :
   - `IntraOMathsMerger` (ex `TryMergeWithAdjacentOMaths`)
   - `CrossMarkerAboveMerger` (ex `TryFindCrossMergeAbove`)
   - `RevertedMultiLineMerger` (ex `TryAbsorbRevertedMultiLineZone`)
   - `MarkerChainCascadeMerger` (ex `TryCascadeAbsorbMarkerChain`)
   - `CasesChainCascadeMerger` (ex `TryCascadeAbsorbCasesChain`)
5. `SuggestionService.OnPopupCommitRequested` : remplace les 5
   appels en cascade par un seul `_mergerPipeline.Run(ctx)`.
6. Tests xUnit par merger (pour ceux dont la logique pure est extractable
   sans dépendance Word — typiquement le calcul des shifts sidecar).

**Exclus** (autres ADRs ou plus tard) :
- Décomposition complète du god-object `SuggestionService` (2400 LoC).
  Cet ADR ne traite QUE les mergers. Le reste (popup orchestration,
  bookmark mgmt, store sync, list-mode state) reste pour un ADR
  ultérieur de "nettoyage L4".
- Port au Core. Les mergers touchent `Word.Document`/`Word.OMath`,
  ils restent côté adapter (L4). L'interface `IZoneMerger` est
  côté adapter, pas dans le contract.

## Conséquences

- ~5 classes nouvelles dans `adapter-vsto/src/MathCursor/Host/Merging/`.
- `SuggestionService` perd ~600 LoC (les 5 `Try...` privés).
- Tests adapter +5 fichiers (un par merger), regroupés sous
  `tests/MathCursor.Tests/Host/Merging/`.
- L'ordre des mergers dans la liste = ordre de priorité (préservé tel
  qu'aujourd'hui pour ne pas changer le comportement).
- Aucun changement de comportement utilisateur attendu. Cet ADR est
  une refacto pure (test de non-régression : 786 GREEN core + 191 GREEN
  adapter avant + après).

## Alternatives considérées

- **Laisser tel quel + ajouter une checklist mentale** au moment d'écrire
  un nouveau merger. Rejeté : c'est exactement ce qui a échoué pour le
  bug 06-05.
- **Faire la décomposition complète du `SuggestionService` en une seule
  passe**. Rejeté : trop gros (2400 LoC), risque de régression élevé.
  On extrait les mergers d'abord, le reste suivra dans un ADR dédié.
- **Porter l'interface au Core** via abstraction document-scanner.
  Rejeté pour le moment : ajoute du code (interfaces + adapters) sans
  bénéfice immédiat — les mergers seront toujours appelés depuis le
  L4 VSTO. À reconsidérer si on attaque le port Office.js phase 2.

## Validé par l'utilisateur

Plan en 3 phrases proposé après le fix bug 06-05 intra-merge :

> "1. Extraire une interface `IZoneMerger { MergeResult? TryMerge(MergeContext ctx); }`
> côté adapter [...] 2. `MergerPipeline` injecté dans `SuggestionService`,
> ordre = priorité [...] 3. Les 5 `Try...` deviennent 5 classes [...]"

Validation utilisateur :

> "tu peux commit / puis on lance b pour consolider / et on fera le
> nettoyage de la couche L4 je crois"

(Le "nettoyage de la couche L4" = ADR ultérieur, hors scope ici.)

## Statut

acté
