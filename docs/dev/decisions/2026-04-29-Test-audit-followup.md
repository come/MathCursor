# Test — Audit follow-up : combler les angles morts de tests

**Date :** 2026-04-29
**Kind :** Test
**Température :** molle
**Statut :** acté

## Contexte

Audit du code lancé par l'utilisateur le 2026-04-29 (3 sub-agents Explore +
vérifications). Synthèse :

- **Adapter VSTO = 0 test** → angle mort majeur. C'est la classe de bug qui a
  produit `\int → ∈t` en v0.5.2 (régression silencieuse côté rendu, repérée
  uniquement par usage utilisateur).
- **3 désambiguïtés annoncées en v0.5.3 sans test direct** : `f(1,2)` (call vs
  vector-coords), `vector-layout-flip` (col↔row), `vec-dot-product` (`u*v`).
  Le code est dans `core-csharp/src/MathCursor.Core/Lattice/AlternativeGenerator.cs`
  mais aucune assertion ne le couvre.
- **Code mort confirmé** : `core-csharp/src/MathCursor.Core/Symbols/SymbolMatcher.cs`
  + `SymbolMatch.cs` (zéro usage runtime, vestige pré-LatticeEngine).
- **Glob orphelin** dans `MathCursor.Core.csproj` : `data/yaml_domains/**/*.yaml`
  pointe sur un dossier inexistant.
- **WpfMathAdapter.Adapt(string→string) non testé** alors qu'il est pure
  compute (rendu LaTeX→WpfMath pour la popup preview).
- Couverture core par ailleurs **solide** (~500 cas + 276 paires gold + 230
  lycée + 75 seconde) → l'audit ne remet pas en cause cette base.

## Décision

5 actions priorisées, exécutées en bloc dans cet ADR umbrella :

| # | Action | Effort | Cible |
|---|---|---|---|
| 1 | Cleanup : supprimer `Symbols/SymbolMatcher.cs` + `SymbolMatch.cs` ; retirer le glob `data/yaml_domains/**/*.yaml` du csproj | ~15 min | `core-csharp/` |
| 2 | Tests désambig manquants (`f(1,2)`, layout-flip, `u*v`) | ~2h | `core-csharp/tests/MathCursor.Core.Tests/Lattice/AlternativeGeneratorTests.cs` |
| 3 | Corpus pathologiques (régressions historiques : `\int`, NBSP, `\vec{AB}`) ajouté à `yaml-gold-extracted.txt` ou fichier dédié | ~1h | `core-csharp/tests/MathCursor.Core.Tests/corpus/` |
| 4 | Créer `adapter-vsto/tests/MathCursor.Tests.csproj` (xUnit, .NET Framework 4.8) + tests `WpfMathAdapter.Adapt()` (10-15 cas) | ~2h | `adapter-vsto/tests/` |
| 5 | Tests inférence NER offline : charge `models/distilmult-v4` + échantillon `data/ner-corpus/*.jsonl`, assert F1 ≥ seuil sur 50-100 cas | ~4h | même csproj que #4 |

## Justifications par action

### #1 Cleanup

`SymbolMatcher` n'a plus aucun usage runtime depuis le pivot Lattice. Les
anciens `SymbolMatcherTests` ont déjà été supprimés du filesystem (visibles
seulement dans un vieux `testresults.trx` du 23 avril). Garder ce code en
parallèle de `LatticeEngine` brouille la lecture.

Le glob `yaml_domains` est silencieux en build (pas d'erreur si le dossier
manque) mais induit en erreur quiconque cherche les ressources embarquées.

### #2 Désambig

Annoncées en v0.5.3 dans le changelog mais non testées : si quelqu'un casse
le code de `ScanFunctionTypicalWithCommaCoords()` ou `ScanVectorLayoutFlipTopLevel()`,
rien ne le détecte. Effort minime (3 tests dans un fichier déjà existant).

### #3 Corpus pathologiques

Les bugs historiques (`\int → ∈t`, NBSP) sont passés en prod parce qu'aucun
test ne couvrait le cas exact. Une section "régressions historiques" dans le
corpus = filet anti-rechute pour les futurs refactos.

### #4 + #5 Adapter tests

L'adapter VSTO accumule 5 composants critiques sans test. Commencer par les
deux plus testables hors VSTO :
- **WpfMathAdapter.Adapt** est `string → string` pure → testable trivialement.
- **MathNerDetector** prend un `modelDir` et un `string` → produit
  `List<DetectedZone>`. Pas de dépendance Word/Office, l'ONNX se charge en
  mémoire depuis xUnit.

Le csproj `MathCursor.Tests` créé ici servira de base pour les futurs tests
adapter (SuggestionService, VstoEquationStore quand on aura besoin).

## Hors scope

- Tests `SuggestionService` / `SuggestionPopupWindow` / `VstoEquationStore` :
  nécessitent du mocking VSTO (Word.Application). À aborder dans un ADR
  séparé après les premiers retours sur le csproj créé ici.
- CI GitHub Actions : on s'assure d'abord que les tests tournent en local.
  Wiring CI = ADR séparé.

## Validé par l'utilisateur

> oui go tout ca en vrai

## Suivi

Tasks 2 → 6 dans la session. Ordre d'exécution choisi (du plus safe au plus
gros) :

1. Cleanup (#1) — défriche.
2. Désambig (#2) — fast feedback, tests dans projet déjà existant.
3. Corpus pathologiques (#3) — texte pur.
4. Adapter csproj + WpfMathAdapter (#4) — gros pas mais isolé.
5. NER inference (#5) — réutilise le csproj.
