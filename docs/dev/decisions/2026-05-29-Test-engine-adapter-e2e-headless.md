# Test — Harnais e2e headless moteur V2 → UnicodeMath (sans Word)

**Date :** 2026-05-29
**Kind :** Test
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [`2026-05-28-Refactor-rewriting-engine-v2-clean.md`](2026-05-28-Refactor-rewriting-engine-v2-clean.md) (moteur V2), [`2026-05-23-Feat-engine-v2-promotion.md`](2026-05-23-Feat-engine-v2-promotion.md) (promotion principal), commit `e79a231` (intervalles/ensembles/listes)

## Citation acté

> « oui go » — utilisateur, 2026-05-29

(en réponse au plan : nouveau projet de test exerçant `texte → EngineZoneSource → ResolvedZone.TopLatex → LatexToUnicodeMath` et assertant l'UnicodeMath final, pour valider l'intégration sans ouvrir Word)

## Contexte

Le moteur V2 (`MathCursor.Engine`) est déjà câblé comme source principale du
`ZoneResolver` dans l'add-in VSTO (`SuggestionService.cs:216-234`, flag
`MATHCURSOR_ENGINE_V2` ON) et **toute la solution compile, add-in .NET 4.8
inclus** (build MSBuild : 0 erreur). L'intégration au niveau câblage n'a donc
rien à recoder.

Reste un risque non vérifié : le moteur V2 a été reconstruit *from scratch* et
émet désormais du LaTeX pour de **nouvelles** constructions (intervalles
`[0;1]`, ensembles `\{0\}`, union/intersection/différence `\cup`/`\cap`/`\setminus`,
matrices `\begin{bmatrix}`). Le dernier maillon avant l'OMath Word est
`MathCursor.Core.LatexToUnicodeMath.Convert` — écrit pour le vocabulaire de
l'**ancien** moteur. Si une construction V2 n'y est pas couverte, l'utilisateur
verrait un OMath vide/cassé dans Word, sans qu'aucun test actuel ne l'attrape
(les tests YAML s'arrêtent au LaTeX ; les tests adapter exigent Word).

La philosophie projet est « tapé → attendu, sans ouvrir Word ». Il manque ce
harnais au **bout de chaîne** (UnicodeMath = ce que Word reçoit avant `BuildUp`).

## Décision

Créer un projet de test dédié **`MathCursor.Engine.Adapter.Tests`** (net8.0,
xUnit) qui référence `MathCursor.Engine.Adapter` (et tire transitivement Core +
Engine). Il exerce la chaîne réellement utilisée par l'add-in, sans Word :

```
texte tapé → EngineZoneSource.TryResolve → ResolvedZone.TopLatex → LatexToUnicodeMath.Convert → UnicodeMath
                (Engine + Engine.Adapter)                              (Core)
```

et asserte la sortie **UnicodeMath** (pas seulement le LaTeX) pour des cas
représentatifs de chaque famille de règles, en insistant sur les nouvelles :
intervalles, ensembles, union/diff, matrices, plus un échantillon
sommes/intégrales/limites/fractions/vecteurs/décimales déjà couvertes.

Le projet est ajouté à `MathCursor.sln`.

## Tradeoff & alternatives écartées

- **Ajouter les cas e2e à `MathCursor.Engine.Tests`** : rejeté — forcerait le
  projet de test du moteur pur à référencer Core (entorse au layering L1→L2) et
  mélangerait deux niveaux (moteur isolé vs chaîne complète).
- **Valider uniquement dans Word** : rejeté — lent, manuel, aucune garde de
  régression, contraire à la philosophie « sans Word ». À garder comme étape
  *suivante* (validation finale OMath), pas comme filet de régression.
- **Tester `EngineToResolvedZone` isolément** (sans `LatexToUnicodeMath`) :
  rejeté — raterait le vrai risque, qui est précisément la conversion
  UnicodeMath finale et sa couverture du vocabulaire V2.

## Conséquences

- **Code touché** : nouveau `core-csharp/tests/MathCursor.Engine.Adapter.Tests/`
  (csproj + 1 fichier de tests), ajout au `.sln`. Aucune touche au code de prod.
- **Tests** : ~15 cas e2e text→UnicodeMath. Tout écart de vocabulaire
  LaTeX→UnicodeMath devient un échec rouge ici, pas un OMath cassé en Word.
- **API publique** : inchangée.
- **Règles MC impactées** : aucune.

## Validation post-fix

`dotnet test` sur le nouveau projet vert = la chaîne headless complète produit
l'UnicodeMath attendu pour toutes les familles. Un rouge pointe la construction
non couverte par `LatexToUnicodeMath` à corriger AVANT la validation Word.
