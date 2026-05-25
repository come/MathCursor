# Refactor — Chantier 1 : hardcoded FR (stopwords/delimiters/keywords) → YAML

**Date :** 2026-05-25
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- ADR [2026-05-22-Feat-engine-poc-isolation](2026-05-22-Feat-engine-poc-isolation.md) (= principe data-driven engine v2).
- ADR [2026-05-23-Feat-engine-v2-promotion](2026-05-23-Feat-engine-v2-promotion.md) (= engine v2 promu moteur principal).
- Brief « Simplification du resolve » 2026-05-25 (= chantiers 1–6 planifiés).

## Citation acté

> « oui on fait 1-6 je valide tes decisions 1-2-3 et et je veux un RuleBasedMerger avec les Yaml » — utilisateur, 2026-05-25

## Contexte

L'adapter VSTO comportait 4 listes hardcodées en C# qui décrivaient en réalité des **données locales FR** :

1. `ManualTriggerController.Stopwords` (29 mots) — bornes backward du span Ctrl+Espace.
2. `ManualTriggerController.Delimiters` (9 chars) — chars qui bornent le span.
3. `ZoneRefiner.MathPrefixKeywords` (19 mots) — keywords qui étendent rétroactivement la zone NER.
4. `Tokenizer.multiCharOps` (21 ops) — opérateurs multi-char tokenisés.

C'est de la data locale qui doit vivre dans `data-v2/locale/fr.yml`. Sinon ajouter un mot-outil FR demande un build C#, et la version EN devrait avoir sa propre liste mais en code (= duplication impraticable).

## Décision

Migrer les 4 listes vers `data-v2/locale/fr.yml`. Le vocab `LocaleVocabulary` expose les listes comme `HashSet<string>` / `HashSet<char>`. L'adapter VSTO accède au vocab via `MathEngine.Vocab` (= nouveau accessor public) ou via `LocaleVocabulary.LoadEmbedded(code)`.

### Nouveaux champs YAML

```yaml
# data-v2/locale/fr.yml

stopwords:
  - soit
  - et
  - ou
  - … (29 entries)

span_delimiters:
  - '.'
  - ';'
  - '='
  - "\n"
  - … (9 entries)

math_prefix_keywords:
  - lim
  - limite
  - somme
  - vec
  - … (19 entries)
```

### Migration code

- `LocaleVocabulary` : 3 propriétés `Stopwords`, `SpanDelimiters`, `MathPrefixKeywords`. POCO `RawDoc` étendu.
- `MathEngine` : nouvelle property publique `Vocab` (= expose `_vocab` interne) pour que l'adapter accède.
- `ManualTriggerController` : ctor reçoit `LocaleVocabulary` ; `ComputeSpanStart` accepte un param vocab ; les arrays static supprimés.
- `ZoneRefiner.ExtendBackwardWithKeyword` : accepte un param vocab ; HashSet static supprimé.
- `SuggestionService` : charge le vocab depuis l'engine v2 (= `engineV2.Vocab`) ou fallback `LocaleVocabulary.LoadEmbedded("fr")` si engine v2 KO.
- `Tokenizer.TryReadSymbolAhead` : la liste `multiCharOps` est maintenant dérivée de `LocaleVocabulary.Relations` (= keys non-alphabétiques de longueur ≥ 2, triées par longueur décroissante). Cache `_multiCharCache` par vocab pour éviter de reconstruire à chaque token. Liste mono-char structurelle conservée (= robustesse même si vocab incomplet).

### Project reference adapter VSTO tests

`MathCursor.Tests.csproj` ajoute `ProjectReference` vers `MathCursor.Engine` pour pouvoir importer `LocaleVocabulary` dans les tests adapter (= `ZoneRefinerTests` charge le vocab pour tester les méthodes data-driven).

## Tradeoff & alternatives écartées

- **Ne rien migrer (= conserver hardcoded)** : rejetée. Ajouter un mot demande un build C# au lieu d'un edit YAML.
- **Migrer SEULEMENT 1–2 listes** : rejetée. Cohérence vise un seul moment de migration plutôt que dette résiduelle.
- **Faire le tokenizer multi-char en plus** : inclus dans cette même migration (= unifie le scope « tout ce qui est data FR »).

## Conséquences

- **Code touché** :
  - `data-v2/locale/fr.yml` (+90 lignes : 3 nouvelles sections).
  - `core-csharp/src/MathCursor.Engine/Vocabulary/LocaleVocabulary.cs` (+30 lignes : 3 propriétés + RawDoc + ctor).
  - `core-csharp/src/MathCursor.Engine/MathEngine.cs` (+3 lignes : accessor `Vocab`).
  - `core-csharp/src/MathCursor.Engine/Tokenization/Tokenizer.cs` (+40 lignes : `TryReadSymbolAhead` data-driven + cache).
  - `adapter-vsto/src/MathCursor/Host/ManualTrigger/ManualTriggerController.cs` (~10 lignes : suppression hardcoded, ctor param vocab, ComputeSpanStart param vocab).
  - `adapter-vsto/src/MathCursor/Host/Detection/ZoneRefiner.cs` (~10 lignes : suppression hardcoded, méthode param vocab).
  - `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` (~10 lignes : field `_vocab`, chargement, propagation ctor).
  - `adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj` (+1 ProjectReference vers MathCursor.Engine).
  - `adapter-vsto/tests/MathCursor.Tests/Host/Detection/ZoneRefinerTests.cs` (+vocab loader + signatures tests adaptées).

- **Tests** :
  - 4 nouveaux tests xUnit (Chantier1DataDrivenTests) qui valident le chargement YAML.
  - 266/266 engine v2 verts + 3 skipped (= cases pré-existants).
  - 393/393 adapter VSTO préservés.

- **API publique** : `MathEngine.Vocab` exposé (= read-only). Pas de breaking change.

- **Règles MC impactées** : aucune.

## Validation post-fix

1. `dotnet test core-csharp/tests/MathCursor.Engine.Tests/` → 266/266 + 3 skipped.
2. `vstest adapter-vsto/tests/MathCursor.Tests.dll` → 393/393.
3. Test manuel Word : tape « Soit f = x+1 » + Ctrl+Espace → la zone démarre après « Soit » (= stopword) → conv `f=x+1`. Idem « limite x→0 f(x) » → la zone capture « limite » via math_prefix_keywords.

## Plan en cours — état d'avancement

Chantier 1 / 6 du plan de simplification du Resolve (= cf. brief 2026-05-25).

| # | Chantier | Statut |
|---|---|---|
| **1** | **Cleanup hardcoded FR → YAML** | ✅ acté ici |
| 2 | Normalizer dédié | à faire |
| 3 | Pre-passes (multi-line + prefix-match) → règles YAML | à faire |
| 4 | Collisions C# → règles YAML | à faire (brief existe) |
| 5 | RuleBasedMerger (= séparer Merger du Resolve, data-driven) | à faire |
| 6 | Découper `SuggestionService` (2579 LOC god class) | à faire |
