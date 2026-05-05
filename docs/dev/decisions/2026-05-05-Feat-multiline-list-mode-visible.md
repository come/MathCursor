# Feat — Mode liste multi-ligne visible (auto-injection du marker en texte)

**Date :** 2026-05-05
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** [`2026-05-05-Feat-multiline-list-mode.md`](2026-05-05-Feat-multiline-list-mode.md)

## Contexte

Premier essai du list-mode (cf. ADR superseded) : le marker était mémorisé
en silence et préfixé "magiquement" au moment du Enter. Test utilisateur
immédiat : **« sinon le gars il va jamais comprendre que ecrire x=2 va
automatiquement rajouter le truc.. et on perd l'interet de l'automatisation »**.

Pédagogiquement faux : un user qui voit son `x=2` se transformer
spontanément en chaîne `<=> x=2` ne comprendra pas pourquoi. La feature
devient invisible et donc inutile (pas de transfert d'apprentissage).

## Décision

Pivoter vers **Option 1 (visible)** du brief :
[`docs/dev/briefs/2026-05-05-multiline-list-mode.md`](../briefs/2026-05-05-multiline-list-mode.md).

Juste après un cross-merge multi-ligne réussi, on **injecte le marker
(`<=> `, `=> `, `= `, etc.) comme TEXTE PLAIN** au début du ¶ d'ancrage,
caret positionné juste après l'espace. L'utilisateur voit le marker, comprend
qu'il peut continuer la chaîne en tapant juste son équation, et conserve
toute liberté de l'effacer si ce n'était pas son intention.

### États & transitions

- **Inactive** → cross-merge multi-ligne réussi → **Active(marker)** + injecte
  `marker + " "` dans ¶ d'ancrage.
- **Active** + Enter sur ligne contenant `marker + content` → conversion
  cross-merge normale → bloc étendu → re-injection du marker sur le ¶ suivant.
- **Active** + Enter sur ligne contenant **uniquement** le marker (pas de
  contenu réel après) → strip le marker (¶ devient vide), Enter consommé
  (= caret reste, list-mode désactivé). Comportement Word bullet list.
- **Active** + caret quitte le ¶ d'ancrage → désactivé. Le `<=> ` orphelin
  reste en texte (option **A**, choisie par l'utilisateur). Si gênant,
  l'user fait Backspace.

### Architecture

- `ListModeStateMachine` (helper pur, déjà testé) : élargi pour distinguer
  « ligne == marker actif seul » → `ExitListMode` (au lieu de `ValidateAsIs`).
- Hook `OnCrossMergeSucceeded` dans `CommitLatexAndOMathCore` : appelle la
  state machine + déclenche `InjectListModeMarker` qui insère le texte dans
  le ¶ d'ancrage.
- `TryHandleListModeEnter` simplifié :
  - `ExitListMode` → strip marker visible, consume Enter (Word ne crée pas
    de nouveau ¶, le caret reste sur la ligne désormais vide).
  - `ValidateAsIs` → trigger `TriggerManual` + `CommitSelected` (la ligne a
    déjà le marker, Mode 1 cross-merge l'absorbe).
  - `PrefixWithActiveMarker` (cas user qui a backspace le marker puis tapé
    du contenu) → on traite comme exit silencieux (passthrough Enter).
  - `Passthrough` → no-op.

## Tradeoff

- **Pro** : pédagogiquement clair, l'user comprend le mécanisme dès le 1er
  cross-merge ; pas de magie suspecte ; conformité aux conventions (Word
  bullet list, éditeurs markdown).
- **Pro** : code plus simple — pas de virtual-source-injection à mi-pipeline,
  on tape vraiment dans le doc et on laisse le cross-merge faire son boulot.
- **Con** : un user qui voulait juste un ¶ vide après son équation se
  retrouve avec un `<=> ` à effacer (1 Backspace ou Enter direct).
  Mitigation : Enter sur marker-only sort proprement.
- **Con** (option A retenue) : si l'user clique ailleurs, le `<=> `
  orphelin reste en texte. Acceptable parce que (a) c'est rare, (b) un
  Backspace résout, (c) un Ctrl+Espace dessus crée juste un OMath isolé
  inoffensif.

## Alternative écartée

**Option B** : auto-strip du marker orphelin quand caret quitte. Plus
agressive, demande tracking précis de notre injection (vs. user qui aurait
tapé `<=> ` lui-même), risque de supprimer du contenu user. L'option A
plus passive est plus sûre et moins surprenante.

## Validé par l'utilisateur

> « non A »
>
> (Réponse à la question « tu valides A ou B ? » sur le strip du marker
> orphelin au caret-leave.)

## Liens

- ADR superseded : [`2026-05-05-Feat-multiline-list-mode.md`](2026-05-05-Feat-multiline-list-mode.md)
- Brief : [`2026-05-05-multiline-list-mode.md`](../briefs/2026-05-05-multiline-list-mode.md)
  (l'Option 1 décrite dans le brief est désormais celle adoptée)
- ADR refactor cross-merge : [`2026-05-04-Meta-cross-merge-pipeline-refactor.md`](2026-05-04-Meta-cross-merge-pipeline-refactor.md)
- ADR cascade multi-ligne : [`2026-05-04-Feat-multiline-edit-cascade-merge.md`](2026-05-04-Feat-multiline-edit-cascade-merge.md)
