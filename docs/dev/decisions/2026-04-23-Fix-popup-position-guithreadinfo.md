# Fix — Position popup via GetGUIThreadInfo

**Date :** 2026-04-23
**Kind :** Fix
**Température :** molle
**Statut :** acté

## Décision

Calcul de la position popup via `GetGUIThreadInfo` (atomic `hwndCaret` + `rcCaret`)
au lieu de `GetCaretPos` + `GetFocus` séparés.

## Pourquoi

- Bug reproduit : dès qu'un OMath existait dans le document, la popup se
  positionnait au mauvais endroit.
- Cause : `GetCaretPos` renvoie des coordonnées relatives à la fenêtre qui
  **possède** le caret, mais on convertissait via `GetFocus()` qui peut pointer
  sur une sous-fenêtre différente (Word a plusieurs HWND internes dès qu'il y
  a du math dans le doc : éditeur math, pane texte, etc.).
- `GetGUIThreadInfo` renvoie `hwndCaret` et `rcCaret` atomiquement, plus de
  désynchro possible.

## Conséquences

- `SuggestionService.GetCaretScreenPosition` réécrit.
- Offset Y ramené de +22 à +4 puisqu'on part de `rcCaret.Bottom` (base du caret)
  au lieu de `rcCaret.Top`.

## Validé par l'utilisateur

> "ok on tente"

(Proposition après diagnostic du bug, approche tentée immédiatement.)

## Statut

acté
