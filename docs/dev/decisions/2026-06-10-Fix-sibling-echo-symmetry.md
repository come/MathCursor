# Fix — Score : écho de symétrie entre frères à signatures prolongées (« 1/2x + 1/2x2 »)

**Date :** 2026-06-10
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-split-distance-cost-vec.md](2026-06-10-Feat-split-distance-cost-vec.md) (philosophie : le coût classe, la popup arbitre)

## Citation acté

> « 1/2x + 1/2x2 ce cas là est super foireux.. la symetrie n'a pas l'air de fonctionner » puis « ok ca marche bien ! » — utilisateur, 2026-06-10

## Contexte

Top observé : `\frac{1}{2x}+\frac{1}{2}x^2` — hybride. Cause : chaque terme est
classé isolément et leurs optima divergent (`\frac{1}{2x}` coût 0 à gauche ;
à droite `\frac{1}{2x^2}` paie nesting fraction⊃exposant + implicite structuré
= 2, contre 1 pour `\frac{1}{2}x^2`). Les mécanismes de symétrie existants
(`GlobalCoherence`, `ParentRefund`) ne couplent que des signatures lexicales
IDENTIQUES (`1/2x + 1/2y` était bien symétrique) ; ici `a/aa` vs `a/aa^a` —
zéro couplage, et la lecture symétrique était même coupée par MaxShow=5.

## Décision

Nouvelle composante de coût `Score.SiblingEcho` (~20 lignes, aucun opérateur
nommé) : entre FRÈRES non-atomes dont la signature de l'un PROLONGE celle de
l'autre (préfixe strict — « la même tournure, étendue par l'utilisateur »),
même forme de tête (`/` et `/`) → bonus −1, formes divergentes (`/` et `·`)
→ malus +1. Analogue « à prolongement » de `GlobalCoherence`.

Effet : top = `\frac{1}{2x}+\frac{1}{2x^{2}}`, l'autre lecture symétrique
`\frac{1}{2}x+\frac{1}{2}x^{2}` dans la fenêtre popup, hybride éjecté du haut.

## Tradeoff & alternatives écartées

- **Baisser les coûts de base du dénominateur structuré** : changerait le
  classement de `1/2x2` SEUL (top `\frac{1}{2}x^2`, défendable en typing
  flow) ; le problème n'existe qu'en contexte de frères — le fix vit donc
  dans le couplage, pas dans les coûts de base.
- **Cohérence par squelette de forme (profondeur bornée)** : plus générale
  mais clé d'appariement floue ; la relation de préfixe de signature est
  exacte, locale et déjà dans le langage du moteur (Sig).

## Conséquences

- **Code touché** : `engine/src/MathCursor.Engine/Score.cs` uniquement
  (fonction + 1 terme dans `Cost`). Coût total désormais possiblement négatif
  (les refunds existaient déjà) — le classement est relatif, sans impact.
- **Fixtures** : 5 ajoutées (cas + contrôles `1/2x + 1/2y`, `2x + 2x2`,
  `x + 1/2`, `1/2 + 1/2x`), corpus à 330. **Zéro fixture existante modifiée**
  (vérifié : 325/325 vertes avant gel).
- **Divergence JS assumée** (le JS figé garde l'ancien classement).

## Validation post-fix

- Suites moteur/serialization/adapter vertes, mutations corpus comprises.
- Word : `1/2x + 1/2x2` → popup avec les deux lectures symétriques en tête.
