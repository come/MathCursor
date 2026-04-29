---
date: 2026-04-27
kind: Feat
température: molle
statut: acté
---

# Feat — Mode édition : popup « Revenir à la saisie initiale »

## Contexte

L'ancien mode édition (TryEnterEditMode) re-callait l'engine sur la source
stockée et affichait les variantes dans la popup de suggestion principale.
Avec la refonte 5b2 (popup à 2 sections + ambiguïté), ce flow est devenu
incohérent — citation utilisateur du 2026-04-27 : « l'edition c'est
n'importe quoi ».

Cf. brief
[`docs/dev/briefs/2026-04-27-edit-mode-revert-to-source.md`](../briefs/2026-04-27-edit-mode-revert-to-source.md).

## Décision

Quand le caret entre dans un OMath produit par MathCursor (= bookmark
`mcEq_<handleId>` présent + handle existant dans `IEquationStore`), on
affiche une popup d'édition dédiée avec une **action unique** :
**« Revenir à la saisie initiale »**.

Click sur le bouton :
1. Lit le source via `IEquationStore.RetrieveAsync(handle)`
2. Étend le range au bookmark complet (mcEq_… couvre l'OMath)
3. Supprime le bookmark + remplace `Range.Text = source`
4. `IEquationStore.RemoveAsync(handle)` (cleanup CustomXMLParts)
5. Caret en fin du texte inséré
6. Ferme la popup

Action « Annuler » / Esc / clic ailleurs → ferme la popup, document inchangé.
L'utilisateur peut continuer à éditer l'OMath caractère par caractère via
les contrôles natifs Word — la popup est une option, pas une obligation.

## Implémentation

- `adapter-vsto/src/MathCursor/UI/EditModePopupWindow.cs` (nouveau) : popup
  WPF simple, 2 boutons, fond blanc, fade in/out 150 ms. Pattern Win32
  WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW pour ne pas voler le focus à Word.
- `SuggestionService.TryEnterEditMode` : ne re-call plus l'engine. Cache
  la popup de suggestion principale, ouvre la popup d'édition au caret,
  branche l'event `RevertRequested → OnRevertRequested`.
- `SuggestionService.OnRevertRequested` : implémente le revert (étapes 1-6
  ci-dessus) avec `try/catch` sur chaque opération Word + log diag.
- `SuggestionService.HidePopup` ferme aussi la popup d'édition.
- Anti-spam : champ `_editingOMathStart` (déjà présent) évite de
  re-déclencher la popup à chaque tick 200 ms tant que le caret reste
  dans le même OMath. Reset quand le caret en sort.

## Comportement préservé

- L'API publique `IEquationStore` (Retrieve/Remove/Store) inchangée.
- Le pipeline d'insertion des nouvelles formules (Ctrl+Espace ou conversion
  auto via NER) inchangé : crée toujours un bookmark `mcEq_<handleId>` +
  StoreAsync.
- L'OMath natif Word peut toujours être édité directement par l'utilisateur
  via les contrôles math Word (la popup ne bloque rien).
- Ctrl+Z après revert restaure l'OMath via undo Word natif (le `Range.Text`
  change est trackable).

## Cas non couverts (acceptés)

- L'OMath créé par un autre outil que MathCursor (pas de bookmark
  `mcEq_…`) : aucune popup affichée, comportement Word natif.
- L'OMath wrappé d'un bookmark mais source absent du store (état
  inconsistent) : popup ne s'affiche pas, log warn.
- Chaîne d'undo après plusieurs revert + reconvert : géré nativement
  par Word, pas de logique custom.

## Citation utilisateur

Thread du 2026-04-27 :

> « l'edition c'est n'importe quoi, si on met le cursor sur un omath,
>   docs/dev/briefs/2026-04-27-edit-mode-revert-to-source.md lis ca »

Brief lu et implémenté tel que spécifié (popup auto à l'entrée d'OMath,
action unique « Revenir à la saisie initiale », pas de double confirmation).
L'arbitrage popup auto vs raccourci dédié est laissé à un ajustement futur
selon retour d'usage.
