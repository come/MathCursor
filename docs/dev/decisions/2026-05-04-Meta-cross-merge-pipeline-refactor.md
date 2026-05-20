# Meta — Refactor du pipeline cross-merge (4 phases)

**Date :** 2026-05-04
**Kind :** Meta
**Température :** molle
**Statut :** acté

## Contexte

La fonctionnalité multi-ligne (équivalences/égalités cross-paragraphe, brief
`2026-04-30-multiline-systems-equivalences.md`) a été implémentée par accrétion :
détection cross-merge, insertion, alignement OOXML, gestion du `¶` résiduel,
gestion du caret… toutes ces étapes ont fini empilées en ~50 lignes inline
dans `CommitLatexAndOMath` avec try/catch imbriqués, des positions calculées
manuellement (`newEnd+1`) souvent stale après les ops intermédiaires, et des
appels redondants à `SyncOMathJustificationToParagraph` à plusieurs niveaux.

Symptômes observés en série :
- caret resté dans l'OMath après cross-merge
- caret « devant » l'OMath (sélection déplacée par `paraRange.InsertXML`)
- `¶` vide en tête (résidu du paragraphe remplacé) que Word ajoute en tête du
  bloc display
- `\r` inséré dans le paragraphe OMath puis bouffé par `paraRange.InsertXML`
  qui normalise les terminateurs consécutifs
- alignement gauche stripé par l'insertion du `\r` (re-render OMath)
- dead code : 3 paths IDispatch dans `SyncOMathJustificationToParagraph`
  qui retournent systématiquement `DISP_E_UNKNOWNNAME` sur la combinaison
  PIA Office15 + Word user

## Décision

Décomposer le pipeline cross-merge en **4 phases nommées et séquentielles**,
miroir du modèle mental utilisateur :

1. **Conversion source → LaTeX** (déjà faite par la lattice engine en amont)
2. **Détection cross-merge** : `TryFindCrossMergeAbove` (rename de l'existant
   `TryMergeWithPreviousParagraphOMath`, signature clarifiée)
3. **Exécution insertion** : `InsertOMathAt` (existant, réutilisable)
4. **Finalisation layout** (uniquement pour cross-merge) :
   `FinalizeCrossMergeLayout`, qui orchestre 4 sous-étapes :
   - `StripLeadingResidualEmptyParagraph`
   - `EnforceOMathParagraphAlignment` *(réutilisable depuis single-eq)*
   - `AppendEmptyParagraphAfterOMath` *(réutilisable)*
   - `SetCaretOutOfMath` *(réutilisable)*

Chaque méthode privée est petite (~10-15 lignes), avec doc XML, contrat
explicite, et réutilisable depuis d'autres flows (mode édition, future
list-mode pour Enter-répète-marker).

### Cleanup associé

- Suppression `TryLookupOMathParagraphs` + `ApplyOMathParaJustificationViaReflection`
  (dead code : retournent toujours `null` / jamais appelés sur cette PIA).
- `SyncOMathJustificationToParagraph` simplifiée : typed `OMath.Justification`
  setter + XML patch direct (`PatchOMathParaJustificationViaXml`), pas de
  fallback IDispatch.
- Logging diagnostic réduit aux erreurs (les `LogDiag` exploratoires retirés).
- Test xUnit ajouté pour figer le contrat « `Hole` LHS d'un RelOp est strippé
  au rendu LaTeX » (`LatexRendererTests.RenderBin_RelationOpWithHoleLhs_StripsHole`).
- Doc XML sur `RenderBin` qui explicite ce contrat parser → renderer.

### Tradeoff

- On ferme la porte à un Word/PIA futur où `OMathParagraphs` serait exposé
  via IDispatch. Les branches XML resteront en place de toute façon (couvrent
  100 % des cas observés). Si on veut ré-ouvrir cette voie plus tard, l'ADR
  sera retracté avec un fix dédié.
- Le refactor touche ~150 lignes mais ne change AUCUN comportement testable
  par xUnit (le core 728/730 reste vert, les 2 échecs préexistants ne nous
  concernent pas).
- `Range.InsertParagraphAfter()` (API Word native) remplace
  `Range.Text = "\r"` — plus robuste pour le dernier `¶` du document
  (l'ancien manuel échouait quand l'OMath était la dernière paragraphe).

## Validé par l'utilisateur

> « oui j'aimerai faire tout ca et surtout que ca soit tres tres propre et
> sequencé, et que les methodes qui se recoupent avec d'autres trucs puissent
> etre reutilisées »

> « oui go »

## Liens

- Brief multi-ligne : [`2026-04-30-multiline-systems-equivalences.md`](../briefs/2026-04-30-multiline-systems-equivalences.md)
- Branche : `lattice-engine` (en cours)
