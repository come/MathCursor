# Fix — Durcissement audit : dialogues d'erreur i18n + statics Vocabulary en lecture seule

**Date :** 2026-06-23
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Lié à :** [2026-06-22-Fix-surface-silent-failures.md](2026-06-22-Fix-surface-silent-failures.md)

## Citation acté

> [audit 2026-06-22 — #7] i18n MessageBox FR-only + statics Vocabulary mutables. « ok 6 7 » — utilisateur, 2026-06-23

## Contexte

Deux constats « à surveiller » de l'audit, regroupés :

1. **i18n** — l'UI est localisée via `Strings`, MAIS les **seuls dialogues d'erreur
   réellement vus** par l'utilisateur étaient codés en FR pur : `ThisAddIn.cs:143`
   (échec démarrage), `RibbonCallback.cs:174` (encadré), `:325` (colonnes), + StatusBar
   `:138`. L'install vise l'EN par défaut → un prof anglophone tombe sur du français au
   pire moment.
2. **État partagé mutable** — `Vocabulary.Vocab/Sep/Role/Splittable` étaient `public
   static readonly` mais sur des **collections mutables** (le `readonly` protège la
   référence, pas le contenu). Immuables par convention seulement (« jamais mutés
   ensuite ») ; rien n'empêchait un appelant de corrompre l'état partagé entre analyses.

## Décision

1. **i18n** : 4 nouvelles clés `Strings` (FR/EN) — `StatusReady`, `StartupFailed`,
   `CalloutInsertFailed`, `ColumnsInsertFailed` — substituées aux littéraux FR.
2. **Lecture seule** : `Vocab/Sep/Role` exposés en `IReadOnlyDictionary` (champ privé
   `_vocab/_sep/_role` rempli au cctor). `Splittable` : `IReadOnlySet` n'existe pas en
   netstandard2.0 et le `.Contains` est en hot-path lexer → encapsulé derrière
   `IsSplittable(s)` (O(1)) + `SplittableTokens` (énumération). `Vocabulary` est
   `internal` → changement contenu à l'assembly engine, zéro impact adapter.

## Tradeoff & alternatives écartées

- **`IReadOnlySet` pour Splittable** : absent de netstandard2.0. **`IReadOnlyCollection` + LINQ `.Contains`** : dégraderait le hot-path lexer en O(n). → méthode `IsSplittable` dédiée.
- **Laisser l'i18n** : un dialogue d'erreur FR devant un utilisateur EN est le pire moment pour de l'incohérence.
- **`ImmutableDictionary`** : dépendance + alloc, sans gain sur un dict rempli une fois. `IReadOnlyDictionary` sur backing field suffit.

## Conséquences

- **Code** : `Strings.cs` (+4 clés FR/EN), `ThisAddIn.cs` (StatusBar + MessageBox démarrage), `RibbonCallback.cs` (2 MessageBox). `Vocabulary.cs` (encapsulation, cctor → champs privés), `Lexer.cs` (2 sites `Splittable` → `SplittableTokens`/`IsSplittable`). `Score.cs` : zéro.
- **Tests** : moteur 21/21 inchangé (456 fixtures + mutations) ; adapter compile + 363.
