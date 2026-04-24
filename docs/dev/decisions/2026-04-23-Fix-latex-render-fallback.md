# Fix — Rendu LaTeX : fallback sur `HasError`

**Date :** 2026-04-23
**Kind :** Fix
**Température :** molle
**Statut :** acté

## Décision

Après création du `FormulaControl` (WpfMath), on check `HasError` et `Errors.Count` ;
si non vide → afficher un `TextBlock` unicode avec la source brute au lieu du
rendu dégénéré.

## Pourquoi

- WpfMath rendait un `.` solitaire quand il acceptait partiellement une formule
  mais ne pouvait pas la dessiner. Expérience utilisateur catastrophique : un
  point mystérieux au milieu de la popup, incompréhensible.
- Première tentative via pré-parse `TexFormulaParser.Parse` était trop stricte
  (`dynamic` dispatch + vérification `RootAtom` rejetait des formules que
  `FormulaControl` savait rendre). Régression immédiate.
- La propriété `HasError` est alimentée par `FormulaControl` lui-même dans son
  setter `Formula` → même parser, même lenience. C'est le signal propre.

## Conséquences

- `SuggestionPopupWindow.RenderMath` : check `HasError && Errors.Count == 0`
  avant d'accepter le rendu.
- Si erreur → `TextBlock` en Cambria Math avec la source LaTeX brute. Moche
  mais lisible et franc.

## Validé par l'utilisateur

> "tu m'a cassé ma visualisation latex la"

(Signalement d'une régression sur une tentative précédente — le fix `HasError`
est la correction qui a suivi, sans complaint ultérieur.)

## Statut

acté
