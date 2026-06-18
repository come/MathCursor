# Fix — Robustesse entrée : fractions vulgaires Word + factorielle au Ctrl+Espace

**Date :** 2026-06-18
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-16-Feat-portable-engine-universal-vocab.md](2026-06-16-Feat-portable-engine-universal-vocab.md) (table `symbols.json`), [2026-05-25-Refactor-chantier1-data-driven-fr-keywords.md](2026-05-25-Refactor-chantier1-data-driven-fr-keywords.md) (futur `LocaleVocabulary.SpanDelimiters`)

## Citation acté

> « ok du coup ! » — utilisateur, 2026-06-18

(au terme du diagnostic conjoint : « le factoriel ne passe plus », « c'est l'auto
correct de word qui rajoute un espace », « c'est le ctrl+espace sur le ! qui
empeche la popup. possible ? » — utilisateur, 2026-06-18)

## Contexte

Deux bugs distincts remontés en test, tous deux des interactions « Word
massacre l'entrée math » :

1. **Fractions vulgaires** : l'AutoFormat de Word remplace `1/2` → le caractère
   unique `½` (U+00BD) en cours de frappe. Le lexer ne connaissait pas `½` →
   `caractère inattendu: ½` (vu dans `mathcursor.log`) → la conversion échoue.
   Le `AutocorrectNormalizer` ne peut pas corriger ça : il a un invariant dur
   **1 char → 1 char** (offsets curseur/zones), or `½`→`1/2` fait 1→3 chars.

2. **Factorielle au Ctrl+Espace** : `!` figurait dans `SpanDelimiters`
   (`ConversionController`). Caret juste après `n!` → le scan de span bute sur
   `!` (pris pour une fin de phrase) → span vide → `return` → **aucune popup**.
   Le `!` est un opérateur postfixe (`symbols.json`), pas une ponctuation.

Diagnostic appuyé sur les faits : moteur (`Analyze("n!")`, `Analyze("F'(x)=n!")`)
et sérialisation OMML déjà corrects ; `½` faithful à `1/2` dans tous les
contextes (`½*g` ≡ `1/2*g`, `2½`→`2\frac{1}{2}`).

## Décision

Approche **ceinture + bretelles** validée par l'utilisateur :

### Fractions vulgaires
- **(a) Couper à la source** : désactiver `Application.Options.`
  `AutoFormatAsYouTypeReplaceFractions` au démarrage (à côté du
  `DisableOMathAutoCorrectOutsideMath` existant) → la frappe reste `1/2`, propre.
- **(b) Rendre le moteur tolérant** : ajouter `¼ ½ ¾ ⅐ ⅑ ⅒ ⅓ ⅔ ⅕ ⅖ ⅗ ⅘ ⅙ ⅚ ⅛ ⅜
  ⅝ ⅞` comme **atomes** dans `data/engine/symbols.json`, `lower`/`upper` =
  `\frac{p}{q}` → robuste même au copier-coller / doc existante / autre locale,
  et offset-safe (1 char en entrée). Verrouillé par fixtures (contrat C#/Python).

### Factorielle
- **Retirer `'!'` de `SpanDelimiters`** (`ConversionController`). Sûr :
  auto-détection inchangée (passe par le NER, pas par ce set) ; `=`/`?`/`.`
  restent délimiteurs ; `a!=b` reste borné par `=` comme avant. Seul effet de
  bord : un Ctrl+Espace explicite sur « Bravo! » inclurait le `!` — négligeable
  (geste volontaire).

## Tradeoff & alternatives écartées

- **Couper l'autocorrect Word seulement** (sans lexer) : écarté — ne protège ni
  du copier-coller d'un `½` ni des docs qui en contiennent déjà.
- **Rendre le moteur tolérant seulement** (sans couper l'autocorrect) : écarté —
  Word continuerait d'afficher `½` pendant la frappe (moins propre visuellement).
- **Normaliser `½`→`1/2` dans `AutocorrectNormalizer`** : impossible sans casser
  l'invariant 1 char → 1 char (désync des offsets Word ↔ texte interne).
- **Détecter `!`-factorielle contextuellement** (postfixe uniquement si précédé
  d'un atome) dans `SpanDelimiters` : sur-ingénierie ; le retrait simple suffit
  car le set ne sert qu'au Ctrl+Espace explicite.

## Conséquences

- **Données (L1, universelles)** : `data/engine/symbols.json` — 18 atomes
  fraction vulgaire ajoutés. Bénéficie C# **et** futur port Python.
- **Moteur (L1)** : aucun changement de code — purement data-driven (le lexer
  matche les atomes au plus-long-match existant).
- **Tests moteur** : `engine/tests/.../fixtures.json` — fixtures `½ ¼ ¾ ⅓ 2½ ½x`
  ajoutées + `Assert.Equal` du compte bumpé. Contrat rejoué par les 3 pipelines
  (moteur, OMML, popup) et les mutations.
- **Adapter (L3)** :
  - `ThisAddIn.cs` : `DisableAutoFormatFractions()` au `Startup`.
  - `ConversionController.cs` : `'!'` retiré de `SpanDelimiters` + test
    `ComputeSpan` sur `n!` / `soit n! le terme`.
- **API publique** : inchangée.
- **Dette notée** : si les délimiteurs passent un jour data-driven
  (`LocaleVocabulary.SpanDelimiters`, ADR 2026-05-25 non câblé), reporter
  l'exclusion du `!` dans la donnée.

## Validation post-fix

1. Moteur : `½`→`\frac{1}{2}`, `2½`→`2\frac{1}{2}`, `½x`→`\frac{1}{2}x`,
   `x=½`→`x=\frac{1}{2}` ; 434+ fixtures vertes (zéro régression).
2. Sérialisation : suite verte (`!` plain et `\frac` déjà couverts).
3. Adapter : test `ComputeSpan` → `n!` capté ; suite adapter verte ; build VSTO OK.
4. Manuel Word : taper `n!` + Ctrl+Espace → popup `n!`. Taper `1/2` → reste
   `1/2` (plus de substitution `½`), conversion OK.
