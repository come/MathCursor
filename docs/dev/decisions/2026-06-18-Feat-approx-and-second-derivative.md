# Feat — Saisie `approx`/≈ complétée + dérivée seconde confirmée

**Date :** 2026-06-18
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** `docs/dev/engine-backlog.md` (items #3 et #4)

## Citation acté

> « 3 et 4 stp » — utilisateur, 2026-06-18
> (#3 : faire fonctionner `≈` via mot-clé `approx`/`environ` et/ou Unicode `≈` ;
> #4 : vérifier la dérivée seconde + fixture de non-régression)

## Contexte

`approx` rendait déjà `\approx` (vocab + couverture `LatexToOmml`/`LatexToUnicodeMath`),
mais **deux entrées manquaient** : le caractère Unicode `≈` collé, et un alias FR
naturel. Côté dérivée seconde, `f''(x)` fonctionnait (postfixe `'`) ; #4 = le
**confirmer** et **verrouiller** par fixture.

## Décision

### #3 — Saisie `approx`/≈ (data only)
- `data/engine/symbols.json` : `"≈": { "sameAs": "approx" }` → le caractère
  Unicode `≈` (U+2248) collé est reconnu.
- `data/engine/cultures.json` (alias `fr`) : `"environ": "approx"`,
  `"environegal": "approx"` → mots-clés FR naturels.
  *(Les alias sont mono-mot : « environ egal » en deux mots ne matcherait pas ;
  d'où `environegal` collé.)*
- `approx` (mot-clé), `\approx`/≈ (rendu OMML) : **déjà en place**, inchangés.

### #4 — Dérivée seconde (confirmation + verrou)
Aucun changement de comportement : `f''`, `f''(x)`, `u''(t)`, `f''(x)=0`
fonctionnent déjà via le postfixe `'`. On ajoute une fixture `u''(t)` de
non-régression. La notation Leibniz `\frac{d^2 f}{dx^2}` reste hors périmètre
(pas de raccourci sténo dédié).

## Tradeoff & alternatives écartées

- **Alias `environ egal` en deux mots** : impossible via le mécanisme d'alias
  (mono-token) → `environegal` collé + `environ` seul.
- **Nouveau code pour `≈`/dérivée seconde** : inutile, tout est data-driven /
  déjà supporté.

## Conséquences

- **Données (L1)** : 1 ligne `symbols.json` (`≈`) + 1 ligne d'alias `cultures.json`.
  Aucun code moteur ni sérialisation touché.
- **Tests** : +3 fixtures (`a ≈ b`, `a environ b`, `u''(t)`) → corpus **447**,
  `Assert.Equal` bumpé. 21 tests verts, zéro régression.
- **API publique** : inchangée.

## Validation post-fix

`Analyze("a ≈ b")` / `("a environ b")` / `("a environegal b")` → `a\approx b` ;
`Analyze("u''(t)")` → `u''(t)` ; `("f''(x)=0")` → `f''(x)=0`. Corpus 447/447 vert.
