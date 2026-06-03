# Feat — Insertion OMML (structure native) au lieu de UnicodeMath + BuildUp

**Date :** 2026-06-02
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** — (rend `LatexToUnicodeMath` obsolète, suppression dans un fix suivant)
**Lié à :** `word-api-helpers.md` §4 ; remplace le pansement ▒ n-aryand (commit
e243428, jamais acté en ADR — annulé avant de figer une décision)

## Citation acté

> « ah bah oui ca me plait ca => latex directement » … « c'est tres simple pourquoi tu vas chercher un caracteres d'insertion foireux » … « oui » (POC validé) — utilisateur, 2026-06-02

## Contexte

L'add-in convertissait LaTeX → **UnicodeMath** (ligne linéaire) puis
`TypeText` + `OMaths.BuildUp()`. Word **re-parse** cette ligne avec sa propre
précédence → bugs : `lim_(x→0) 1/(x+1)` rendu `\frac{lim 1}{x+1}` (lim happe le
numérateur). Aucun correctif linéaire propre (parenthèses visibles,
function-apply, `▒` : tous KO en Word ou foireux). Cause racine : on donne à
Word une **ligne ambiguë** qu'il réinterprète.

## Décision

Insérer directement l'**OMML** (OfficeMath, le XML natif où Word stocke ses
équations) via `Range.InsertXML`. Word **ne re-parse rien** : la structure est
explicite (`<m:f>`, `<m:func><m:limLow>`, `<m:nary>`, `<m:rad>`, `<m:sSubSup>`,
`<m:acc>`…).

- **Émetteur** `MathCursor.Core.LatexToOmml` : LaTeX → `<m:oMath>`. Validé sur
  15 constructions (batterie debug) : frac, racine(+deg), exp/indice/combiné,
  lim, somme/intégrale (nary), vec/accents, ensembles, relations, imbriqué.
  - **Délimiteurs `\left`/`\right`** → `<m:d>` auto-sizé (begChr/endChr,
    imbrication gérée). Sans ça, ils fuyaient en texte brut : `f(x)` rendu
    « fleft(xright) » en Word. 8 tests Core (`LatexToOmmlDelimiterTests`).
- **Intégration chirurgicale** dans `InsertOMathAt` : seules les ex-étapes 5-6
  (TypeText unicodeMath + BuildUp) sont remplacées par `BuildOMathViaOmml`
  (placeholder 1-char **après le ZWSP** → InsertXML sur range locale). **Tout
  le reste est inchangé** : normalize bornes, ZoneCleaner, ZWSP caché, anchor
  CC, DecideOMathTyping (Display/Inline), Justification, re-probe, Tag.
- **Échappement caret** (step 7) : après l'insert, `SetRange(om.End)` +
  **`MoveRight(wdCharacter,1)`**. Word laisse le caret en « math input mode »
  à `om.End` → la frappe suivante sortait en italique math (bug retour-saisie).
  `MoveRight` **franchit la frontière de l'OMath et clôt la saisie math** (=
  flèche droite). Technique élue par POC (7 candidates) : seuls `MoveRight` et
  `EndKey` donnent du texte plat ; `MoveRight` est retenu car **local** (reste
  collé à l'équation, `EndKey` saute en fin de ligne). Validé en **normal +
  tableau + liste**.
- **Local** : on ne lit que le WordOpenXML du **paragraphe courant** (1 ¶,
  iso-perf), jamais le doc entier. **Mesuré** (bouton Perf probe) : doc vide vs
  doc 44 587 chars (×44 000) → total insert **203 ms vs 204 ms (plat)**,
  InsertXML 122/113 ms, lecture WordOpenXML 39/49 ms. Le WordOpenXML grossit
  (62→240 Ko) mais avec le **catalogue de styles** du doc (toujours 1 `<w:p>`),
  pas avec la longueur du texte — coût de lecture ~50 ms, plat. Aucun
  `doc.OMaths`/`doc.ContentControls`/WordOpenXML doc-entier dans le chemin ;
  `ZoneCleaner` ne balaie que la zone, `DecideOMathTyping` que le ¶ courant.

## POC validés (avant prod, règle Word-API)

1. OMML rendu correct (lim/fraction) — structure gardée par Word.
2. **Chemin complet** : ZWSP → oMath inline (même ¶) → anchor CC →
   `CcMetaResolver.ResolveAt(om)` retrouve le **backlink** ✅. L'OMML s'insère
   donc dans le pattern existant sans le casser.
3. Batterie 15 constructions : toutes structurellement correctes.

## Tradeoff & alternatives écartées

- **UnicodeMath + BuildUp** (existant) : re-parsing Word ambigu → bugs de
  précédence insolubles proprement (lim, …).
- **Basculer Word en mode LaTeX** (registre/ExecuteMso) : hack global fragile,
  refusé par l'utilisateur.
- **`▒` n-aryand** : pansement sur le symptôme, foireux en Word.

## Conséquences

- **Code touché** : nouveau `Core/LatexToOmml.cs` (+ `\left`/`\right` → `<m:d>`) ;
  `InsertOMathAt` ex-étapes 5-6 remplacées par `BuildOMathViaOmml` + échappement
  caret `MoveRight` (step 7).
- **Tests** : `LatexToOmmlDelimiterTests` 8/8 verts (Core) ; batterie Word
  `WordScenarioRunner` étendue de 5 scénarios (bug lim Display+Inline, entre 2 ¶
  pleins, remplacement intra-merge, retour-saisie).
- **Garde-fou** : `LatexToUnicodeMath` reste appelé en early-bail (valide le
  LaTeX) — suppression complète repoussée à un fix dédié.
- **Reste à nettoyer** (après ce palier) : variantes debug obsolètes
  (`RunPocInlineA/B`, `RunVariantG*`, anciens POC), couche UnicodeMath de
  l'insert, ADR ▒.

## Validation post-fix (faite — batterie Word 16/17)

Run `WordScenarioRunner` en doc vierge : **16 PASS / 1 FAIL**.
- ✅ **bug lim/fraction MORT** : Display + Inline, preuve structurelle
  `<m:limLow>` AVANT `<m:f>` (le lim englobe la fraction, plus de `\frac{lim}{}`).
- ✅ `f(x)` rendu correct (plus de « fleft(xright) »).
- ✅ retour-saisie (frappe après l'équation en texte plat), insert entre 2 ¶
  pleins, prose, display, listes, cellules, cross-merges align*/cases.
- ⚠️ **1 FAIL = `Remplacement intra-merge`** : `IntraOMathsMerger` ne retrouve
  pas le CC ancre de l'OMath voisine (« OMath orpheline »). **Bug PRÉ-EXISTANT**
  (cf. `project_bug_intra_merge_display`, AnchorProbeMaxDelta vs gap structural),
  **indépendant de l'OMML** — les cross-merges passent. À traiter séparément.
