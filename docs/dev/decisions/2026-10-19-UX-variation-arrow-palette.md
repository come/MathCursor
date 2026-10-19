# UX — Palette de flèches dans les cases d'un tableau de variation

**Date :** 2026-10-19
**Kind :** UX
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-10-16-Feat-sign-variation-table-insert](2026-10-16-Feat-sign-variation-table-insert.md) (gabarit, flèches caractère), [2026-09-04-Fix-popup-owner-multiwindow](2026-09-04-Fix-popup-owner-multiwindow.md) (owner de popup)

## Citation acté

> « je prefere nettement l'alternative ! bcp plus propre » puis « tu peux faire
> une popup derivée de la notre pour que les fleches soient sur une ligne de
> choix ? » — utilisateur, 2026-10-19, en réponse à la proposition : palette
> `↗ ↘ →` à l'entrée du curseur dans une case de flèche, au lieu d'un geste
> souris (survol + glisser) écarté.

## Contexte

Le gabarit du 2026-10-16 pose des flèches caractère, mais changer ou ajouter
une flèche obligeait à la copier ou à passer par le menu Symbole de Word. Une
première idée (survol de la case → curseur en croix → glisser pour dessiner)
demandait un hook souris global, le calcul des rectangles de cases à l'écran et
une fenêtre posée au-dessus de Word : trop de risques pour le gain.

## Décision

### Comportement

- Quand le curseur (sélection réduite) entre dans une **case de flèche**, une
  palette **horizontale** `↗ ↘ →` s'affiche sous le curseur, dans le style de la
  popup de conversion (fond blanc, opacité 0.5 au repos / 0.9 en navigation,
  surlignage bleu).
- Clavier, aligné sur la popup de conversion : **↓** entre en navigation,
  **← / →** déplacent le choix, **Entrée** valide, **↑** sort de la navigation,
  **Échap** ferme. Clic sur une flèche = validation directe.
- Valider **remplace le contenu de la case** par la flèche (un seul Ctrl+Z).
- La palette se ferme si le curseur quitte la case ou si l'élève tape du
  texte ; après une validation ou un Échap, elle ne se rouvre pas dans la même
  case tant que le curseur n'en est pas sorti.

### Repérage des cases de flèche : style de paragraphe

Les cases de flèche portent le style de paragraphe **« MathCursor Flèche »**
(centré, 20 pt), créé dans le document à l'insertion du tableau. C'est ce style
qui porte la taille des flèches (remplace la mise en forme directe du
2026-10-16). Avantages : invisible pour l'élève, voyage avec le document et le
copier-coller, suivi par Word quand on insère une colonne à côté.

### Code

- `UI/ArrowPalettePopupWindow.cs` : fenêtre WPF dérivée de la popup de
  conversion (mêmes réglages Win32 `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, owner
  re-posé à chaque affichage), mais en ligne et sans pied de popup.
- `Host/ArrowCellController.cs` : détection de la case sur `WindowSelectionChange`,
  affichage, écriture de la flèche.
- `ThisAddIn.cs` : branchement sur les handlers clavier existants ; la popup de
  conversion garde la priorité si elle est ouverte.

## Tradeoff & alternatives écartées

- **Geste souris (survol + glisser)** : hook souris bas niveau, rectangles de
  cases à recalculer (défilement, zoom, DPI), fenêtre transparente au-dessus de
  Word qui ne doit ni voler le focus ni bloquer le clic. Fragile, coût sur tout
  le système.
- **Réutiliser `SuggestionPopupWindow` telle quelle** : liste verticale, pied
  de popup (rappel pause, « Signaler une erreur »), validation branchée sur le
  pipeline de conversion. Une fenêtre dédiée garde les deux indépendantes.
- **Repérer les cases par la forme du tableau** (dernière ligne, colonnes
  paires, contenu `f(x)`) : casse dès que l'élève renomme, ajoute une ligne ou
  une colonne.
- **Marqueur sur le tableau (`Title`)** : ne dit pas quelle case est une case
  de flèche.

## Conséquences

- **Code touché** : `StudyTableInserter.cs` (style « MathCursor Flèche »),
  nouveaux `ArrowPalettePopupWindow.cs` / `ArrowCellController.cs`, `ThisAddIn.cs`
  (handlers ← → + priorités), csproj VSTO.
- **Documents** : un style « MathCursor Flèche » apparaît dans la liste des
  styles des documents qui ont un tableau de variation.
- Les tableaux insérés avant cette version (non livrée) n'ont pas la palette.

## Validation post-fix

Observation dans Word : entrée dans une case de flèche (clic, flèches du
clavier, Tab de cellule), choix au clavier et à la souris, Ctrl+Z, sortie de la
case, frappe dans la case, cohabitation avec la popup de conversion, colonne
insérée à côté d'une colonne de flèche.
