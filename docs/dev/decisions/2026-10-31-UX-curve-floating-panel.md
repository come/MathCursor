# UX — Courbes : dessin inséré tout de suite, panneau de réglages qui suit l'image

**Date :** 2026-10-31
**Kind :** UX
**Température :** molle
**Statut :** acté
**Supersedes :** [2026-10-31-Feat-insert-curves](2026-10-31-Feat-insert-curves.md) (partie « fenêtre modale + double-clic » seulement ; moteur, rendu, stockage et insertion restent)
**Lié à :** [2026-09-04-Fix-popup-owner-multiwindow](2026-09-04-Fix-popup-owner-multiwindow.md) (fenêtres WPF possédées par Word)

## Citation acté

> « j'aurai voulu mieux integré, un peu comme un tableau dans word, l'image arrive vite avec
> un repere, et quand on clique dessus les options et controles arrivent en popup sur la
> droite » — utilisateur, 2026-10-31

> « pour moi comme le tableau, je prefere faire l'effort de faire suivre le panneau de
> controle à coté de la courbe. on peut tenter ? » — utilisateur, 2026-10-31 (préféré au
> volet de tâches ancré qui lui était proposé)

## Décision

- **Bouton Courbe** : le dessin est inséré **tout de suite** (repère par défaut, avec la
  fonction sous le caret si elle se trace, sinon vide), puis sélectionné.
- **Dessin sélectionné** (clic simple, event natif `WindowSelectionChange`) → un **panneau
  flottant** s'ouvre à droite de l'image (à gauche s'il n'y a pas la place) : fonctions
  (+ Ajouter, ✕, rendu de la lecture ou raison de l'échec), repère, grille, style. Sélection
  ailleurs → le panneau se ferme.
- **Pas de bouton Insérer** : chaque modification traçable met l'image à jour dans le
  document (après une courte pause de frappe, à l'ajout/retrait d'une courbe), une entrée
  Ctrl+Z par mise à jour ; l'image est resélectionnée pour garder le panneau.
- **Le panneau suit l'image** au défilement et au zoom : sa position est relue
  (`Window.GetPoint` sur la plage de l'image) par un minuteur **uniquement tant qu'il est
  ouvert**, faute d'événement de défilement dans Word. Image hors écran, ou Word au second
  plan → panneau masqué.
- Le hook clavier MathCursor est suspendu tant que le panneau a le focus (sinon
  Ctrl+Espace et la popup se déclencheraient dans ses champs).
- Échap dans le panneau rend la main au document, curseur après l'image.

## Compléments (2026-11-01)

> « idealement il faudrait la popup math cursor dans le tapage de courbe ? avec les options ? »
> puis « zoom des axes -> idealement un scroll au hover de l'image QUAND elle est cliquée, ca
> zoom les axes ? ok pour 1. et rajoute un onglet settings avec les reperes / le style »
> — utilisateur, 2026-11-01

- **Lectures façon popup** dans le panneau : sous la ligne qui a le focus, les lectures
  **traçables** de la saisie, dans l'ordre de la popup (`ForestEngine.FunctionReadings`) ;
  ↑/↓ ou clic pour en changer, effet immédiat sur le dessin. La lecture choisie est
  mémorisée par ligne (`<f pick="LaTeX">`), sinon celle de tête. La vraie popup de Word n'est
  pas réutilisée : elle est liée au caret du document, au hook et à l'insertion.
- **Onglets** : Fonctions | Réglages (repère, bouton Automatique, style, grille).
- **Molette sur l'image sélectionnée** = zoom du repère autour du pointeur (×0,8 par cran),
  les champs du repère suivent. Ctrl+molette reste le zoom de Word. Hook souris du thread de
  Word, posé seulement panneau ouvert.

## Alternatives écartées

- Volet de tâches Word ancré à droite (`CustomTaskPane`) : plus simple et robuste, mais loin
  du dessin ; l'utilisateur veut le geste « comme un tableau ».
- Garder la fenêtre modale : bloque le document, impose Insérer.

## Conséquences

- Le minuteur de position est une exception assumée à « pas de polling » : il ne tourne que
  panneau ouvert et ne lit qu'une position, jamais le contenu.
- Le panneau prend le focus (il faut y taper) : contrairement aux popups au caret, il n'est
  pas `WS_EX_NOACTIVATE`. Il tourne sur son propre thread WPF et **n'a pas Word pour
  propriétaire** : un lien propriétaire entre threads fusionne leurs files d'entrée et Word
  reprenait le focus à chaque clic (essai du 31/10). Il est `Topmost`, et masqué dès que ni
  Word ni lui ne sont au premier plan.
- Fichiers : `UI/CurvePanel.cs` (remplace `UI/CurveWindow.cs`), `Host/Curves/CurveController.cs`,
  `ThisAddIn.cs`, `Strings.cs`, csproj.
