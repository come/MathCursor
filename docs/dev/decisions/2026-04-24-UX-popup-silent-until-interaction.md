# UX — Popup silencieuse jusqu'à la première interaction utilisateur

**Date :** 2026-04-24
**Kind :** UX
**Température :** molle
**Statut :** acté

## Décision

À l'ouverture d'un document Word qui contient déjà du texte
mathématique-like (équations, expressions, etc.), la popup MathCursor
**n'apparaît PAS automatiquement**. Elle reste silencieuse tant que
l'utilisateur n'a pas interagi (clic ou frappe). Une fois interaction
détectée, le comportement normal reprend pour le reste de la session Word.

Critère d'interaction : la position du caret a changé depuis le 1er tick
qui a suivi l'installation de `SuggestionService`.

## Pourquoi

- Ouvrir un doc existant et se voir balancer la popup sans avoir rien fait
  est intrusif — l'utilisateur n'a pas demandé à analyser ce contenu.
- Le déclenchement implicite à l'ouverture peut faire croire à un bug ou à
  un add-in trop bavard.
- Une fois que l'utilisateur tape ou clique, son intention d'éditer est
  claire → la popup peut reprendre son rôle d'aide à la saisie.

## Conséquences

- `SuggestionService` ajoute deux états :
  - `_initialCaretPos` (sentinelle `-1`) : position au tout premier tick.
  - `_userInteracted` (bool) : true dès qu'un tick voit `caretPos !=
    _initialCaretPos`.
- Dans `CheckAndUpdate` : si `_userInteracted` est false, on continue à
  faire le tick (pour détecter le mouvement) mais on appelle `HidePopup()`
  et on `return` avant le NER.
- Comportement post-interaction strictement inchangé.
- Esc → `HidePopup` ne ré-arme PAS la garde (elle est one-shot pour la
  session). Une fois levée, elle ne se remet pas.
- `WindowDeactivate` → `WindowActivate` (alt-tab vers Word) ne ré-arme pas
  non plus : si l'utilisateur a déjà interagi, c'est cuit.

## Validé par l'utilisateur

> "quand on ouvre un word, la popup ne devrait pas etre montrée par defaut
> (attendre le premier clic ? ou premoiere frappe)"

## Statut

acté
