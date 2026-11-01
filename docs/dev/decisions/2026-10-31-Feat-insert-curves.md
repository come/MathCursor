# Feat — Insérer des courbes (bouton Courbe, image modifiable)

**Date :** 2026-10-31
**Kind :** Feat
**Température :** molle
**Statut :** acté (UX fenêtre + double-clic remplacée)
**Superseded by :** [2026-10-31-UX-curve-floating-panel](2026-10-31-UX-curve-floating-panel.md) (partiel)
**Supersedes :** —
**Lié à :** [2026-06-11-Feat-hash-source-map-no-cc](2026-06-11-Feat-hash-source-map-no-cc.md) (source de l'équation sous le caret), [2026-06-20-Feat-encadres-callouts-boxed](2026-06-20-Feat-encadres-callouts-boxed.md) (modèle de bouton périphérique), plan validé `curried-juggling-heron.md`

## Citation acté

> « il faudrait que ça soit super ergonomique. un bouton dans le ruban, et facile d'ajouter
> une courbe ou plusieurs après » — utilisateur, 2026-10-31

> Style : « Au choix dans les Paramètres » ; périmètre v1 : « Plusieurs courbes sur un
> repère, Intervalle et repère réglables, Image modifiable » — réponses aux questions de
> cadrage, 2026-10-31, puis approbation explicite du plan.

## Contexte

Un dessin de courbes fait en matplotlib aide à « sentir »
un résultat (lim x/√x). Un élève doit pouvoir faire pareil dans Word, d'un clic, à partir
de la fonction qu'il vient de taper. Rien n'existait : pas d'évaluateur numérique (moteur,
sérialisation, Rust), pas d'insertion d'image dans l'add-in.

## Décision

- **Bouton Courbe** dans le ruban (groupe mise en page). Il ouvre une fenêtre « Courbes » :
  une ligne par fonction, tapée comme dans MathCursor (`rac x`, `x^2 - 1`), « + Ajouter une
  courbe », repère réglable (x de … à …, y auto ou réglé, grille), aperçu en direct,
  Entrée = Insérer. Curseur sur une équation `f(x) = …` → la fenêtre s'ouvre pré-remplie.
- **Image modifiable** : PNG inséré (`InlineShapes.AddPicture`), `Title =
  MathCursor.Curve:<id>`, spec (fonctions, repère, style) dans `CustomXMLParts`. Double-clic
  sur l'image (ou bouton avec l'image sélectionnée) → la fenêtre rouvre, Insérer remplace.
- **Style** : propre (défaut) ou main levée, réglage `CurveStyle` dans les Paramètres.
- **Moteur pur** : compilation steno → fonction numérique (`ForestEngine.TryCompileFunction`,
  même lecture que la popup) et échantillonnage (coupures aux discontinuités, y auto,
  graduations) dans `engine/` (netstandard2.0). Pixels et Word dans `adapter-vsto/`.
  Une expression non prise en charge échoue avec un message, jamais une courbe fausse.

## Alternatives écartées

- Courbe en OMML ou en formes Word dessinées : illisible à produire, pas de style main levée.
- Évaluer depuis le LaTeX stocké, dans l'adapter : refait un parseur ; le moteur a déjà l'arbre.
- Spec dans `AlternativeText` seul : réservé à l'accessibilité (on y met une description
  lisible) ; la règle projet est `CustomXMLParts`.

## Conséquences

- Nouvelle API publique du moteur, **sans parité Rust en v1** : VSCode et LibreOffice n'ont
  pas la fonctionnalité. À porter si elle y est demandée.
- Copier l'image dans un autre document perd la spec (l'image reste, non modifiable).
- Fichiers : `engine/src/MathCursor.Engine/Plot/*`, `adapter-vsto/src/MathCursor/Host/Curves/*`,
  `Ribbon.xml`, `RibbonCallback.cs`, `Strings.cs`, `AppSettings`/`SettingsStore`/`SettingsWindow`,
  les 2 csproj de l'adapter.
