# Fix — Score : les décorations tight ne déclenchent pas la pénalité de nesting

**Date :** 2026-06-10
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Fix-sibling-echo-symmetry.md](2026-06-10-Fix-sibling-echo-symmetry.md) (même couche : Score), [2026-06-10-Feat-split-distance-cost-vec.md](2026-06-10-Feat-split-distance-cost-vec.md) (conj splittable)

## Citation acté

> « abs z-conjz ne me propose pas le abs(z-conjz) comme le ferait cos ? y'a une raison ca devrait etre gratuit? » — utilisateur, 2026-06-10

## Contexte

`abs z+1` proposait bien `\left|z+1\right|` (comme `cos x+1` → `\cos(x+1)`),
mais `abs z-conjz` ne proposait PAS `\left|z-\bar{z}\right|`. Le coupable
n'était pas `abs` : la lecture large payait inversion (+1, normal, comme cos)
PLUS la pénalité de nesting « strong dans strong » (+1) à cause du `\bar{z}`
imbriqué → coût 2, juste hors de la fenêtre popup (< meilleur + 2).
`cos x-conjz` avait exactement le même trou.

## Décision

`Score.NestStrong` exclut les opérateurs **tight** (décorations `bar`/`vec`/
`hat`, `partial`/`nabla` : opérande d'un seul morceau) : `\bar{z}` est une
lettre décorée, visuellement atomique — pas une imbrication. Une garde d'une
ligne : `Decl(n) is not { Tight: true }`.

Effet : `abs z-conjz` → popup `[\left|z\right|-\bar{z}, \left|z-\bar{z}\right|]`,
même forme que `cos`. Idem `cos x-conjz`, `module z-conjz`.

## Tradeoff & alternatives écartées

- **Élargir PopupGap** : global, bruit partout — le problème est local à la
  sémantique « décoration ≈ atome ».
- **Coût spécial pour abs** : abs n'était pas en cause (`cos x-conjz` avait
  le même trou) ; la règle par feature `Tight` reste générique (aucun
  opérateur nommé).

## Conséquences

- **Code touché** : `Score.cs`, une garde dans `NestStrong`.
- **Fixtures** : +2 (`abs z-conjz`, `cos x-conjz`), corpus à 332.
  **Zéro fixture existante modifiée** (330/330 vertes avant gel).
- **Divergence JS assumée** (le JS figé garde l'ancien classement).

## Validation post-fix

- Suites moteur/serialization/adapter vertes, mutations comprises.
- Word : `abs z-conjz` → popup avec `\left|z-\bar{z}\right|` en 2ᵉ choix.
