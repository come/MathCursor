# Feat — Édition multi-ligne via cascade cross-merge

**Date :** 2026-05-04
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Contexte

L'édition d'un OMath multi-ligne existant (clic + « Revenir à la saisie »)
revert vers N paragraphes Word de texte brut. Au commit (Enter), seule la
ligne courante se convertit ; les paragraphes au-dessus restent texte. Pour
re-merger, l'utilisateur doit re-déclencher la conversion sur chaque ligne,
ce qui est punitif.

Le brief associé (`docs/dev/briefs/2026-05-04-multiline-edit-cascade-merge.md`)
détaille l'analyse, les modes, l'algorithme et les edge cases.

## Décision

Étendre `TryFindCrossMergeAbove` (Phase 2 du pipeline cross-merge, cf. ADR
04-05 refactor) avec un mécanisme de **cascade montante en deux modes** :

### Mode 1 — Default (frappe neuve)

Cascade conservatrice qui absorbe les paragraphes au-dessus tant qu'ils
ont un marker align (`=`, `<=>`, `=>`, `<=`, et variantes). Le sommet
s'arrête sur :
- Un OMath qu'on possède (absorbé, comme cross-merge actuel).
- Un paragraphe sans marker (non absorbé).
- Une ligne vide (barrier).

### Mode 2 — Revert mode

Un nouveau champ d'état `_revertedMultiLineZone` mémorise la zone (range)
des paragraphes issus d'un revert d'OMath multi-ligne. Tant qu'il est
actif, le cascade absorbe **tous les paragraphes** de la zone, y compris
la première ligne sans marker.

Invalidation du Mode 2 :
- Commit succès → reset du champ.
- Caret quitte la zone (clic ailleurs, scroll loin) → reset.
- Edit-mode globalement annulé → reset.

## Impact

- **`SuggestionService.cs`** : nouveau champ `_revertedMultiLineZone`,
  set dans `OnRevertRequested` quand source contient `\n`, reset dans
  les triggers d'invalidation, lecture dans `TryFindCrossMergeAbove`.
- Logique de cascade en Mode 1 : itérer sur les paragraphes au-dessus
  (vs current single-step) avec heuristique marker.
- Pas de changement core/lattice : le pipeline reçoit déjà un source
  multi-ligne (`\n` séparateurs) et produit un MultiLineBlock.
- Pas de changement UI/popup.

## Tradeoff

- **Mode 1 cascade** peut être trop agressif sur du texte normal avec
  paragraphes consécutifs commençant par `=`. Mitigation : seuls les
  markers spécifiques déclenchent (markers ASCII multi-char rares hors
  contexte math, `=` solo en début de paragraphe également rare).
- **Mode 2 fragile sur tracking** : Word a plein d'events de selection,
  on doit hook proprement (réutilise le pattern existant d'invalidation
  edit-mode).

Si le Mode 1 produit trop de faux positifs en usage réel, on peut le
durcir : exiger 2+ markers consécutifs avant de cascade, ou un compteur
de tolérance. Trop tôt pour décider sans usage.

## Alternatives écartées

- **Approche B** (edit-zone multi-paragraphe explicite, tracking lourd
  sur tous les events) : plus prévisible mais 2-3 jours de plomberie,
  rendu visuel d'une zone surlignée à inventer.
- **Approche C** (popup multi-ligne avec textbox éditable) : self-contained
  mais découple visuellement de Word, nouvelle UI WPF à coder. Bonne
  alternative si A en réel pose problème.

## Validé par l'utilisateur

> « oui A »

> « ok on fais ca »

## Liens

- Brief : [`2026-05-04-multiline-edit-cascade-merge.md`](../briefs/2026-05-04-multiline-edit-cascade-merge.md)
- ADR refactor cross-merge : [`2026-05-04-Meta-cross-merge-pipeline-refactor.md`](2026-05-04-Meta-cross-merge-pipeline-refactor.md)
- Brief multi-ligne Phase 1 : [`2026-04-30-multiline-systems-equivalences.md`](../briefs/2026-04-30-multiline-systems-equivalences.md)
