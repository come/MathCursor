# UX — Raccourcis clavier du ruban : Alt, M (onglet) puis T (tableau)

**Date :** 2026-10-26
**Kind :** UX
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-10-16-Feat-sign-variation-table-insert](2026-10-16-Feat-sign-variation-table-insert.md) (bouton « Tableau de signe / variation »)

## Citation acté

> « ok pour Alt M + T » — utilisateur, 2026-10-26, en réponse à la proposition
> d'ajouter des KeyTips au ruban MathCursor plutôt que de faire cliquer le ruban
> par un script d'automatisation.

## Contexte

Le bouton « Tableau de signe / variation » n'est accessible qu'à la souris : Word
attribue bien des KeyTips automatiques (touche Alt) aux contrôles personnalisés, mais
ils ne sont ni documentés ni stables d'une version du ruban à l'autre. Deux besoins :

- **élèves au clavier** (dont PAP) : insérer un tableau sans quitter le clavier ;
- **scripts d'automatisation** (qui tapent au clavier) : un
  script ne sait pas cliquer dans le ruban ; une séquence de touches fixe suffit.

## Décision

KeyTips explicites dans `Ribbon.xml` :

- onglet MathCursor : **`M`** (Alt, M) ;
- bouton « Tableau de signe / variation » : **`T`** (Alt, M, T).

Les autres contrôles gardent les KeyTips automatiques de Word.

## Tradeoff & alternatives écartées

- **Script qui clique le ruban par UI Automation** : dépendance Python en plus,
  fragile (onglet non sélectionné = bouton absent de l'arbre), et n'apporte rien aux
  élèves.
- **Raccourci global (Ctrl+Maj+…)** : hook clavier en plus, risque de conflit avec
  les raccourcis Word ; la mécanique KeyTips est native et découvrable (Alt).
- **Insérer le tableau à la main avant le script** : on ne voit pas l'insertion à
  l'écran (repli temporaire seulement).

Risque connu : si une autre extension ou un onglet Word utilise aussi `M`, Word
désambiguïse (KeyTips à deux caractères). À vérifier à l'usage (Alt dans Word).

## Conséquences

- **Code touché** : `adapter-vsto/src/MathCursor/Ribbon.xml` (2 attributs `keytip`).
- **Tests** : aucun test automatique (XML de ruban) ; validation visuelle.
- **Moteur / sérialisation** : non touchés.

## Validation post-fix

Dans Word, après rebuild : Alt affiche `M` sur l'onglet MathCursor ; Alt, M ouvre
l'onglet et affiche `T` sur « Tableau de signe / variation » ; Alt, M, T insère le
tableau.
