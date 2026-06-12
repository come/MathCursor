# Feat — Enchaînements de virgules : tuples littéraux, listes nues, repère (O, ⃗ı, ⃗ȷ) en auto

**Date :** 2026-06-12
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** — (amende la **Limit** « liste à virgules top-level reportée » de [2026-06-12-Feat-dots-ellipsis-atoms.md](2026-06-12-Feat-dots-ellipsis-atoms.md))
**Lié à :** [2026-06-10-Feat-geo-point-pairs.md](2026-06-10-Feat-geo-point-pairs.md) (précédent : lecture littérale À CÔTÉ du groupement, « à coût égal les parenthèses tapées priment »)

## Citation acté

> « et je veux bien qu'on regarde les enchainement de , j'essaie d'integrer le repere (O, veci, vecj) mais ca ne veut pas » — utilisateur, 2026-06-12
> Choix au cadrage : tuple littéral partout (parenthèses ET top-level), repère → AUTO, tuple EN TÊTE aux virgules. Plan approuvé.

## Contexte

`(O, veci, vecj)` n'avait que des lectures matrices (popup ligne/colonne) ; `(O, vec i, vec j)` espacé partait en matrice absurde à trous EN AUTO ; les virgules nues (`x, y`, `u_1, u_2`, `1, 2, ..., n`) étaient des erreurs moteur.

**Tuple vs matrice** (règle produit) : la **matrice** est l'objet OMML en grille SANS virgules (`(1 2)`, colonne empilée) — l'objet scolaire, voie d'accès `;` (`(1; 2)` → colonne AUTO, inchangé). Le **tuple** est du math PLAT, parenthèses et virgules conservées — repère `(O, ⃗ı, ⃗ȷ)`, couple `A(1, 2)`, appel `f(x, y)`. La virgule penche tuple (présélection), les matrices restent en alternatives popup.

## Décision

1. **Lecture « tuple » parenthésée** (Parser, branche lparen) : intérieur à virgules profondeur 0 → `Node Type="tuple"` (cartésien des segments, calque de la branche set, parts `Grouped`), rendu `(e1,e2,…)`. `_onGroup` skippé sur ces intérieurs (sinon doublon sans parenthèses via la liste nue). **Tuple en tête par ordre d'émission** (geo → tuple → matrices) + égalité de coût + tri stable — zéro biais de Score.
2. **Repère AUTO par suppression des matrices** : si les segments matchent le pattern (3-4 segs ; seg₀ = 1 atome lettre majuscule ; segs suivants = `[prefix Tight, atome]`), `Matrices` n'est pas appelé → le tuple est seul dans la fenêtre → auto, à toute profondeur (`R = (O, veci, vecj)` compris). Conséquence assumée : `(O, hat i, hat j)` passe aussi (le Parser raisonne par features, pas par opérateurs nommés).
3. **Liste « nue » top-level** (ParseSpan) : span à virgules profondeur 0 → `Node Type="list"` existant (rendu join `,`), avec **garde anti-prose** : aucun segment ne doit être un unique atome-MOT (≥2 lettres) — `oui, non` reste une erreur, `x, y` / `u_1, u_2, ..., u_n` passent. Garde non appliquée au tuple (parenthèses = intention explicite).

## Tradeoff & alternatives écartées

- **Bonus de score −2 pour le repère** : la fenêtre popup (strictement <) tombait PILE à la frontière — fragile, et un bonus nommé viole la doctrine features-only de Score. Écarté par chiffrage.
- **Décision spéciale dans `Finish` (type PairSkeletons)** : ne voit que la racine — échoue dès que le repère est imbriqué. Écarté.
- **Liste nue sans garde** : la prose à virgules (« oui, non ») devenait parsable → popups sur fragments NER. Garde mot-littéral retenue ; collatéral assumé : `AB, CD` nu reste une erreur.

## Conséquences

- **Code** : `Parser.cs` (tuple + garde repère + liste nue, ~40 lignes), `LatexRenderer.cs` (case tuple), `Node.cs` (commentaire). **Score.cs : zéro changement** (chiffré : tuple = 0 + enfants ; types inconnus neutres).
- **Interactions vérifiées** : `forall x,y in R`/`exists x,y` (même chaîne → dédoublonnage, fixtures identiques), `(a b c, d e f)` et `(a,b,c;d,e,f)` (segs imparsables → pas de tuple), décimales `4,5`/`pgcd(12,18)`/`[0,5;1,5]` (lexées avant le sep — corollaire : le tuple numérique se tape AVEC espace, `(1,2)` collé reste la décimale `1{,}2`), `(1; 2)` colonne auto inchangé.
- **`1, 2, ..., n` devient AUTO** `1,2,\ldots ,n` — la fixture-limite de l'ADR dots (même jour) se retourne ; sa section Limit pointe ici.
- **Fixtures** : 2 régénérées (`(a,b,c)` → popup 3 candidats tuple-first ; `1, 2, ..., n`), ~13 nouvelles → corpus ~415. Tuto : vert sans retouche (consignes à séparateurs toutes auto-inchangées).
- **Aval** : rien — LatexToOmml passe `(`/`,`/`)` en runs texte, WpfMath rend ce LaTeX banal (audits en barrière).

## Validation post-fix

1. Probes : tableau d'interactions + `(x + 1, y)` → `(x+1,y)` (piège du skip `_onGroup`) + `R = (O, veci, vecj)` imbriqué.
2. Suite moteur avant retouche fixtures : seules `(a,b,c)` et `1, 2, ..., n` cassent. Puis corpus étendu vert, sérialisation + adapter verts.
3. Word : `(O, veci, vecj)` → conversion directe ; `(1, 2)` → popup tuple en tête ; `(1; 2)` → colonne auto.
