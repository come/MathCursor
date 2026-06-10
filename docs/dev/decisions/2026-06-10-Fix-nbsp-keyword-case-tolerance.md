# Fix — Lexer : espaces insécables Word (NBSP) + capitale de début de phrase sur mots-clés

**Date :** 2026-06-10
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-culture-scoped-aliases.md](2026-06-10-Feat-culture-scoped-aliases.md) (Canon/alias utilisés par le repli de casse)

## Citation acté

> « on a f :R2->R n'a pas l'air de passer. Par ailleurs, possible d'avoir une recherche dans les keyword avec une majuscule aussi genre Sum devrait passer (Word autocorrect sum en Sum) » — utilisateur, 2026-06-10

## Contexte

Deux casses d'autocorrection Word observées en usage réel (logs du 2026-06-10) :

1. **`f :R2->R` ne passait pas** : la typographie française de Word insère un
   **espace insécable U+00A0** (U+202F en versions récentes) avant `: ; ! ?`.
   Le lexer jetait `caractère inattendu` dessus (log :
   `engine error sur "f :R2->R": caractère inattendu: <U+00A0>`). Le moteur,
   lui, lit parfaitement `f :R2->R` avec un espace normal
   (`f\colon\mathbb{R}^2\to\mathbb{R}`). Le commentaire d'`AutocorrectNormalizer`
   disait « NBSP laissé intact (traité côté Lexer) » — mais le lexer forest ne
   le traitait pas (le JS de référence non plus : le web ne subit pas
   l'autocorrect Word).

2. **`Sum` ne passait pas** : Word capitalise en début de phrase
   (`sum` → `Sum`). Le lexer n'avait de repli minuscule que pour les ATOMES
   (grec/ensembles : `Pi` → `\Pi`). Précédent produit : l'ancien moteur avait
   `CaseToleranceLookup` (Cos→`\cos`, OMEGA→`\Omega`,
   ADR 2026-05-25-Refactor-chantier2).

## Décision

Les deux tolérances vivent dans le **lexer** (robustesse pour tous les hosts,
y compris la démo WASM) :

- **`IsSpace(char)`** : espace, U+00A0, U+202F — utilisé par le scan
  principal, `Spaced`, le check WordSpace et `detachedR` (étoile postfixe).
- **Repli de casse dans `Word()`** : si le lookup exact (canonicalisé) échoue
  ET que le chemin atome-minuscule n'a rien donné, retenter
  `Canon(w.ToLowerInvariant())` pour les mots ≥ 2 lettres, en n'acceptant
  qu'une cible **non-atome**. `Sum`→`sum`, `Cos`→`cos`, `Somme`→`somme`→`sum`
  et `Dans`→`dans`→`in` (le repli passe par les alias de la culture). La
  sémantique de casse des atomes est intacte (`Pi`→`\Pi`, `R`=ensemble,
  `r`=variable) car leur chemin prime. Même repli dans le check « mot connu ? »
  des runs de lettres (cohérence avec la protection anti-découpage ξ).

## Tradeoff & alternatives écartées

- **NBSP normalisé côté adapter (`AutocorrectNormalizer`)** : laisserait la
  démo WASM et tout futur host vulnérables ; un caractère d'espace qui fait
  jeter le lexer est une fragilité moteur, pas un artefact Word.
- **Repli de casse limité au pattern `Xxx` (1re lettre)** : l'ancien moteur
  acceptait aussi OMEGA (tout-caps) ; `ToLowerInvariant` couvre les deux sans
  complexité. Le risque de collision est borné par : ≥ 2 lettres, cible
  non-atome, et seulement quand le lookup exact a échoué.
- **Tables d'alias capitalisés (« Sum »→sum dans les maps)** : duplication
  combinatoire des entrées ; le repli est générique et gratuit.

## Conséquences

- **Code touché** : `engine/src/MathCursor.Engine/Lexer.cs` uniquement
  (`IsSpace` + 4 sites, repli de casse dans `Word()` et le check known).
- **Tests** : cas ciblés ajoutés à `fixtures.json` (politique « tout snapshot
  va aux fixtures », champ optionnel `culture` — NBSP/NNBSP, Sum/Somme/Cos/Dans,
  casse atomes intacte, alias FR majuscule inactif en US) +
  `FixtureToleranceTests.cs` pour les PROPRIÉTÉS (corpus entier muté : fixtures
  à espaces rejouées en NBSP et NNBSP, fixtures commençant par un mot-opérateur
  rejouées avec capitale de début de phrase — résultat identique attendu).
  `InternalsVisibleTo("MathCursor.Engine.Tests")` ajouté au csproj moteur
  pour filtrer par `Vocabulary`/`Canon`. Les 280 fixtures baseline inchangées.
- **Garde-fou unités** : le repli de casse exige `Shape` non-null et non-atome
  → les mots-unités (Shape null) sont exclus (`5 Km` resterait sinon reconnu
  à moitié, avec `\mathrm{Km}` au lieu du mot d'origine).
- **API publique** : aucune.
- **Comportement produit** : un mot-clé capitalisé par Word se comporte comme
  sa version minuscule ; un NBSP se comporte comme un espace (y compris comme
  signal d'étoile postfixe détachée).

## Validation post-fix

- `dotnet test` engine 25/25, serialization 13/13, adapter 186/186.
- Test manuel Word : taper `f :R2->R` (Word transforme l'espace en NBSP) puis
  Ctrl+Espace → popup avec `f\colon\mathbb{R}^2\to\mathbb{R}` ; taper `Sum` en
  début de phrase → reconnu comme `sum`.
