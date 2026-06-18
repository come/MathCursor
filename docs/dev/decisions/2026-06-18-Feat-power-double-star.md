# Feat — `**` comme opérateur puissance (alias de `^`)

**Date :** 2026-06-18
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** `docs/dev/engine-backlog.md` (item #1), [2026-06-16-Feat-portable-engine-universal-vocab.md](2026-06-16-Feat-portable-engine-universal-vocab.md) (table `symbols.json`)

## Citation acté

> « 1 deja » (= on attaque le #1 du backlog) — utilisateur, 2026-06-18
> (item backlog : « `**` pour la puissance — accepter `**` comme opérateur
> d'exposant, ex. `x**2` → `x^2`, convention répandue (Python…), naturelle au
> clavier sans AltGr »)

## Contexte

`^` est l'opérateur puissance, mais peu accessible au clavier (AltGr + espace
mort sur AZERTY). `**` est une convention répandue (Python, etc.) et directe à
taper. On veut l'accepter comme **synonyme** de `^`.

## Décision

Ajouter `"**": { "sameAs": "^" }` dans `data/engine/symbols.json`. Purement
data-driven, **zéro code** : le lexer fait du plus-long-match (longueurs 3, 2,
puis 1), donc `**` (len 2) est reconnu **avant** `*` (len 1) et hérite de tout
le comportement de `^` (infixe, `sup`, render `{0}^{{1}}`). Bénéficie aux deux
implémentations (C# et futur port Python), le contrat restant les fixtures.

`x**2` → `x^{2}`, `a**b` → `a^{b}`, `(x+1)**2` → `(x+1)^{2}`.

## Tradeoff & alternatives écartées

- **Traiter `**` dans le lexer (code)** : inutile — le plus-long-match existant
  suffit, et le code resterait « générique sans nommer d'opérateur » (principe
  du moteur). Le `sameAs` est la voie déclarative.
- **Risque de collision avec `*` (mult) et `(R*)` (`\ast`)** : vérifié par spike
  — `a*b` reste `a\times b`, `(R*)` reste `R^{\ast}` (le PostSign `\ast` est sur
  le `*` len 1 uniquement ; `**` len 2 passe avant et ne l'atteint pas).

## Conséquences

- **Données (L1)** : 1 ligne dans `symbols.json`. Aucun code moteur touché.
- **Tests** : +3 fixtures (`x**2`, `a**b`, `(x+1)**2`) → corpus **444** ;
  `Assert.Equal` bumpé. 21 tests verts, zéro régression.
- **API publique** : inchangée.

## Validation post-fix

`ForestEngine.Analyze("x**2")` → `x^{2}` ; `a*b` → `a\times b` (non régressé) ;
`(R*)` → `R^{\ast}` (non régressé). Corpus 444/444 vert.
