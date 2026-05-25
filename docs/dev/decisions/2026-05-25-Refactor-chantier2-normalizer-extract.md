# Refactor — Chantier 2 : extraction module Normalization

**Date :** 2026-05-25
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- ADR [2026-05-25-Refactor-chantier1-data-driven-fr-keywords](2026-05-25-Refactor-chantier1-data-driven-fr-keywords.md) (= Chantier précédent).
- Plan simplification du Resolve 2026-05-25.

## Citation acté

> « go continue » — utilisateur, 2026-05-25 (= validation du Chantier 2 après Ch1 livré)

## Contexte

Le `Tokenizer` mélangeait 2 responsabilités :

1. **char → Token** (= sa vraie mission).
2. **Normalisation de données** :
   - Détection + remplacement des caractères primes Unicode (`'`, `″`, `‴`, `⁗`, etc.) en `'` ASCII répétés.
   - Lookup case-tolerant pour les Functions (`Cos→\cos`, `OMEGA→\Omega`).

Ces 2 transformations sont des **données déterministes** (= pas de matching, pas de contexte). Elles n'ont pas leur place dans le tokenizer — elles méritent un module dédié, testable individuellement, réutilisable.

## Décision

Créer `core-csharp/src/MathCursor.Engine/Normalization/` avec :

### `PrimeNormalizer.cs`
- `bool IsPrimeChar(char c)` : true si caractère prime (= 9 variants).
- `int PrimeCount(char c)` : nombre de primes ASCII représentés.
- `string Normalize(string? raw)` : canonicalise tous les primes en `'` ASCII répétés.

### `CaseToleranceLookup.cs`
- `bool TryLookup(IReadOnlyDictionary<string, string> dict, string word, out string value)` :
  stratégie d'essais successifs (exact → all-upper retry → lowercase fallback).

### `Normalizer.cs` (façade)
- Forward vers les 2 helpers ci-dessus.
- Sert de point d'extension pour futures passes (= pre-tokenize string-level si besoin, post-tokenize token-level si besoin).

### Tokenizer
- `IsPrimeChar` + `NormalizePrimes` méthodes locales supprimées (= dead code post-migration).
- `TryLookupFunction` réduit à un one-liner qui délègue à `Normalizer.TryLookupCaseTolerant`.

## Tradeoff & alternatives écartées

- **Mettre le Normalizer comme pipeline configurable YAML** : rejetée pour V1. Le Normalizer reste statique car ses transformations sont déterministes et orthogonales. Si un jour on veut activer/désactiver des passes via YAML, on étendra la façade `Normalizer`.

- **Faire le Normalizer instance + DI** : rejetée. Les transformations sont sans état, statiques. DI ajouterait du bruit pour zéro bénéfice.

- **Garder le tokenizer monolithique** : rejetée. Le brief simplification 2026-05-25 vise un Tokenizer minimal (= « char → Token simple, aucune smart reclassification »). Ce Chantier prépare le terrain pour les Chantiers 3+ (= pre-passes → rules YAML, qui nécessitent un tokenizer prévisible).

## Conséquences

- **Code touché** :
  - `core-csharp/src/MathCursor.Engine/Normalization/PrimeNormalizer.cs` (+57 lignes, nouveau).
  - `core-csharp/src/MathCursor.Engine/Normalization/CaseToleranceLookup.cs` (+43 lignes, nouveau).
  - `core-csharp/src/MathCursor.Engine/Normalization/Normalizer.cs` (+45 lignes, façade).
  - `core-csharp/src/MathCursor.Engine/Tokenization/Tokenizer.cs` (−40 lignes, helpers supprimés).

- **Tests** :
  - `Normalization/PrimeNormalizerTests.cs` (+22 cas via `[Theory]` : variants Unicode, count, normalisation).
  - `Normalization/CaseToleranceLookupTests.cs` (+6 cas : exact, autocapitalize, all-upper retry, lowercase preferred, capitalized direct, not found).
  - 297/297 engine v2 verts (= +31 vs Ch1) + 3 skipped.

- **API publique** : 3 classes statiques `public`. Pas de breaking change.

- **Règles MC impactées** : aucune.

## Validation post-fix

1. `dotnet test core-csharp/tests/MathCursor.Engine.Tests/` → 297/297 + 3 skipped.
2. Tests fonctionnels existants (`f'`, `cos x`, `Cos x`, `OMEGA`) inchangés (= passent par les helpers extraits).

## Plan en cours — état d'avancement

Chantier 2 / 6 du plan simplification du Resolve.

| # | Chantier | Statut |
|---|---|---|
| 1 | hardcoded FR → YAML | ✅ |
| **2** | **Normalizer dédié** | ✅ acté ici |
| 3 | Pre-passes (multi-line + prefix-match) → règles YAML | à faire |
| 4 | Collisions C# → règles YAML | à faire |
| 5 | RuleBasedMerger data-driven | à faire |
| 6 | Découper `SuggestionService` god class | à faire |
