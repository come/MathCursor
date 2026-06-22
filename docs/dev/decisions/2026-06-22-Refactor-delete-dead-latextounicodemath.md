# Refactor — Suppression du code mort `LatexToUnicodeMath`

**Date :** 2026-06-22
**Kind :** Refactor
**Température :** molle
**Statut :** acté
**Supersedes :** — (accomplit la promesse de [2026-06-02-Feat-omml-insertion.md](2026-06-02-Feat-omml-insertion.md) : « rend `LatexToUnicodeMath` obsolète, suppression dans un fix suivant »)
**Lié à :** [2026-06-02-Feat-omml-insertion.md](2026-06-02-Feat-omml-insertion.md), [2026-06-10-Refactor-phase2-adapter-orchestration-rewrite.md](2026-06-10-Refactor-phase2-adapter-orchestration-rewrite.md)

## Citation acté

> [audit 2026-06-22 — #3] « à quel endroit c'est utilisé le 3 ? je crois qu'on l'utilise plus » puis « ok go » — utilisateur, 2026-06-22

## Contexte

L'audit avait classé `serialization/.../LatexToUnicodeMath.cs` (568 lignes, zéro test,
6 warnings MC0001) comme « trou de couverture critique ». Vérification : **la classe
n'est appelée nulle part.** Les seules occurrences hors sa définition sont un
`<see cref>` de doc (`LatexToOmml.cs:12`) et trois commentaires (`WpfMathAdapter.cs:225/242`,
`UndoPoc.cs:21`). Zéro `LatexToUnicodeMath.Convert(...)` dans le code.

C'était l'ANCIEN chemin de sérialisation (UnicodeMath linéaire re-parsé par Word au
`OMaths.BuildUp`, avec ses bugs de précédence — `lim` happant le numérateur, `\in`
avant `\int`…). Il a été remplacé par l'**insertion OMML** (`LatexToOmml` →
`Range.InsertXML`, ADR 2026-06-02), qui a explicitement annoncé sa suppression. Gardé
un temps en « early-bail garde-fou », son appel a été retiré au rewrite phase2
(2026-06-10). La suppression promise n'avait jamais été faite — c'est du code mort
depuis.

## Décision

Supprimer `LatexToUnicodeMath.cs`. Le chemin vivant unique est `LatexToOmml`
(insertion OMML), couvert par `OmmlCoverageTests` (456 fixtures → OMML).

Nettoyages associés (références désormais pendantes) :
- `LatexToOmml.cs` : le `<see cref="LatexToUnicodeMath"/>` devient prose historique (sinon CS1574).
- `WpfMathAdapter.cs` : deux commentaires « LatexToUnicodeMath → BuildUp » → « LatexToOmml → OMML ».

## Tradeoff & alternatives écartées

- **Écrire les tests manquants** (plan #3 initial) : aurait figé 568 lignes de code mort. Inutile.
- **Garder en garde-fou** : il n'est même plus appelé en early-bail ; aucune valeur.
- **Garder « au cas où » le portage Office.js** : le pivot reste le LaTeX + `LatexToOmml` ; un éventuel besoin UnicodeMath se réécrira proprement depuis l'AST (cf. plan `IOutputSerializer`), pas en ressuscitant ce fichier Regex.

## Conséquences

- **Code** : suppression `serialization/src/MathCursor.Serialization/LatexToUnicodeMath.cs` (le projet `MathCursor.Serialization` reste — il porte `LatexToOmml`). 2 commentaires rafraîchis. Disparition de 6 warnings MC0001.
- **Docs** : `ROADMAP.md` — l'item « Refacto `LatexToUnicodeMath` Regex→parser » devient caduc. `cartography.md` à réconcilier (étape 3 `IOutputSerializer` sur `LatexToUnicodeMath` n'a plus d'objet) — cf. angle mort i18n/doc de l'audit.
- **Tests** : aucun ne référençait la classe → suite inchangée, gate vert attendu.
