# Test — Skip NER honnête (SkippableFact) au lieu du faux-vert

**Date :** 2026-06-23
**Kind :** Test
**Température :** molle
**Statut :** acté
**Lié à :** —

## Citation acté

> [audit 2026-06-22 — #6] NER en faux-vert. « ok 6 7 » — utilisateur, 2026-06-23

## Contexte

`MathNerInferenceTests` (6 `[Fact]`, sanity + 4 seuils F1) garde la perf du modèle
NER. Mais chaque test fait `if (!_fix.Available) { _log.WriteLine(_fix.SkipReason); return; }` :
quand le modèle ONNX (129 Mo, **jamais commité**) est absent — c.-à-d. sur la plupart
des machines — les 6 tests **rapportent VERT** au lieu de *skipped*. Le garde-fou F1
est donc inerte ET invisible : on croit la perf NER vérifiée alors qu'aucun test n'a
tourné. Fausse confiance, exactement le scénario que ces tests devaient prévenir.

## Décision

Passer les 6 tests en `[SkippableFact]` (package `Xunit.SkippableFact`) et remplacer
le early-return par `Skip.IfNot(_fix.Available, _fix.SkipReason)`. Modèle absent →
statut **Skipped** explicite (visible dans le runner et le gate), pas Passed. Modèle
présent → exécution réelle des seuils F1, inchangée.

## Tradeoff & alternatives écartées

- **`Assert.Skip`** : n'existe qu'en xUnit v3 ; le projet est en xUnit 2.9. Écarté.
- **`[Fact(Skip="…")]` statique** : skip TOUJOURS, même quand le modèle est là → on perdrait le garde-fou. Écarté.
- **Committer le modèle (129 Mo)** : alourdit le dépôt ; orthogonal (sujet « où héberger le modèle » à part). Le skip honnête est la correction minimale.

## Conséquences

- **Code** : `MathCursor.Tests.csproj` (+ `Xunit.SkippableFact`), `MathNerInferenceTests.cs` (6× `[Fact]`→`[SkippableFact]`, early-return → `Skip.IfNot`).
- Sur une machine sans modèle : 6 tests **Skipped** (avant : 6 faux-Passed). Le gate ne ment plus sur la couverture NER.
- Reste à décider séparément : héberger/télécharger le modèle pour que ces tests tournent réellement quelque part (sinon le garde-fou reste théorique, juste honnête).
