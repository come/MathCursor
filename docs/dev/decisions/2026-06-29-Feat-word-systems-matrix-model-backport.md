# Feat — Word : refonte des systèmes « { » au modèle matrice (backport phase 2)

**Date :** 2026-06-29
**Kind :** Feat
**Température :** molle (modèle validé sur LibreOffice/VSCode ; refonte d'une feature existante)
**Statut :** acté
**Supersedes :** la sous-décision « systèmes incrémentaux » de [2026-06-10-Feat-multiline-chain-eqarr-architecture](2026-06-10-Feat-multiline-chain-eqarr-architecture.md) (§5 + A3 ; **les chaînes restent incrémentales**, inchangées)
**Lié à :** [2026-06-26-Feat-systems-matrix-model-libreoffice-vscode](2026-06-26-Feat-systems-matrix-model-libreoffice-vscode.md) (le modèle backporté)

## Citation acté

> « pour moi c'est concluant, on va backport sur Word maintenant, les systèmes
> doivent être refais dans word. » — utilisateur, 2026-06-29

## Contexte

Le modèle **matrice** des systèmes d'équations (zone plate : accolade `{` ouvreuse
n'importe où + préfixe analysé par le moteur + lignes séparées par `;`/Maj+Entrée,
composé en **un coup**) est livré et validé sur LibreOffice + VSCode (ADR 2026-06-26).
Word, lui, garde le modèle **incrémental** d'origine (`CommitSystemLine` create-or-extend,
`{ ` pré-placé à chaque Entrée, probe du bloc au-dessus, re-génération). L'utilisateur
juge le modèle matrice concluant → on l'adopte aussi dans Word.

**Découverte clé** : Maj+Entrée Word = `\v` (vertical tab) **dans le paragraphe** (pas
un nouveau ¶) → un système multi-ligne = **UN paragraphe**, déjà lu en entier par
`WordContextReader.ReadCurrentParagraph` (`\v` inclus dans `Range.Text`). Pas de lecture
multi-paragraphe à ajouter.

## Décision

### 1. Composition pure (réutilise l'OMML existant + miroir du Rust)

- `RelationLineDetector.FindUnclosedBrace` (accolade `{` non fermée n'importe où) +
  `SplitTrailingRelation` (relation finale du préfixe, `f(x) =` → `f(x)`,`=`) — port C#
  fidèle de `rust/mc-engine/src/chain.rs`.
- `ChainComposer.ComposeSystem(prefixLatex, latexLines)` : greffe le **préfixe**
  (`LatexToOmml.Convert`) AVANT le `<m:d>` existant (begChr `{`, endChr vide) + eqArr ;
  l'ancienne signature `(lines)` = `("", lines)`.
- `ChainComposer.ComposeSystemLatex(prefixLatex, latexLines)` : `<préfixe> \left\{
  \begin{aligned}…\right.` pour l'aperçu WpfMath de la popup.

### 2. Flux Word en UN coup (remplace l'incrémental)

`ConversionController` : sur le ¶ courant (lu avec `\v`), si `FindUnclosedBrace ≥ 0` →
système. Préfixe + lignes (split `;`/`\v`) analysés (chaque ligne via `ForestEngine`),
**UN candidat** = le bloc composé (popup), recomposé live. Commit → nouveau
`ChainController.CommitSystem` : insertion **unique** via `OMathInserter.InsertBlock`
(réutilise `ReplaceStart` + `ZoneCleaner` + walker + source-map `BlockTypes.System`),
zone = ¶ entier. Retraits : routage `_pendingSystemOpener`→`CommitSystemLine`, pré-placement
M4 `{ ` à l'Entrée (systèmes), create-or-extend. **Chaînes inchangées.**

### 3. Livraison phasée, testée

Phase A pure (xUnit, vérifiée avant Phase B) → Phase B interop Word (**testée par
l'utilisateur** : je n'exécute pas Word ici). Une modif à la fois (règles dures
`feedback-word-api-workflow`). Pipeline 2026-06-12 (pas d'anchor CC, walker, hash-source-map).

## Tradeoff & alternatives écartées

- **Garder l'incrémental Word** : diverge des hôtes phase 2 (3 modèles à maintenir),
  et l'incrémental est plus fragile (relecture + reprobe + re-génération à chaque ligne).
- **Refondre AUSSI les chaînes en matrice** : non demandé ; les chaînes incrémentales
  marchent et l'édition ligne-à-ligne y est naturelle. Hors périmètre.
- **Lecture multi-paragraphe** : inutile (Maj+Entrée = `\v` intra-¶).

## Conséquences

- **Code (Phase A, pur)** : `Host/Blocks/RelationLineDetector.cs`, `Host/Blocks/ChainComposer.cs`
  + tests `tests/MathCursor.Tests/Host/Blocks/*`.
- **Code (Phase B, interop)** : `Host/ConversionController.cs`, `Host/Blocks/ChainController.cs`.
- **Réutilise** : `LatexToOmml`, `LatexTopLevelSplit`, `RelationMarkers`, `OMathInserter.InsertBlock`,
  `ZoneCleaner`, `ChainController.ReplaceStart`, `SourceMapStore`.
- **Risque** : WpfMath pourrait ne pas rendre `\begin{aligned}` (aperçu popup) → repli à tester.
- **Tests** : ChainComposer/RelationLineDetector xUnit ; Word manuel (utilisateur). Moteur/conformance inchangés.

## Validation post-fix

Phase A : `dotnet test adapter-vsto/tests/MathCursor.Tests`. Phase B (utilisateur, build VSTO) :
`{ 2x+y=5 ; x-y=1`, `f(x) = {2x;x`, Maj+Entrée pour les lignes → bloc système accolade en un coup ;
Ctrl+Z = un pas ; revert OK.
