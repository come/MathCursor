# Limit — OMath display recentre après fusion ¶ via Backspace (limite Word)

**Date :** 2026-05-05
**Kind :** Limit
**Température :** forte (= acceptée comme limite, pas de fix automatique côté add-in)
**Statut :** acté

## Contexte

Lors d'un cross-merge multi-ligne (cases ou align*) ou d'une conversion
single-line standalone, on pose `<m:oMathPara><m:oMathParaPr><m:jc m:val="left"/>`
sur l'OMath inséré pour qu'il s'affiche aligné gauche (sinon Word centre
par défaut pour display math).

**Bug observé** : si l'utilisateur fusionne deux ¶s via Backspace (cas
typique : delete `<=> ` ou `{ ` auto-injecté puis Backspace sur le ¶
mark pour rejoindre le ¶ OMath), Word **strip activement le
`<m:oMathParaPr>`** dans l'XML résultant tout en gardant le wrapper
`<m:oMathPara>`. Conséquence : l'OMath repasse en display centré (default
Word).

Diff observé sur un docx user :

```diff
 <m:oMathPara>
-  <m:oMathParaPr><m:jc m:val="left"/></m:oMathParaPr>
   <m:oMath>...</m:oMath>
 </m:oMathPara>
```

## Décision

**On accepte cette limite comme un comportement Word non contournable
sans hack.** Pas de fix automatique. L'utilisateur peut re-aligner via
le bouton « Aligner à gauche » du ribbon Word si besoin.

## Pourquoi pas de fix

Plusieurs options ont été explorées et écartées :

### Option A — Re-patch défensif sur `WindowSelectionChange`

Hook : si caret atterrit dans un de nos OMaths (= avec bookmark `mcEq_*`)
ET que `<m:oMathParaPr>` est manquant, re-poser `m:jc=left` via
`Range.InsertXML`.

**Écartée** : ressenti hacky par l'utilisateur. Polling sur chaque
sélection, même limité par garde-fous, dégage une mauvaise odeur.

> Citation user : « non hors de question ton fix :D y'a pas moyen de
> regler l'alignement plus proprmeent ? »

### Option B — Strip le wrapper `<m:oMathPara>` complètement

Au lieu de patcher `m:jc=left`, dégager tout le wrapper display →
l'OMath devient `<m:oMath>` inline → pas de centrage par défaut
possible.

**Écartée** : Word **auto-promote** un OMath inline standalone-in-¶ en
display sans `<m:oMathParaPr>` → centré quand même. Et ça cassait le
single-line `Y=2X+1` qui s'affichait centré au lieu de gauche.

### Option C — Refactor list-mode : `\v` au lieu de `\r`

Remplacer le ¶ break entre OMath et `{ `/`<=> ` auto-injecté par un
line break (`\v`) → tout dans le même ¶, pas de fusion ¶ possible
même au Backspace.

**Écartée** : gros refactor. Casse la cascade cross-merge qui itère
`doc.Paragraphs`. ROI faible vu que le bug n'apparaît que post-Backspace
volontaire de l'utilisateur (= rare).

### Option D — Alignement via `<w:pPr><w:jc>` du ¶

Tester si Word fallback sur le `<w:jc>` du paragraphe quand
`<m:oMathParaPr>` est absent.

**Écartée** : testé empiriquement avec un docx patché manuellement,
Word **ignore le `<w:jc>` du paragraphe** pour le rendu display math.

> Citation user après test : « nope »

### Option E — Paramètre alignment directement sur `<m:oMath>`

Vérifié dans la spec OOXML : **n'existe pas**. L'alignement display
math est défini uniquement au niveau `<m:oMathPara><m:oMathParaPr><m:jc>`.
Ni `<m:oMath>`, ni `<m:eqArr>`/`<m:eqArrPr>` (vertical baseJc seulement),
ni `<m:m>`/`<m:mPr>` (per-column dans matrix mais on utilise eqArr) ne
proposent d'attribut horizontal qui survive à la fusion.

> Citation user : « ok on va rien faire on documente la limite »

## État du code après décision

`OMathParaJcPatcher` garde :
- `EnsureDisplayWithLeftJc` (entry point) : enrobe inline pur en display+jc=left,
  ou patch si display déjà présent. Appelée à l'insertion via XML transplant.
- `Patch` (helper bas niveau, 4 cas).

Helpers retirés au cleanup : `StripOMathParaWrapper`,
`NeedsLeftAlignmentRestore` et leurs regex (jamais utilisés en prod).

## Workaround utilisateur

Si l'utilisateur observe le centrage post-Backspace, il peut :
1. Sélectionner l'OMath (clic dedans).
2. Cliquer « Aligner à gauche » dans le ribbon Word (Accueil > Paragraphe).

Le `m:jc=left` sera reposé manuellement et tient jusqu'à la prochaine
fusion ¶.

## Liens

- ADR multi-ligne list-mode visible : [`2026-05-05-Feat-multiline-list-mode-visible.md`](2026-05-05-Feat-multiline-list-mode-visible.md)
- ADR cases multi-ligne Phase 2 : [`2026-05-05-Feat-cases-multiline-phase2.md`](2026-05-05-Feat-cases-multiline-phase2.md)
- Helper `OMathParaJcPatcher.cs` : `adapter-vsto/src/MathCursor/Host/OMathParaJcPatcher.cs`
