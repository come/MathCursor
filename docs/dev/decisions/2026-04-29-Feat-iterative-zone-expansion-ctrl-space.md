# Feat — Extension itérative de la zone via Ctrl+Espace répété

**Date :** 2026-04-29
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Quand la popup est déjà ouverte, chaque appui supplémentaire sur Ctrl+Espace
**étend la zone d'un cran vers la gauche** jusqu'au prochain stopword, délimiteur
de ponctuation, fin d'OMath précédent, ou début de paragraphe. La popup
re-render avec les nouvelles propositions sur la zone élargie.

Quand la popup ferme (Esc, validation, déplacement curseur, frappe, click
ailleurs), l'état d'extension se **reset** : le prochain Ctrl+Espace repart
d'une détection neuve sur la zone initiale calculée par `ComputeManualSpanStart`.

L'extension est **uniquement vers la gauche** (vers la droite hors scope).
Si on atteint le début du paragraphe, plus d'extension — la popup reste sur
la dernière zone valide. Comportement silencieux (pas de bip).

## Pourquoi

Aujourd'hui Ctrl+Espace ouvre la popup sur une zone calculée une fois pour
toutes. Si la zone est trop courte (NER manqué un keyword en début, span
heuristique tronquée par un stopword serré), l'élève doit fermer la popup,
déplacer le caret, et re-tenter. Friction inutile.

L'extension itérative donne le contrôle à l'utilisateur : un appui = un cran.
Granularité fine, prévisible, auto-correctrice (si on étend trop, Esc pour
recommencer).

## Conséquences

### Code (couche 3 — adapter VSTO)

- **SuggestionService.cs** : nouvel état session
  - `_iterativeParagraph` (string)
  - `_iterativeParaAbsStart` (int)
  - `_iterativeSpanStart` (int, offset dans paragraph)
  - `_iterativeSpanEnd` (int, fin = caret au moment du 1er trigger)
  - `_iterativeOMathRegions` (snapshot)
- **`TriggerManual()`** détecte si popup déjà ouverte + état d'expansion actif :
  - Oui → appelle nouveau `ExtendOneStop()` qui remonte d'un cran
  - Non → flow actuel + initialise l'état
- **`ExtendOneStop()`** : appelle `ComputeManualSpanStart(text, _iterativeSpanStart, omaths)`
  pour trouver la borne avant la borne actuelle (= un cran de plus à gauche).
  Si la nouvelle borne == ancienne, on est au début → no-op.
- **Reset** dans `HidePopup()` + `OnSelectionChange()` (quand caret se
  déplace volontairement).

### Tests

Pas de tests automatisés ajoutés (logique VSTO Word, mock complexe). Tests
manuels dans Word obligatoires (cf. brief §5).

### Réutilisation

- **Stopwords** : réutilisation de `ManualTriggerStopwords` existant (FR).
- **Délimiteurs** : réutilisation de `ManualTriggerDelimiters`.
- **Borne calc** : réutilisation de `ComputeManualSpanStart` (déjà implémenté
  pour le trigger initial). Pas de nouvelle logique de tokenisation.

## Validé par l'utilisateur

Brief complet :
[`docs/dev/briefs/2026-04-29-iterative-zone-expansion-ctrl-space.md`](../briefs/2026-04-29-iterative-zone-expansion-ctrl-space.md)

Direction (sélection des briefs à attaquer) :
> "iterative et implication et merge"

## Statut

acté
