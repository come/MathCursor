# Feat — Patterns : trailing-space hints + IsIncomplete pour popup persistante (P5R+)

**Date :** 2026-05-21
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [`2026-05-21-Refactor-forall-belongs-arglist-convention.md`](2026-05-21-Refactor-forall-belongs-arglist-convention.md) — P5R (commit `451d94d`)
- [`2026-05-21-Feat-popup-pattern-completion-rendering.md`](2026-05-21-Feat-popup-pattern-completion-rendering.md) — P7d (commit `1e8f54a`)

## Citation acté

> « quand le head d'un truc avec argument est detecté, la popup ne doit pas se fermer à l'espace.. et doit montrer des carrés sur les arguments qui arrivent formatés comme il faut » — utilisateur, 2026-05-21

Choix validés via AskUserQuestion :
- **Convention trailing space = signal arg optionnel attendu** pour forall
- **`\square` LaTeX noir standard** (= rendu naturel par WPF MixedLatexRenderer)
- **Head seul → IsIncomplete = true** (popup reste ouverte tant que pattern actif)

## Contexte

P5R/P7d ont livré la fonctionnalité Patterns visible dans la popup, mais
2 trous UX subsistaient :

1. **Popup se fermait à l'espace** quand l'user tapait `V` puis espace
   (= aucun signal "pattern incomplet" côté `ResolvedZone.IsIncomplete`).
2. **Pas de carrés guidant** la saisie : `V` seul affichait juste `∀`
   au lieu de `∀▭` (= hint visuel pour le slot var attendu).

P5R+ résout les deux.

## Décision

### 1. Trailing whitespace = signal arg optionnel attendu (forall)

`ForallBelongsTemplate.Expand` détecte si la source post-args termine
par whitespace (= user a fini de taper et appuyé espace). Si oui + le
slot domain n'est pas identifié + au moins 1 var présente, on ajoute
un hint `\in \square` dans `HintLatex` (mais **pas** dans
`PreviewLatex`).

```csharp
private static bool HasTrailingWhitespaceAfterArgs(
    string source, int sourceAfterHead, IReadOnlyList<ArgSpan> args)
{
    int lastEnd = args.Count > 0 ? args[args.Count - 1].End : sourceAfterHead;
    if (lastEnd >= source.Length) return false;
    if (!char.IsWhiteSpace(source[lastEnd])) return false;
    for (int i = lastEnd; i < source.Length; i++)
        if (!char.IsWhiteSpace(source[i])) return false;
    return true;
}
```

Comportement résultant :

| Source | `PreviewLatex` (commit) | `HintLatex` (popup) |
|---|---|---|
| `V` | `\forall` | `\forall \square \in \square` (V seul → template complet) |
| `V ` | `\forall` | `\forall \square \in \square` |
| `V x` | `\forall x` | `\forall x` |
| `V x ` | `\forall x` | `\forall x \in \square` (trailing → carré domain) |
| `V x R` | `\forall x \in \mathbb{R}` | identique |
| `V x R ` | `\forall x \in \mathbb{R}` | identique (déjà complet) |

Le carré domain n'apparaît que sur **action explicite de l'user**
(trailing space). Le slot domain reste optionnel : `V x` (sans
trailing) ne pollue pas avec un carré.

Convention spécifique à forall (= slot optionnel avec opener implicite
"espace"). Lim/Sum/Int (P9+) avec slots **tous requis** afficheront
leurs carrés en permanence dès le head, indépendamment du trailing.

### 2. `ResolvedZone.IsIncomplete` étendu pour patterns partiels

`ZoneResolver.Resolve` :

```csharp
bool incomplete = ComputeIsIncomplete(rawSource, ambig.TopLatex)
    || HasPartialPatternCompletion(patternCompletions);

private static bool HasPartialPatternCompletion(
    IReadOnlyList<PatternCompletion>? completions)
{
    if (completions == null || completions.Count == 0) return false;
    foreach (var pc in completions)
        if (pc.CompletenessScore < 100) return true;
    return false;
}
```

Sans ce check, `V` seul ou `V x ` (trailing) n'avaient pas de `\square`
dans le `topLatex` lattice (= rendu sans pattern) → `IsIncomplete = false`
→ la popup pouvait se fermer. Avec le check, tant qu'un pattern est
partiel, `IsIncomplete = true` → popup reste ouverte.

### 3. Popup utilise `HintLatex` pour affichage et `PreviewLatex` pour commit

`SuggestionPopupWindow.PrependPatternCompletions` :

- L'entry `AmbiguityAlternative` virtuelle a `Latex = pc.HintLatex`
  (= ce que l'user voit dans la popup, avec carrés).
- L'index dans `_patternCompletions` est encodé via
  `AltIdxPatternBase - patternIndex` (-1000, -1001, ...).
- Au commit, `ResolveCurrentAltIfFocused` retrouve le `pc.PreviewLatex`
  via cet index et l'utilise pour `_resolvedLatex` (= OMath inséré
  dans Word **sans carrés**).

