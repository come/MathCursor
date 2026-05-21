# Feat — Popup affiche les PatternCompletion (rendering définitif P7d)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Feat-popup-pattern-completion-spike.md`](2026-05-21-Feat-popup-pattern-completion-spike.md) — P7c spike (commit `705d3d2`)
- [`2026-05-21-Feat-pattern-pipeline-integration-zone-resolver.md`](2026-05-21-Feat-pattern-pipeline-integration-zone-resolver.md) — P7a

## Citation acté

> « ok cest tout bon on continue pas besoin de build iss » — utilisateur, 2026-05-21
> (validation P7c et continuation sur P7d sans test manuel intermédiaire)

## Contexte

P7d est la sous-étape finale qui **restaure l'UX user après P6**.
Suite à P7c (spike pass-through), les `PatternCompletion[]` arrivaient
au popup mais n'étaient pas affichées. P7d les rend visibles dans la
liste d'alternatives et permet à l'utilisateur de les sélectionner.

L'utilisateur Word peut désormais (sous réserve de validation manuelle
build-iss) :
- Taper `V x app a R` + Ctrl+Espace → popup propose `\forall x \in \mathbb{R}`
- Naviguer vers cette suggestion (flèche bas) + Enter → OMath inséré

Le mécanisme respecte les choix validés par AskUserQuestion :
- **Pattern d'abord, puis AmbigMatch** (Choix 5)
- **\square LaTeX → OMath natural** (Choix 4) — déjà fait par les templates en P5

## Décision

### 1. Sentinel `AltIdxPattern = -200` local au popup

```csharp
private const int AltIdxPattern = -200;
```

Constante locale au `SuggestionPopupWindow` (pas dans `SpanOverride.cs`
Core car `SpanOverride` rejette `altIdx < -1`). Marque dans `_altIdxMap`
les entries qui sont des PatternCompletion (vs ambig closed standard).

### 2. Champ `_patternCompletions`

```csharp
private IReadOnlyList<PatternCompletion> _patternCompletions
    = Array.Empty<PatternCompletion>();
```

Stocké au `Show()` pour pouvoir reconstruire les entries Pattern et
log diag.

### 3. Helpers `PrependPatternCompletions` + `MergePrependedMap`

Convertit chaque `PatternCompletion` en `AmbiguityAlternative` virtuelle
(`Latex = PreviewLatex`, `Mutation = patternMutation`) et prepend en
tête de `_alternatives`. `_altIdxMap` étendu avec `AltIdxPattern` pour
chaque entry Pattern.

### 4. Branche `else if (_patternCompletions.Count > 0)`

Cas critique : la zone source n'a pas d'ambig closed mais a des Patterns
(ex. `V x app a R` sans AB/tight-chain). Sans cette branche, le code
existant tombait dans `else { _alternatives = Empty; ... }` et la popup
n'aurait montré aucun item Pattern.

```csharp
else if (_patternCompletions.Count > 0)
{
    _alternatives = PrependPatternCompletions(Array.Empty<...>(), out var patternMap);
    _altIdxMap = patternMap;
}
```

### 5. Handler Pattern dans `ResolveCurrentAltIfFocused`

```csharp
if (realAltIdx == AltIdxPattern)
{
    var patternAlt = _alternatives[_altIndex];
    _resolvedLatex = patternAlt.Latex;
    _finalContainer.Children.Clear();
    _finalContainer.Children.Add(BuildFinalRow(_resolvedLatex));
    _alternatives = Array.Empty<...>();
    _altsRow.Children.Clear();
    _altsRowBorder.Visibility = Visibility.Collapsed;
    _spotStart = _spotEnd = -1;
    _focusOnFinal = true;
    UpdateHighlight();
    return true;
}
```

Quand l'user sélectionne une entry Pattern :
- `_resolvedLatex` prend la valeur de `PreviewLatex` (= `\forall x \in \mathbb{R}` etc.)
- Le `_finalContainer` est refresh pour afficher le nouveau LaTeX
- La zone d'ambig est fermée comme un identity pick
- Focus revient sur la formule finale
- L'user fait Enter → `OnPopupCommitRequested` lit `_popup.CurrentFinalLatex`
  (= `_resolvedLatex`) et insère l'OMath dans Word

### 6. Le check `_spotStart < 0` est sauté pour Pattern

Auparavant le handler skipper si `_spotStart < 0` (= pas d'ambig
closed). Pour les Patterns, le span n'est pas dans `_spotStart` mais
dans `pc.Mutation.Offset/Length`. Le handler Pattern est placé **avant**
le check `_spotStart` pour éviter de skip.

### 7. Pas de mutation source persistante (laisser P9+)

Quand l'user sélectionne un Pattern, **on ne mute pas la source
ContentControl Word**. Le `_resolvedLatex` change localement dans le
popup, le commit Enter insère l'OMath. La source brute reste `V x app a R`
dans le ContentControl jusqu'au commit final qui crée l'OMath.

