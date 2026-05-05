# Feat — Mode liste invisible pour multi-ligne (préfixage automatique du marker)

**Date :** 2026-05-05
**Kind :** Feat
**Température :** molle
**Statut :** retracté
**Superseded by :** [`2026-05-05-Feat-multiline-list-mode-visible.md`](2026-05-05-Feat-multiline-list-mode-visible.md)

## Contexte

Continuer une chaîne d'équivalences/égalités/implications dans un bloc
multi-ligne demande aujourd'hui de retaper le marker (`<=>`, `=>`,
`<=`, `=`) sur chaque ligne. Friction notable pour les démonstrations
courantes en cours de maths lycée.

Cf. brief associé : [`docs/dev/briefs/2026-05-05-multiline-list-mode.md`](../briefs/2026-05-05-multiline-list-mode.md)

## Décision

Activer un **mode liste invisible** après un cross-merge multi-ligne
réussi : la machine d'état mémorise le marker utilisé. Quand
l'utilisateur tape une nouvelle ligne et fait Enter, on **préfixe
silencieusement** la source par le marker actif avant de la passer au
pipeline de cross-merge. L'utilisateur tape juste son équation, voit
sa chaîne se compléter automatiquement.

### États

- **Inactive** : default, comportement Enter normal
- **Active(marker)** : marker mémorisé, prêt à pré-préfixer

### Transitions clés

- Cross-merge réussi avec marker → `Active(marker)`
- Enter sur ligne vide ou whitespace-only → `Inactive` + passthrough
- Enter sur ligne avec contenu → préfixe `marker + " " + ligne`, commit,
  re-active
- Enter sur ligne qui commence DÉJÀ par un marker connu → valider tel
  quel (pas de double-préfixe)
- Caret quitte la zone (clic ailleurs, scroll loin) → `Inactive`

### Architecture

- Helper pur **`ListModeStateMachine`** dans `adapter-vsto/src/MathCursor/Host/`,
  testable sans dépendance Word/VSTO.
- API : `OnCrossMergeSucceeded(marker)`, `OnEnterPressed(currentLine) → EnterAction`,
  `OnSelectionMoved()`.
- `EnterAction` enum : `Passthrough | ExitListMode | PrefixWithActiveMarker | ValidateAsIs`.
- Hook dans `SuggestionService.CommitLatexAndOMath` (succès cross-merge
  → `OnCrossMergeSucceeded`).
- Hook dans `KeyboardInterceptor.OnEnter` (lit la ligne, applique
  l'`EnterAction`).
- Hook dans `OnSelectionChange` (caret hors ¶ post-multi-ligne →
  `OnSelectionMoved()`).

## Tradeoff

- **Pro** : ergonomie majeure pour démonstrations math courantes, fluide
  une fois habitué, zéro friction visuelle (pas de placeholder, pas de
  popup confirmation).
- **Con** : machine d'état tenue entre frappes — risque bugs subtils
  (undo, clic, scroll, switch fenêtre, redo). Mitigation : invalidation
  agressive sur tout signal "user a quitté le contexte", + tests
  unitaires couvrant les transitions.
- **Risque UX** : utilisateur surpris que sa frappe `4` (sans marker)
  devienne `<=> 4` dans le bloc. Mitigation : visuel reste cohérent
  (l'équation ajoutée au bloc est ce que l'utilisateur veut), et la
  désactivation sur ligne vide ou clic ailleurs est intuitive.

## Alternative écartée

**Auto-injection visible du marker** : insérer `<=> ` en texte sur la
ligne vide quand caret y arrive. Inconvénient : un user qui voulait
juste un retour à la ligne se retrouve avec un `<=>` parasite à
effacer. L'option « invisible state machine + prefix silencieux »
évite ce problème.

## Validé par l'utilisateur

> « yes nickel ! go ca »

(Validation après proposition détaillée du plan : machine d'état avec
trigger sur cross-merge, Enter qui pré-préfixe, double-Enter qui sort,
helper pur testable.)

## Liens

- Brief : [`2026-05-05-multiline-list-mode.md`](../briefs/2026-05-05-multiline-list-mode.md)
- ADR refactor cross-merge : [`2026-05-04-Meta-cross-merge-pipeline-refactor.md`](2026-05-04-Meta-cross-merge-pipeline-refactor.md)
- ADR cascade multi-ligne : [`2026-05-04-Feat-multiline-edit-cascade-merge.md`](2026-05-04-Feat-multiline-edit-cascade-merge.md)
