# Feat — Mode liste cases `{` Phase 2 (multi-ligne + list-mode visible)

**Date :** 2026-05-05
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Contexte

Le marker `{` (système d'équations, `\begin{cases}...\end{cases}`) était
prévu dans le brief 30-04 mais hors V1 (Phase 1 = align uniquement). La
Phase 1 (align) + le list-mode visible sont validés. On peut maintenant
activer Phase 2 cases.

État actuel du codebase (cf. survey 05-05) :
- AST `MultiLineBlock(Mode="cases")` + renderer LaTeX `\begin{cases}` :
  **existent** (`AstNodes.cs:238`, `LatexRenderer.cs:237`).
- Conversion LaTeX→OOXML `\begin{cases}` → `{█(...)┤` : **existe**
  (`LatexToUnicodeMath.cs:48`).
- Parser `TryParseMultiLineBlock` détecte uniquement align : **à étendre**.
- Adapter `AlignMarkers` + cascade `TryCascadeAbsorbMarkerChain` :
  **ne connaissent pas `{`**, à étendre.

Cf. brief : [`docs/dev/briefs/2026-05-05-cases-multiline-phase2.md`](../briefs/2026-05-05-cases-multiline-phase2.md).

## Décision

### Comportement utilisateur

1. **Création système** : tape `{ x+y=5` puis Ctrl+Espace → conversion
   en single-line cases → list-mode actif → `{ ` auto-injecté sur le ¶
   d'ancrage en dessous.
2. **Extension** : tape `2x-y=1` derrière le `{ ` injecté → ligne devient
   `{ 2x-y=1` → Enter → cross-merge cases → bloc multi-ligne étendu →
   nouveau `{ ` injecté en dessous.
3. **Sortie principale** : Backspace pour effacer le `{ ` injecté.
   L'user voit le marker en texte plain et comprend qu'il peut l'effacer.
4. **Sortie fallback** : Enter sur ligne contenant uniquement `{ ` →
   strip + désactivation (cohérent avec align).

### Règles de détection

**Marker `{` détecté SI ET SEULEMENT SI suivi d'un espace.** Sans espace,
ce n'est pas un système :
- `{ x = 1` → cases ✓
- `{1, 2}` → ensemble en extension, **pas** un système
- `{}` → ensemble vide, **pas** un système

Cette règle s'applique à **deux endroits** :
1. `StartsWithCasesMarker(line)` (cross-merge cascade)
2. `StartsWithKnownMarker(trimmed)` (state machine `{` doit aussi exiger
   l'espace, pour éviter qu'un Backspace partiel sur le `{ ` injecté ne
   transforme `{1,2}` en faux ValidateAsIs)

### Pas de mix align/cases

Les deux cascades sont **mutuellement exclusives** : la cascade cases
absorbe uniquement les ¶ commençant par `{ `, la cascade align uniquement
les markers align. Si l'user mélange (ligne en cases + ligne en align
juste en dessous), la cascade s'arrête à la frontière, pas de fusion.

Cf. brief 30-04 §3.4 « Mix de modes cases vs align ».

### Architecture (testable)

- **Nouveau helper pur** `CasesCascadeMerger` (sur le modèle de
  `RevertedZoneMerger`) : prend une liste de textes de paragraphes et un
  current source, retourne `(chainStartIndex, mergedSource)` ou null.
  Logique de cascade extraite hors de SuggestionService → testable sans
  Word interop.
- **Détection** : helper statique `StartsWithCasesMarker(line)` (peut
  vivre dans `CasesCascadeMerger` ou un helper utilitaire séparé).
- **Dispatch** dans `TryFindCrossMergeAbove` : selon le marker du
  current source (cases ou align), appelle la cascade appropriée.
- **Parser core** : `TryParseMultiLineBlock` étendu pour détecter le
  mode (lignes commencent toutes par `{ ` → mode "cases", lignes
  commencent toutes par marker align → mode "align").
- **List-mode** :
  - `ListModeStateMachine.KnownMarkers` ajoute `"{"` avec règle stricte
    « `{` + espace » dans `StartsWithKnownMarker`.
  - `SuggestionService.ExtractMarkerFromMergedSource` reconnaît `{`.
  - `InjectListModeMarker` : aucun changement (`ListModeMarkerInjector.Plan`
    est déjà générique sur le marker).
- **Activation single-line cases** : dans `CommitLatexAndOMathCore`,
  après le bloc cross-merge, si commit non-cross-merge mais le LaTeX
  émis commence par `\begin{cases}` → activer list-mode + injecter `{`.

## Tradeoff

- **Pro** : ergonomie cohérente avec align (même UX d'auto-injection),
  l'user qui sait écrire un système découvre l'extension naturellement.
- **Pro** : code adapter mieux structuré — extraction de la cascade en
  helper pur permet de tester les transitions sans Word.
- **Con** : un peu de duplication entre cascade align et cascade cases
  (deux fonctions similaires). Acceptable parce que les règles de
  détection diffèrent et qu'on veut éviter de mixer.
- **Risque marginal** : `{ 1, 2 }` (avec espaces, set en extension écrit
  par un user) serait à tort traité comme cases. À documenter — pas un
  vrai problème en pratique.

## Validé par l'utilisateur

> « pour moi { ligne1 => on active l'auto sur la ligne 2 on rajoute {
> pour indiquer à l'utilisateur qu'il est toujours dans le systeme. si
> il veut en sortir il fait backspace et il recree une ligne »

> « oui helper pur => puis go adr »

(Validation après proposition Q1 = activation single-line + Q2 = helper
pur testable comme `RevertedZoneMerger`.)

## Plan d'implémentation (ordre)

1. **Tests-first** :
   - State machine : tests `{` marker + règle stricte espace
   - `ListModeMarkerInjector` : Theory ajoutée pour `{` (pas de nouveau test, déjà couvert)
   - `CasesCascadeMerger` : tests purs cascade (chainage, stop sur
     marker align, stop sur ¶ vide, stop sur OMath sommet)
   - Core parser : tests `TryParseMultiLineBlock` mode cases
   - Core renderer : test `MultiLineBlock(cases)` rendering
2. **Parser core** : étendre `TryParseMultiLineBlock` pour détecter cases
3. **Adapter** : `CasesCascadeMerger` extrait + dispatch dans
   `TryFindCrossMergeAbove`
4. **State machine** : ajouter `{` dans `KnownMarkers` avec règle stricte
5. **List-mode activation single-line** : détection `\begin{cases}` dans
   `CommitLatexAndOMathCore`
6. **Test manuel Word** : scénarios A, B, C, D du brief

## Liens

- Brief : [`2026-05-05-cases-multiline-phase2.md`](../briefs/2026-05-05-cases-multiline-phase2.md)
- Brief original Phase 1 : [`2026-04-30-multiline-systems-equivalences.md`](../briefs/2026-04-30-multiline-systems-equivalences.md)
- ADR list-mode visible : [`2026-05-05-Feat-multiline-list-mode-visible.md`](2026-05-05-Feat-multiline-list-mode-visible.md)
- ADR cross-merge pipeline : [`2026-05-04-Meta-cross-merge-pipeline-refactor.md`](2026-05-04-Meta-cross-merge-pipeline-refactor.md)
- ADR cascade multi-ligne : [`2026-05-04-Feat-multiline-edit-cascade-merge.md`](2026-05-04-Feat-multiline-edit-cascade-merge.md)
