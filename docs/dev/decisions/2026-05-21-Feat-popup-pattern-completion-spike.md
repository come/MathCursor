# Feat — Popup consomme PatternCompletion (spike pass-through P7c)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** provisoire
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Feat-pattern-pipeline-integration-zone-resolver.md`](2026-05-21-Feat-pattern-pipeline-integration-zone-resolver.md) — P7a
- [`2026-05-21-Feat-suggestion-service-pattern-injection.md`](2026-05-21-Feat-suggestion-service-pattern-injection.md) — P7b (commit `2bd45ac`)

## Citation acté

> « oui on termine » — utilisateur, 2026-05-21
> (validation pour enchaîner P7c → P7d après P7b)

## Contexte

P7c est la sous-étape **WPF Popup** de P7. Suite à P7a (Core produit
les `PatternCompletion[]`) et P7b (Adapter VSTO injecte le registry),
le flux technique end-to-end est wiré : `SuggestionService.Show` passe
maintenant `resolved.PatternCompletions` à `SuggestionPopupWindow.Show`.

**Spike technique** : pour P7c, on fait un pass-through minimal — la
popup reçoit le paramètre `patternCompletions` mais ne modifie pas
encore son rendering visuel. Elle log les complétions en diagnostic.
P7d validera en Word puis itèrera le rendering selon l'observation
manuelle.

Pourquoi spike plutôt qu'implémentation complète : le rendering UX
exact (Pattern d'abord, click handler, application de mutation source,
gestion AltIdxMap avec sentinel Pattern, propagation vers
re-pipeline) implique plusieurs décisions UX qui se prennent mieux
avec un POC visuel en Word. Le commit P7c "spike" valide la plomberie
sans engager prématurément un rendering qu'on devra peut-être refaire.

## Décision

### 1. Signature `SuggestionPopupWindow.Show` étendue

```csharp
public void Show(
    string topLatex,
    string ruleId,
    IReadOnlyList<AmbiguityAlternative> alternatives,
    int spotStart,
    int spotEnd,
    IReadOnlyList<AmbiguityMatch> allMatches,
    double screenX,
    double screenY,
    string debugText = "",
    IReadOnlyList<PatternCompletion>? patternCompletions = null)  // ← NOUVEAU
```

Paramètre **optionnel en queue** → rétro-compat avec les call-sites
existants (1 seul actuellement dans `SuggestionService.cs`).

### 2. `SuggestionService.Show` passe `resolved.PatternCompletions`

```csharp
_popup.Show(resolved.TopLatex, ruleId, alts, spotStart, spotEnd,
    resolved.AllMatches, popupX, popupY, debugText,
    resolved.PatternCompletions);
```

### 3. Pass-through avec log diag

```csharp
if (patternCompletions != null && patternCompletions.Count > 0)
{
    LogPopup($"Pattern completions: {patternCompletions.Count}, first preview=\"{patternCompletions[0].PreviewLatex}\" desc=\"{patternCompletions[0].Description}\"");
}
```

Aucune modification du flow de filtrage, des `_alternatives`, ou du
rendering visuel. Le LogPopup permet de **vérifier en P7d que le flux
est wired** correctement (= les complétions arrivent bien à la popup).

### 4. Pas de click handler dédié

Les `PatternCompletion[]` ne sont pas insérées dans `_alternatives`
ni dans `_altIdxMap`. Donc :
- Aucun nouveau click handler à wire.
- Aucun risque de casser le flow d'interaction existant.
- Le user ne voit visuellement aucune différence — mais le log diag
  prouve que la plomberie fonctionne.

## Tradeoff & alternatives écartées

- **Option α : implémentation complète** (Pattern en tête de liste +
  click handler → apply mutation → re-pipeline). Rejetée pour P7c :
  multiplie les décisions UX prises sans POC visuel. Risque de devoir
  refaire en P7d.
- **Option β : affichage seul (preview) sans click handler**. Rejetée
  : nécessite déjà modifier le rendering layout (BuildAltCells,
  highlight nav, etc.). Effort intermédiaire moyen sans garantie que
  le résultat visuel sera celui voulu.
- **Option γ : pré-application automatique de la mutation source côté
  SuggestionService** (= le user voit le rendu cible avant même de
  cliquer). Rejetée : trop agressif, retire le choix user, risque
  ergonomie.
- **Option spike (retenue)** : pass-through technique + log diag.
  Permet à P7d de valider la plomberie en Word, observer ce qui
  s'affiche, et décider du rendering définitif en connaissance de
  cause. Faible engagement, faible risque.

## Conséquences

### Code touché

- **Modifié** :
  - `adapter-vsto/src/MathCursor/UI/SuggestionPopupWindow.cs` — signature
    `Show` étendue avec `patternCompletions` optionnel + LogPopup diag
  - `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` — appel
    `_popup.Show` passe `resolved.PatternCompletions`

### Tests

- Build VSTO en CLI ne marche pas (VSTOOLS targets — normal sur machine
  sans Office MSBuild). Sera buildé par VS / `/build-iss`.
- Tests adapter (qui référencent Core only) : 393/393 verts (P7c n'a
  pas touché le Core).
- **Validation manuelle en P7d** : invoquer `/build-iss` puis tester
  en Word :
  1. Taper `V x app a R` + Ctrl+Espace
  2. Observer le log popup → vérifier que `Pattern completions: N` est
     loggé avec la bonne preview
  3. Si OK : décider du rendering visuel pour itération P7c+

### API publique

- `SuggestionPopupWindow.Show` : paramètre optionnel en queue.
  Rétro-compat.

### Régression UX

**Toujours dégradée** côté visuel : l'utilisateur ne verra rien
de nouveau dans la popup. Le flux technique est complet mais le
rendering reste celui de P6. La régression UX P6 (popup vide pour
V/E/R/N/Z/Q/C) **n'est PAS restaurée à P7c**. P7d décide du rendering.

## Validation post-fix

```bash
# Tests adapter
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts

# Build VSTO (manuel via VS ou /build-iss)
# Test manuel Word : taper V x app a R + Ctrl+Espace, vérifier log
```

## Fenêtre de réversibilité

Température **provisoire** — spike technique. Conditions qui
justifieraient un revert/réécriture :

1. **P7d révèle un problème de plomberie** : par exemple la popup ne
   reçoit pas les complétions (= `patternCompletions == null` toujours)
   à cause d'un mismatch dans le ZoneResolver ou un bug VSTO. Debug
   + fix dans P7d itération.
2. **Le rendering "rien de visible" est trop frustrant** pour le test
   manuel : on itère immédiatement avec un rendering minimal (ex.
   afficher le `Description` en tooltip ou status bar) avant le test
   manuel.

P7c+ (rendering complet) sera décidé en P7d.

## Plan Patterns — état d'avancement

- [x] **P7a** — Core (commit `a2d2516`)
- [x] **P7b** — Adapter VSTO injection (commit `2bd45ac`)
- [→] **P7c** — Popup spike pass-through (cet ADR)
- [ ] **P7d** — Build + test manuel Word + ADR final + itération rendering