Le sentinel `AltIdxPattern = -200` (P7d) est remplacé par
`AltIdxPatternBase = -1000` pour permettre l'indexation jusqu'à 200
patterns sans risque de collision avec `AltIdxRevert = -1`.

## Tradeoff & alternatives écartées

- **Toujours afficher le carré domain même sans trailing space**.
  Rejeté : pollue le visuel quand l'user ne veut pas de domain (= cas
  `V x` final).
- **Jamais de carré domain sauf click explicite** : rejeté car perd
  l'effet "guide" recherché par l'user.
- **`IsIncomplete = true` tant que un head est présent** (= même `V x R`
  complet reste ouvert) : rejeté. La complétion à score 100 doit fermer
  la popup naturellement, sinon l'user ne peut pas commit sans Ctrl+
  Espace forcé.
- **Style spécial pour `\square`** (= gris/italique) : rejeté pour P5R+,
  WPF MixedLatexRenderer rend `\square` comme `▢` Unicode standard.
  Itération UI possible en P8+ si l'user trouve le rendu peu visible.

## Conséquences

### Code touché

- **Modifié** :
  - `core-csharp/src/MathCursor.Core/Patterns/Templates/ForallBelongsTemplate.cs` —
    helper `HasTrailingWhitespaceAfterArgs`, paramètre `hintDomainExpected`
    propagé via `BuildCompletion` → `BuildLatex`. Branche `else if` dans
    `BuildLatex` pour rendre le carré domain en HintLatex.
  - `core-csharp/src/MathCursor.Core/ZoneResolver.cs` — `Resolve` étendu
    avec `|| HasPartialPatternCompletion(patternCompletions)` dans le
    calcul `incomplete`. Helper `HasPartialPatternCompletion` ajouté.
  - `adapter-vsto/src/MathCursor/UI/SuggestionPopupWindow.cs` —
    `AltIdxPattern (-200)` remplacé par `AltIdxPatternBase (-1000)`.
    `PrependPatternCompletions` utilise `HintLatex` pour `Latex` et
    encode l'index via `AltIdxPatternBase - i`.
    `ResolveCurrentAltIfFocused` check `realAltIdx <= AltIdxPatternBase`,
    décode `patternIndex = AltIdxPatternBase - realAltIdx`, utilise
    `pc.PreviewLatex` pour `_resolvedLatex` (commit-clean).
- **Nouveau tests** :
  - `core-csharp/tests/MathCursor.Core.Tests/Patterns/PatternTrailingHintsTests.cs` —
    10 tests (trailing space hint + IsIncomplete).

### Tests

- **Core** : 1103/1110 verts (post-P5R = 1093/1100). Delta : **+10 nouveaux verts**, 6 préexistants rouges idem.
- **Adapter** : 393/393 verts (référence Core only, pas SuggestionPopupWindow).

### API publique

- `ResolvedZone.IsIncomplete` : sémantique enrichie (= aussi true si
  pattern partiel). Rétro-compat (= un consumer qui utilisait
  IsIncomplete continuera à fermer la popup quand approprié, mais aura
  des cas supplémentaires où c'est true).
- `PatternCompletion.HintLatex` vs `PreviewLatex` : distinction
  désormais user-visible (la popup montre HintLatex, l'OMath final =
  PreviewLatex). Avant P5R+ la popup montrait PreviewLatex aussi (= pas
  de carrés).

### Régression UX

Aucune. P5R+ **améliore** l'UX :
- Plus de fermeture intempestive de la popup à l'espace.
- Carrés guidants pour les slots manquants.
- Commit Enter insère le LaTeX final propre (= sans carrés).

### Règles MC impactées

Aucune.

## Validation post-fix

```bash
# Tests P5R+
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj \
  --filter "FullyQualifiedName~PatternTrailingHints"
# → 10/10 verts

# Suite Core
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 1103/1110 verts (6 préexistants rouges)

# Adapter
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 393/393 verts

# Validation manuelle en P8 via /build-iss :
# 1. Taper "V" → popup avec "∀□ ∈ □" (hint complet)
# 2. Taper "V x" → popup avec "∀x" (carré disparu)
# 3. Taper "V x " (espace) → popup avec "∀x ∈ □"
# 4. Taper "V x R" → popup avec "∀x ∈ ℝ"
# 5. Enter → OMath inséré = ∀x ∈ ℝ (sans carré)
```

## Plan Patterns — état d'avancement

- [x] **P7d** — Popup rendering définitif (commit `1e8f54a`)
- [x] **P5R** — Convention args espace (commit `451d94d`)
- [x] **P5R+** — Trailing hints + IsIncomplete (cet ADR)
- [ ] **P8** — Validation manuelle PAP-friendly via `/build-iss` + Word
- [ ] **P9+** — `LimTemplate`, `SumTemplate`, etc. (héritent ArgListPatternBase)
