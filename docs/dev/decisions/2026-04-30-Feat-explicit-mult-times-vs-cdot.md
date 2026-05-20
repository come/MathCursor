# Feat — Multiplication explicite `*` rendue selon culture (`×` ou `·`)

**Date :** 2026-04-30
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Brief :** [`2026-04-30-explicit-mult-times-vs-cdot.md`](../briefs/2026-04-30-explicit-mult-times-vs-cdot.md)

## Décision

Le rendu LaTeX de la multiplication explicite `*` est **configurable** :

- **Default culture-aware** : `\times` pour FR (cible lycée français),
  `\cdot` pour autres cultures.
- **Override Registry** : `HKCU\Software\MathCursor\Rendering\
  MultiplicationSymbol` (`Times` | `Cdot` | `Auto`). L'adapter VSTO lit
  cette valeur au démarrage.
- **API core** : `MathCursor.Core.RenderOptions` injecté via
  `LatexRenderer.GlobalOptions` (statique). Le core reste agnostique de
  Windows.

**Cas exceptionnels (forcent `\cdot` indépendamment du setting)** :
1. **Vec * Vec** : convention math du produit scalaire vectoriel.
2. **Cascade `RuleVecDotProduct`** : alt `\vec{a} \cdot \vec{b}` reste
   `\cdot`.
3. **Mult typée `.`** (cf. ADR `Feat-dot-as-multiplier`) : `\cdot` toujours.

**Fix bonus — Number juxtaposition `2 3`** : auparavant rendu `23` (concat
faux mathématiquement), désormais rendu avec le symbole explicite (`2 ×
3` ou `2 · 3`).

## Pourquoi

### Convention scolaire FR

Au lycée français, le signe officiel de la multiplication est `×`. Les
profs et élèves attendent ce signe dans leurs copies. `\cdot` (point
centré) reste valide en algèbre abstraite mais c'est la convention
universitaire / anglo-saxonne.

### Configurabilité

Le choix `×` vs `·` reste culturel. Pour ne pas pénaliser les utilisateurs
hors FR (dévs, profs universitaires, etc.), on rend le setting modifiable
via Registry. La culture initiale fixe le default, l'utilisateur peut
override.

### Architecture core ↔ adapter

Le core (`MathCursor.Core`, .NET Standard 2.0) reste agnostique de
Windows comme imposé par CLAUDE.md. L'adapter VSTO lit le Registry et
configure `LatexRenderer.GlobalOptions` au démarrage. Le core n'utilise
que `CultureInfo.CurrentUICulture` pour le default culture-aware
(disponible cross-platform).

### Vec * Vec forcé

Le produit scalaire vectoriel `\vec{a} \cdot \vec{b}` est une convention
math stricte. Forcer `×` ici romprait avec la pratique enseignée
(produit scalaire ≠ produit vectoriel `\times`). Hard-codé dans
`LatexRenderer.RenderBin`.

### Number juxtaposition

`2 3` rendait `23` collés, ce qui est mathématiquement faux. Avec ce
brief, la mult implicite Number-Number force le symbole explicite (`2 ×
3` ou `2 · 3`). Cas trivial à détecter (deux Atom("number") adjacents
dans `Bin("*", _, _, implicit=true)`).

## Conséquences

### Code (couche 1 — core)

- **Nouveau fichier** `core-csharp/src/MathCursor.Core/RenderOptions.cs` :
  classe avec `MultSymbol` (default culture-aware via
  `CultureInfo.CurrentUICulture.TwoLetterISOLanguageName`).
- **`LatexRenderer.cs`** :
  - Statique `GlobalOptions` exposé.
  - `RenderBin` modifié : 4 branches pour mult :
    1. `Bin(".")` → `\cdot ` toujours (cf. ADR Feat-dot-as-multiplier).
    2. `Bin("*", _, _, implicit=true)` Number-Number → symbole explicite.
    3. `Bin("*")` Vec*Vec → `\cdot ` forcé.
    4. `Bin("*")` standard → `GlobalOptions.MultSymbol`.
- **`AlternativeGenerator.cs`** :
  - `MatchAmbiguity` (RuleVecDotProduct) : `defaultLatex` aligné sur
    `GlobalOptions.MultSymbol` pour matcher le rendu actuel dans le
    `topLatex`.

### Code (couche 3 — adapter VSTO)

À ajouter (hors scope de cette session, à faire avant release) :

```csharp
// Dans ThisAddIn_Startup ou SuggestionService init
var key = Microsoft.Win32.Registry.CurrentUser
    .OpenSubKey(@"Software\MathCursor\Rendering");
var setting = key?.GetValue("MultiplicationSymbol") as string;
if (setting == "Times")
    LatexRenderer.GlobalOptions.MultSymbol = "\\times ";
else if (setting == "Cdot")
    LatexRenderer.GlobalOptions.MultSymbol = "\\cdot ";
// Sinon (`Auto` ou absent) : default culture-aware utilisé.
```

### Tests

- `LatexRendererTests` : 7 tests adaptés/ajoutés
  (`Bin_explicit_mult_uses_times`, `Vec_times_vec_forces_cdot_*`,
  `Number_times_number_juxtaposition_uses_explicit_symbol`,
  `Mult_setting_cdot_renders_with_cdot`).
- `AlternativeGeneratorTests` : tests vec dot product mis à jour pour
  matcher `\times ` dans le topLatex.
- **Sérialisation** des tests qui touchent `GlobalOptions` via
  `[Collection(GlobalOptionsTestCollection.Name)]` pour éviter les
  races xUnit parallel.
- `LatexToUnicodeMathTests` : conversion `\times → ×` pinée (déjà
  présente dans `LiteralReplacements`).

### Tests à ajouter post-VSTO

- Test d'intégration : registry override → setting appliqué.
- Test culture EN : default = `\cdot`.

### Hors scope V1

- ❌ UI Settings panel : reportée au brief
  `2026-04-30-ribbon-dedicated-tab-with-examples.md` ("Préférences
  notation"). Pour l'instant, modification manuelle du Registry.
- ❌ Setting per-document : un seul setting global utilisateur.
- ❌ Migration des documents existants : les OMath déjà créés ne sont
  pas re-rendus.

## Validé par l'utilisateur

Demande initiale (brief frère du 30-04) :

> "Au lycée français, le signe officiel de la multiplication est `×`."

Configurabilité + culture detect + Registry :

> "alors ce que je veux c'est si il tape * => x ou . selon un settings
> (aujourd'hui si tu peux recuperer la culture pour determiner la valeur
> de ce settings)"

Vec*Vec forcé + Number juxtaposition + Registry storage :

> "1. peux tu forcer que vec * vec ou vec.vec est toujours un cdot ?"
> "2. 2 3 (mult * et regle culture)"
> "3. registry"

Autorisation de coder :

> "oui et go dans la foulée sur les deux briefs"

## Statut

acté
