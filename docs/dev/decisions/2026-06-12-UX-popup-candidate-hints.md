# UX — Badges discrets dans la popup quand l'aperçu ne distingue pas (matrice ligne ≈ tuple)

**Date :** 2026-06-12
**Kind :** UX
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-12-Feat-comma-tuples-bare-lists-repere.md](2026-06-12-Feat-comma-tuples-bare-lists-repere.md) (le tuple a créé l'ambiguïté visuelle)

## Citation acté

> « j'ai un soucis de rendu , matrice et tuple se rendent exactement pareil donc bof dans la popup ? qu'est ce qu'on pourrait faire ? » — utilisateur, 2026-06-12. Choix : « Badge discret par candidat » (vs retrait de la matrice ligne à la virgule, écarté).

## Contexte

L'aperçu popup d'une matrice LIGNE passe par `WpfMathAdapter.StackrelDelim` qui remplace les `&` par `\quad` (WpfMath 2.1 n'a pas de `\matrix` aligné) : `(1 2)` avec espaces larges — quasi indiscernable du tuple `(1,2)` à la taille de la popup. Dans Word, la matrice est pourtant une vraie grille OMML : l'ambiguïté n'existe QUE dans l'aperçu.

## Décision

Étiquette grise discrète à droite du rendu, déduite du LaTeX du candidat (`CandidateHints.GetHint`, pure compute, compilé aussi par les tests) :
- `pmatrix/bmatrix/vmatrix` avec `\\` et `&` → « matrice » ; `\\` seul → « colonne » ; sinon → « matrice ligne » ;
- tuples et expressions ordinaires → **aucun badge** (les virgules se lisent toutes seules).

Information sans rien retirer : tous les candidats restent accessibles. Alternative écartée (choix user) : ne plus proposer la matrice ligne à la virgule — perdait un accès clavier.

## Conséquences

- **Code** : `UI/CandidateHints.cs` (nouveau, déclaré dans les 2 csproj), `SuggestionPopupWindow.BuildRows` (colonne badge dans la grille de rang). Moteur : zéro changement.
- **Tests** : `CandidateHintsTests` (11 cas — matrices étiquetées, tuples/repères/expressions sans badge). Adapter 277/277, build VSTO vérifié.

## Validation post-fix

Word, taper `(1, 2)` : la popup montre `(1, 2)` sans badge (présélection), `(1 2)` étiqueté « matrice ligne », la pile étiquetée « colonne ».