Conséquence : si l'user re-trigger Ctrl+Espace (sans avoir validé via
Enter), le popup repart de zéro avec la même source brute → même
PatternCompletion proposée. Pas de mémoire cross-popup.

P9+ pourra ajouter un mécanisme de persistance (= `_resolver.ApplyPatternMutation(SourceMutation)`)
si besoin pour des sessions plus complexes (ex. édition multi-zone).

## Tradeoff & alternatives écartées

### Alternative écartée : pre-application automatique côté SuggestionService

L'idée : SuggestionService prend la première PatternCompletion et applique
sa mutation à la source AVANT d'appeler popup.Show. La popup voit
directement la source mutée. Rejetée : retire le choix user, agressive.
L'user pourrait vouloir voir `V x app a R` brut et ne PAS le convertir.

### Alternative écartée : click handler fait apply mutation + re-resolve

Plus complet : quand click pattern, on applique la mutation au
ContentControl Word, re-Resolve avec la source mutée, l'OMath inséré
au commit est le rendu lattice de la source mutée. Avantage : symétrie
avec AddPreference (= mécanisme legacy). Inconvénient : touche le
ContentControl Word à travers la popup, complexité supplémentaire,
risque effets de bord (cross-merge, edit mode, sidecar). Rejetée pour
P7d minimal — le simple set `_resolvedLatex` suffit pour le commit
Enter standard. À envisager en P9+ si besoin avéré.

### Alternative écartée : démarrer popup en `_focusOnFinal = false` si Patterns présents

L'user serait directement en focus sur la première entry Pattern au lieu
de la formule finale. UX possiblement meilleure pour les patterns. Mais
change comportement par défaut existant (`_focusOnFinal = true`).
Rejetée pour P7d — préservation du comportement legacy. À itérer en P8
si user trouve la navigation peu naturelle.

## Conséquences

### Code touché

- **Modifié** : `adapter-vsto/src/MathCursor/UI/SuggestionPopupWindow.cs`
  - Nouveau champ `_patternCompletions`
  - Nouveau const `AltIdxPattern = -200`
  - Nouveaux helpers `PrependPatternCompletions` + `MergePrependedMap`
  - Branche `else if (_patternCompletions.Count > 0)` pour cas patterns-only
  - Handler `if (realAltIdx == AltIdxPattern)` dans `ResolveCurrentAltIfFocused`

Total : ~80 lignes de logique nouvelles, 0 ligne supprimée.

### Tests

- Build VSTO en CLI impossible (VSTOOLS targets — normal). Validation
  via `/build-iss` + test Word.
- Tests Core 1098/1105 verts (inchangé, P7d ne touche pas le Core).
- Tests Adapter 393/393 verts (référencent Core only, ne couvrent pas
  SuggestionPopupWindow).

### API publique

- `SuggestionPopupWindow.Show` : paramètre `patternCompletions`
  optionnel (introduit en P7c, utilisé maintenant en P7d).
- `SuggestionPopupWindow.CurrentFinalLatex` : sémantique enrichie
  — peut maintenant retourner un `PreviewLatex` de PatternCompletion
  au lieu d'un topLatex lattice.

### Régression UX restaurée

Après P7d, l'utilisateur Word voit à nouveau des suggestions dans la
popup pour :
- `V x app a R` → ∀x ∈ ℝ
- `V x app a [0,1]U[3,4]` → ∀x ∈ [0,1]∪[3,4]
- `E x app a N` → ∃x ∈ ℕ
- `R` seul → ℝ
- `N` seul → ℕ
- etc. (3 templates pilote actifs)

La régression P6 est **techniquement restaurée**. P8 validera
manuellement.

### Règles MC impactées

Aucune. Pas de Regex, pas de splice, pas de SuppressMessage.

## Validation post-fix

```bash
# Tests Core (inchangé par P7d)
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1098/1105 verts

# Tests Adapter (inchangé)
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts

# Build VSTO via VS ou /build-iss
# Test manuel Word (P8) :
# 1. Taper "V x app a [0,1]U[3,4]" + Ctrl+Espace
# 2. Vérifier popup affiche "\forall x \in \left[0,1\right] \cup \left[3,4\right]" en tête
# 3. Flèche bas pour focus sur l'alt → Enter
# 4. Vérifier OMath inséré = ∀x ∈ [0,1]∪[3,4]
# 5. Vérifier que AB/tight-chain/decimal restent fonctionnels (rétro-compat)
```

## Plan Patterns — état d'avancement (ROADMAP Chantier 6)

- [x] **P7a** — Core (commit `a2d2516`)
- [x] **P7b** — Adapter VSTO (commit `2bd45ac`)
- [x] **P7c** — Popup spike pass-through (commit `705d3d2`)
- [x] **P7d** — Popup rendering définitif (cet ADR) ✨ **UX user-visible restaurée**
- [ ] **P8** — Validation manuelle PAP-friendly via `/build-iss` + Word
- [ ] **P9+** — `LimTemplate`, `SumTemplate`, etc.
