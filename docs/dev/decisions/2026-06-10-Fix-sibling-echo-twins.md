# Fix — SiblingEcho régime « jumelles » : symétrie à opérateur près (« 1/x+x2 * 1/x-x2 »)

**Date :** 2026-06-10
**Kind :** Fix
**Température :** molle
**Statut :** retracté
**Superseded by :** [2026-06-10-Feat-slurp-coherence.md](2026-06-10-Feat-slurp-coherence.md)
**Supersedes :** —
**Lié à :** [2026-06-10-Fix-sibling-echo-symmetry.md](2026-06-10-Fix-sibling-echo-symmetry.md) (régime « prolongement » du même mécanisme)

## Citation acté

> « cool mais on a perdu ma symetrie :'( .. tu peux checker et ajouter en fixture » (capture : `1/x+x2 * 1/x-x2` proposait deux hybrides et UNE seule paire symétrique) — utilisateur, 2026-06-10

## Contexte

Les opérandes `1/x+x2` et `1/x-x2` ont des signatures de MÊME longueur mais
différentes (`a/a+a^a` vs `a/a-a^a`) : ni la cohérence exacte
(`GlobalCoherence`) ni l'écho « préfixe » ne les couplaient → la popup
montrait les hybrides et pas la paire groupée
`\frac{1}{x+x^{2}}\times\frac{1}{x-x^{2}}`. Deuxième étage : même couplée,
la paire groupée paie 2× l'inversion → un bonus FIXE la laissait pile au
bord de la fenêtre (gap strict).

## Décision

`SiblingEcho` gagne un second régime, JUMELLES : frères de même longueur de
signature, même MASQUE d'atomes (opérateurs anonymisés : `a/a?a^a`), même
NIVEAU de tête (`RootKey` : STRONG / STRONG-implicite / WEAK — « + » et
« − » sont la même forme, « / » et « · » non) → **remise du dupliqué**
`−min(Base(a), Base(b))`, exactement l'esprit de la remise GlobalCoherence
pour signatures identiques (un bonus fixe ne réduit pas l'écart de base).
Têtes divergentes → malus +1 (écarte les hybrides).

Effet : popup `[(\frac{1}{x}+x^{2})\times(\frac{1}{x}-x^{2}),
\frac{1}{x+x^{2}}\times\frac{1}{x-x^{2}}]` — les deux paires symétriques,
zéro hybride.

## Tradeoff & alternatives écartées

- **Bonus fixe (comme le régime préfixe)** : insuffisant mathématiquement —
  l'écart de base (2× inversion) reste égal au PopupGap strict.
- **Comparaison des têtes par symbole exact** : « + » vs « − » auraient été
  jugés divergents → la paire symétrique naturelle pénalisée ; le niveau de
  structure (classe + implicite) est la bonne granularité.

## Conséquences

- **Code touché** : `Score.cs` (`SigMask`, `RootKey`, branche jumelles).
- **Fixtures** : +1 (le cas de la capture), corpus à 362.
  **Zéro fixture existante modifiée** (vérifié sur les 361).
- Sur-couplages accidentels (masques égaux par hasard) : bénins — un malus
  uniforme sur toutes les combinaisons ne change pas le classement.

## Validation post-fix

- Suites complètes vertes ; gardes re-sondées : `1/2x + 1/2x2`, `lim`,
  `f :R2->R`, `x+1 = x-1` inchangés.
- Word : `1/x+x2 * 1/x-x2` → popup avec les deux paires symétriques.
