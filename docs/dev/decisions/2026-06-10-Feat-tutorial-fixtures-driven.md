# Feat — Tutoriel Word généré, DÉRIVÉ des fixtures triées par section mathématique

**Date :** 2026-06-10
**Kind :** Feat
**Température :** molle (curation des sections ajustable ; le principe « tuto = fixtures » est ferme)
**Statut :** acté
**Supersedes :** —
**Lié à :** ADR DocMath `2026-05-22-Feat-tutorial-docx-generated-onboarding` (feature d'origine, portée ici)

## Citation acté

> « on avait fait un truc cool c'est de générer au moment du build iss un document word de tuto qui s'ouvrait après l'install — tu peux retrouver la feature ? » puis « ok super, du coup il faudra aller prendre les fixtures et les trier par section Mathématiques. » — utilisateur, 2026-06-10

## Contexte

DocMath générait au build un tutoriel .docx (tableau consigne / case d'essai)
depuis une spec JSON, embarqué dans l'installer Inno Setup et proposé à
l'ouverture en fin d'install (checkbox `[Tasks]` + `[Run] shellexec
postinstall`). La spec DocMath décrivait l'ANCIEN moteur — fausse par
endroits après la journée (ex. `x2` y était « popup », c'est un auto).

## Décision

1. **Port tel quel** de `tools/TutorialBuilder/` (CLI dotnet + OpenXML pur,
   zéro XML brut — anti-MC0001) : `Program.cs`, `DocxRenderer.cs`,
   `Models/TutorialSpec.cs`, csproj.
2. **La spec est DÉRIVÉE des fixtures** : `tutorial-spec.fr.json` réécrite —
   14 sections MATHÉMATIQUES (fractions, puissances/indices, racines,
   vecteurs/géométrie, ensembles, intervalles, suites/limites,
   sommes/intégrales/dénombrement, complexes, nombres/unités, matrices,
   symboles/LaTeX, popup), 61 exercices dont CHAQUE consigne est une entrée
   EXACTE de `fixtures.json` (input, top, décision auto/popup, alternative).
3. **Test anti-péremption** (`TutorialSpecTests`, engine tests) : chaque item
   du tuto doit matcher sa fixture (existence, décision, top, alt). Une
   régénération de fixture qui touche le tuto CASSE le build → le tuto ne
   peut pas mentir aux élèves.
4. **Branchement installer** : EN ATTENTE — ce repo n'a pas encore
   `adapter-vsto/installer/`. À reprendre de DocMath au port de l'installer :
   hook `build.ps1` (dotnet run TutorialBuilder → payload/), `[Files]`
   (DestDir `{userdocs}\MathCursor`, flag `uninsneveruninstall`), `[Tasks]`
   checkbox `opentutorial`, `[Run]` `shellexec postinstall nowait skipifsilent`.

## Tradeoff & alternatives écartées

- **Reprendre la spec DocMath telle quelle** : périmée (sup-only, etc.) et
  non vérifiée — le lien fixtures rend le tuto exact par construction.
- **Générer le tuto entièrement automatiquement depuis les fixtures** : le
  corpus contient des sondes non pédagogiques (mutations NBSP, erreurs US,
  protections « sinus ») — la CURATION (choix + consignes rédigées) reste
  humaine, la VÉRITÉ (entrées/sorties) vient des fixtures.
- **Spec EN** : différée (produit FR d'abord).

## Conséquences

- **Nouveaux fichiers** : `tools/TutorialBuilder/*` (port), spec FR v2.0,
  `engine/tests/.../TutorialSpecTests.cs`.
- **Sortie** : `dotnet run --project tools/TutorialBuilder -- --in
  tutorial-spec.fr.json --out <payload>/MathCursor-Tutoriel-fr.docx`
  (validé : 14 sections / 61 items, .docx généré).
- Le tuto sert aussi de doc d'onboarding beta (consigne suites « indice = _ »
  en bonne place, section puissances).

## Validation post-fix

- `TutorialSpecTests` vert (61/61 consignes = fixtures exactes).
- Ouvrir `tools/TutorialBuilder/bin/MathCursor-Tutoriel-fr.docx` dans Word
  et dérouler les exercices avec l'add-in actif.
