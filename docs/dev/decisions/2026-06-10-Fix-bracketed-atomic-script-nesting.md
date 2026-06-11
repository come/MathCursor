# Fix — Score : script atomique aplati DANS un conteneur bracketed (« 1/x+x2 »)

**Date :** 2026-06-10
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Fix-tight-decorations-nesting.md](2026-06-10-Fix-tight-decorations-nesting.md) (même famille : qu'est-ce qu'une imbrication visuelle ?), [2026-06-10-UX-sup-only-juxtaposition.md](2026-06-10-UX-sup-only-juxtaposition.md)

## Citation acté

> « 1/x+x2 devrait montrer 1/(x+x²) » puis « oui 1 et 2 » (chantier 2 :
> calibration nesting des scripts, séparé du rollback sup-only) — utilisateur, 2026-06-10

## Contexte

`\frac{1}{x+x^{2}}` était hors fenêtre popup : inversion (+1) + pénalité de
nesting fraction⊃exposant (+1) = 2. Même cause que `abs z-conjz` (décorations),
mais pour les scripts.

## Décision

**Première tentative (rejetée par la mesure)** : exempter globalement les
scripts atomiques de `NestStrong` — 2 régressions attrapées par le corpus :
`lim x 0+ 1/x = +inf` perdait son AUTO pour la lecture absurde
`\frac{\lim…1}{x}`, et `f :R2->R` gagnait le bruit `f\div R^{2}`.
L'exemption abaissait aussi le coût des MAUVAISES lectures.

**Version retenue (chirurgicale)** : le script à opérandes atomiques (x², u_n)
n'est exempté que DANS un conteneur `Bracketed` (fraction, binôme) — ses
accolades aplatissent visuellement le contenu. Hors conteneur bracketed
(lim, ÷…), il compte toujours comme imbrication. Implémentation : paramètre
`inBrackets` de `ContainsNest`, activé quand le nœud porteur est Bracketed.

Effet : `1/x+x2` → popup `[\frac{1}{x}+x², \frac{1}{x+x^{2}}]` ; `lim` reste
auto ; `f:R2->R` inchangé ; UNE seule fixture régénérée (`1/2x + 1/2x2`,
qui perd un hybride traînard).

## Tradeoff & alternatives écartées

- **Exemption globale des scripts atomiques** : rejetée SUR MESURE (2
  régressions corpus ci-dessus) — leçon : toute exemption de coût doit être
  conditionnée au contexte visuel réel, pas au seul nœud.
- **Élargir PopupGap / baisser l'inversion** : globaux, bruit partout.

## Conséquences

- **Code touché** : `Score.cs` (`AtomicScript`, `ContainsNest(inBrackets)`).
- **Fixtures** : 1 régénérée + 2 ajoutées (`1/x+x2`, `1/2x2` — fige aussi le
  nouvel ordre du solo : lecture dénominateur d'abord, à égalité de coût).
  Corpus à 361.

## Validation post-fix

- Suites complètes vertes, mutations comprises.
- Word : `1/x+x2` → popup avec `\frac{1}{x+x^{2}}` en 2ᵉ choix.
