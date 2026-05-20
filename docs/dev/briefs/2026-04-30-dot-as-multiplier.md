# Brief — Le point `.` comme opérateur de multiplication

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-30 (révisé après confirmation)
**Branche :** `lattice-engine`
**Public cible :** agent C# qui ne connaît pas le projet, intervient sur
la couche `core-csharp/src/MathCursor.Core/Lattice/`.
**Brief lié :** [`2026-04-30-explicit-mult-times-vs-cdot.md`](2026-04-30-explicit-mult-times-vs-cdot.md)
(rendu `*` configurable, complémentaire de ce brief).

---

## 1. Le besoin et doctrine

L'utilisateur a **trois entrées de saisie** pour la multiplication :

| Saisie utilisateur | Rendu LaTeX | Rendu OMath visuel | Notes |
|--------------------|-------------|--------------------|-------|
| `*` (étoile) | `\times` ou `\cdot` selon **setting culture** | `×` ou `·` | Cf. brief frère, configurable via Registry |
| `.` (point) | `\cdot` (toujours, lecture **littérale** du point) | `·` | Non configurable — `.` = `·` |
| Juxtaposition `ab`, `2x` | rien (concaténation) | `ab`, `2x` | Comportement actuel inchangé pour la plupart des cas |

**Corollaire** : l'utilisateur a la flexibilité — il tape `*` pour utiliser
le symbole de son setting (FR = `×`, anglo = `·`), il tape `.` pour forcer
le centered dot quelque soit son setting.

**Cas exceptionnels (forcent `\cdot` indépendamment du setting)** :

- **Vec · Vec** (les deux opérandes sont des `Vec` nodes) → toujours
  `\cdot`. Convention math du produit scalaire. S'applique à `vec a * vec
  b` ET `vec a . vec b`.
- **Cascade `RuleVecDotProduct`** (alt sur `a*b` lettre simple) : alt
  reste `\vec{a} \cdot \vec{b}` indépendamment du setting.

**Cas qui change avec ce brief (NOUVEAU vs comportement actuel)** :

- **Juxtaposition `nombre nombre`** (ex: `2 3`) actuellement rendue `23`
  (concaténation, **bug**). Désormais : insérer le symbole **explicite**
  selon le setting. `2 3` → `2 \times 3` ou `2 \cdot 3`.

## 2. Spec syntaxe — point comme multiplicateur

### 2.1. Cas positifs (`.` = mult, rendu `\cdot`)

Le `.` est un opérateur de multiplication entre deux opérandes. Suit les
**mêmes règles d'associativité et de tightness que `*`** (cf. ADR
`Feat-asterisk-tightness-associativity`) :

- `.` tight (collé) → gauche-assoc PEMDAS
- `.` loose (espace) → droite-récursive
- Flip d'associativité exposé en cascade `RuleTightChainExtension`

| Saisie | Tightness | Default | Alt désambig |
|--------|-----------|---------|--------------|
| `a.b` | n/a (atomes) | `a \cdot b` | `\vec{a} \cdot \vec{b}` (cascade vec dot product) |
| `a.b/3` | tight | `\frac{a \cdot b}{3}` | `a \cdot \frac{b}{3}` |
| `a .b/3` | loose | `a \cdot \frac{b}{3}` | `\frac{a \cdot b}{3}` |
| `1/2.3/4` | tight | `\frac{(1/2)\cdot 3}{4}` | `\frac{1}{2} \cdot \frac{3}{4}` |
| `1/2 . 3/4` | loose | `\frac{1}{2} \cdot \frac{3}{4}` | `\frac{(1/2)\cdot 3}{4}` |
| `cos(x).sin(x)` | tight | `\cos(x) \cdot \sin(x)` | (cascade flip) |
| `vec u . vec v` | tight | `\vec{u} \cdot \vec{v}` | — (déjà la forme canonique) |

### 2.2. Cas neutres (le `.` n'est PAS un mult)

Le `.` ne doit PAS être traité comme mult quand :

- **Fin de phrase / ponctuation** : `.` final + espace + majuscule
  (= début de phrase). Hors scope MathCursor (le span Ctrl+Espace
  s'arrête à la ponctuation).
- **Préfixe d'un nombre seul** (`.5`) : pas une notation FR, hors scope V1.

## 3. Désambig — décimal anglo `3.4`

### 3.1. Le conflit

Les utilisateurs habitués au clavier numérique anglo tapent `3.4` pour
"trois virgule quatre" (= `3,4` en FR). En notation FR pure, c'est
`3 × 4`. Le brief acte **mult par défaut** + alt décimal en cascade.

### 3.2. Stratégie V1

| Saisie | Default | Alt désambig (`RuleDecimalVsMultiplication`) |
|--------|---------|-----------------------------------------------|
| `3.4` | `3 \cdot 4` | `3{,}4` (décimal) |
| `3.14` | `3 \cdot 14` | `3{,}14` |
| `0.5` | `0 \cdot 5` | `0{,}5` |
| `a.b` | `a \cdot b` | (pas d'alt décimal — pas chiffre.chiffre) |
| `2.x` | `2 \cdot x` | (pas d'alt — `x` non chiffre) |
| `x.2` | `x \cdot 2` | (pas d'alt — `x` non chiffre) |

**Critère de déclenchement** : `\d+\.\d+` strict avec les **deux côtés
purement numériques**.

### 3.3. Sticky preference (V2)

Si l'utilisateur choisit "décimal" sur un `3.4` dans une session, mémoriser
pour ré-appliquer aux autres `\d+\.\d+` de la même formule. Mécanique de
préférence côté `AlternativeGenerator` (RuleId partagé).

## 4. Architecture impactée

### 4.1. Lexer

**Aujourd'hui** (à vérifier dans `Lattice/Lexer.cs`) : un nombre comme
`3.4` est probablement tokenisé en **un seul** Number (`3.4`) — comportement
décimal anglo natif.

**À modifier** :
- Tokeniser `3.4` en **3 tokens** : `Number(3)`, `Op(.)`, `Number(4)`. Le
  `.` devient un Op à part entière, comme `*`.
- La tightness du `.` est calculée comme pour les autres Ops (positions
  des bornes adjacentes).
- **Conséquence directe** : on perd le décimal `3.4` comme number unique.
  Récupéré en alt via `RuleDecimalVsMultiplication` (mutation source
  `3.4` → `3,4` ou directement render `3{,}4`).

### 4.2. Parser

`.` traité **comme `*`** dans `ParseTerm`. Branche `IsOp("*")` étendue à
`IsOp("*", ".")`. Tightness, associativité, flip alt : identiques.

### 4.3. AST — distinction `.` vs `*` requise

⚠️ **Changement par rapport à V0 du brief** : puisque `.` et `*` rendent
DIFFÉREMMENT (`\cdot` toujours pour `.` ; `\times` ou `\cdot` selon
setting pour `*`), l'AST doit les distinguer.

**Solution** : utiliser le champ `Op` du `Bin` existant.
- Saisie `*` → `Bin("*", lhs, rhs, tight, implicit=false)`
- Saisie `.` → `Bin(".", lhs, rhs, tight, implicit=false)`
- Juxtaposition → `Bin("*", lhs, rhs, tight, implicit=true)` (inchangé)

Pas de nouveau nœud, juste une extension du dictionnaire d'`Op` valides.

### 4.4. LatexRenderer

**Modifications dans `RenderBin`** :

```csharp
// Multiplication explicite : `.` toujours \cdot, `*` selon setting
if (b.Op == "." && !b.Implicit) return $"{lhs}\\cdot {rhs}";
if (b.Op == "*" && !b.Implicit)
{
    // Vec*Vec → toujours \cdot (convention produit scalaire)
    if (b.Lhs is Vec && b.Rhs is Vec) return $"{lhs}\\cdot {rhs}";
    // Sinon : symbole selon RenderOptions (setting culture)
    return $"{lhs}{RenderOptions.MultSymbol}{rhs}";
}
// Mult implicite (juxtaposition) :
if (b.Op == "*" && b.Implicit)
{
    // Number-Number → forcer le symbole (sinon "23" au lieu de "2×3")
    if (b.Lhs is Atom la && la.Kind == "number"
        && b.Rhs is Atom ra && ra.Kind == "number")
        return $"{lhs}{RenderOptions.MultSymbol}{rhs}";
    // Cas standard : concaténation visuelle (rien)
    return $"{lhs}{rhs}";
}
```

Où `RenderOptions.MultSymbol` est `"\\times "` ou `"\\cdot "` selon le
setting.

### 4.5. RenderOptions — config injection

**Nouveau dans le core** : `RenderOptions` (struct/class) injecté dans
`LatexRenderer` ou passé en paramètre. Champs :

```csharp
public sealed class RenderOptions
{
    public string MultSymbol { get; set; } = "\\cdot ";  // default safe
    public static RenderOptions Default { get; } = new RenderOptions();
}
```

L'**adapter VSTO** lit le Registry au démarrage et configure les options :

- Registry path : `HKCU\Software\MathCursor\Rendering`
- Valeur : `MultiplicationSymbol` (`Times` | `Cdot` | `Auto`)
- Default `Auto` → resolve via `CultureInfo.CurrentUICulture` :
  - FR* → `\times`
  - autres → `\cdot`

**Le core reste agnostique** de Windows (Règle dure CLAUDE.md). Il reçoit
juste les options résolues.

API à exposer :

```csharp
// Dans LatticeEngine ou nouvelle façade :
public IReadOnlyList<LatexSuggestion> Convert(string rawSpan, RenderOptions options);
```

Si on ne veut pas modifier la signature : statique configuré au démarrage
côté adapter (`LatexRenderer.GlobalOptions = ...`). Plus simple, suffisant
en V1.

### 4.6. AlternativeGenerator

**Nouvelle règle** `RuleDecimalVsMultiplication` :

- Scan source pour pattern `\d+\.\d+` au top-level (pas dans les
  intervalles ni dans les groupes parens).
- Si match → ajouter Spot avec :
  - Default = mult (LaTeX top du parser : `3 \cdot 4`)
  - Alt = décimal : LaTeX `3{,}4` (substitution string), pas de mutation
    source (l'utilisateur peut accepter sans changer ce qu'il a tapé).

**Priorité** : 3 (entre vec-dot-product et tight-chain-extension).

**Cascade vec dot product** : étendre pour matcher aussi `Bin(".", ...)`
en plus de `Bin("*", ...)`. Le code actuel :

```csharp
if (node is Bin b && b.Op == "*" && !b.Implicit ...)
```

devient :

```csharp
if (node is Bin b && (b.Op == "*" || b.Op == ".") && !b.Implicit ...)
```

### 4.7. LatexToUnicodeMath

Vérifier la présence de `\\times → ×` dans `LiteralReplacements`. Si
absent (probablement le cas), ajouter :

```csharp
new KeyValuePair<string, string>("\\times", "×"),
```

`\\cdot → ·` est déjà présent (cf. brief frère §3.3).

### 4.8. Mode édition revert (Ctrl+E)

**Aucun changement.** Le revert utilise la **source brute** stockée dans
le CustomXMLPart côté adapter, pas le LaTeX rendu. Donc :
- User tape `a*b` → rendu `a \times b` → OMath inséré
- Ctrl+E → revert au texte source `a*b` (intact)
- Idem pour `.` → revert à `a.b`

Test d'intégration adapter à ajouter pour pinner cet invariant après le
changement de rendering.

### 4.9. NER

Le NER voit le texte source. Si le corpus contient `3.4` interprété
auparavant comme un nombre décimal unique, il faut **vérifier que les
fixtures NER ne se cassent pas**. Actions :
- Re-générer le corpus si certains spans changent.
- Ou marquer ce brief comme déclencheur d'un re-train mineur (cf. brief
  NER `2026-04-29-ner-distilmult-adoption.md`).

## 5. Cas de test obligatoires (xUnit)

### 5.1. LatexRendererTests (couche a)

```csharp
// Default config (assume MultSymbol = "\\times " pour FR)
[Fact] public void Dot_letter_letter_renders_cdot_always()
    => Assert.Equal("a\\cdot b", RenderTop("a.b"));

[Fact] public void Dot_number_number_renders_cdot_default()
    => Assert.Equal("3\\cdot 4", RenderTop("3.4"));

[Fact] public void Dot_chain_left_assoc()
    => Assert.Equal("a\\cdot b\\cdot c", RenderTop("a.b.c"));

[Fact] public void Dot_with_paren_unchanged()
    => Assert.Equal("\\left(a+b\\right)\\cdot c", RenderTop("(a+b).c"));

[Fact] public void Dot_func_renders_cdot()
    => Assert.Equal(
        "\\cos\\left(x\\right)\\cdot \\sin\\left(x\\right)",
        RenderTop("cos(x).sin(x)"));

// Tightness alignée sur `*` (cf. ADR asterisk-tightness-associativity)
[Fact] public void Dot_tight_left_assoc_pemdas()
    => Assert.Equal("\\frac{a\\cdot b}{3}", RenderTop("a.b/3"));

[Fact] public void Dot_loose_right_recursive()
    => Assert.Equal("a\\cdot \\frac{b}{3}", RenderTop("a .b/3"));

// Vec . Vec → forcé \cdot (s'applique aussi à *)
[Fact] public void Vec_dot_vec_forces_cdot_via_dot()
    => Assert.Equal("\\vec{u}\\cdot \\vec{v}", RenderTop("vec u . vec v"));

[Fact] public void Vec_star_vec_forces_cdot_independently_of_setting()
{
    // Même avec MultSymbol = "\\times ", les vec*vec restent en \cdot
    var prev = LatexRenderer.GlobalOptions.MultSymbol;
    LatexRenderer.GlobalOptions.MultSymbol = "\\times ";
    try
    {
        Assert.Equal("\\vec{u}\\cdot \\vec{v}", RenderTop("vec u * vec v"));
    }
    finally { LatexRenderer.GlobalOptions.MultSymbol = prev; }
}

// Number-number juxtaposition → symbole explicite (NEW)
[Fact] public void Number_juxtaposition_uses_explicit_mult_symbol()
{
    var prev = LatexRenderer.GlobalOptions.MultSymbol;
    LatexRenderer.GlobalOptions.MultSymbol = "\\times ";
    try
    {
        Assert.Equal("2\\times 3", RenderTop("2 3"));
    }
    finally { LatexRenderer.GlobalOptions.MultSymbol = prev; }
}
```

### 5.2. AlternativeGeneratorTests (couche b)

```csharp
[Fact]
public void Dot_number_pair_proposes_decimal_alt()
{
    var r = _engine.ConvertWithAmbiguity("3.4");
    Assert.Equal("3\\cdot 4", r.TopLatex);
    Assert.NotNull(r.Spot);
    Assert.Equal(AlternativeGenerator.RuleDecimalVsMultiplication, r.Spot!.RuleId);
    var alts = r.Spot.Alternatives.Select(a => a.Latex).ToList();
    Assert.Contains("3{,}4", alts);
}

[Fact]
public void Dot_letter_pair_proposes_vec_dot_alt()
{
    // a.b déclenche la cascade vec dot product (extension à Bin("."))
    var r = _engine.ConvertWithAmbiguity("a.b");
    Assert.Equal("a\\cdot b", r.TopLatex);
    var alts = r.Spot?.Alternatives.Select(a => a.Latex).ToList();
    Assert.Contains("\\vec{a} \\cdot \\vec{b}", alts ?? new System.Collections.Generic.List<string>());
}

[Fact]
public void Dot_letter_pair_no_decimal_alt()
{
    var r = _engine.ConvertWithAmbiguity("a.b");
    if (r.Spot != null)
        Assert.NotEqual(AlternativeGenerator.RuleDecimalVsMultiplication, r.Spot.RuleId);
}
```

### 5.3. LatexToUnicodeMathTests (couche c)

```csharp
[InlineData("\\times", "×")]
[InlineData("a\\times b", "a×b")]
[InlineData("3\\times 4", "3×4")]
public void Times_command_maps_to_x_symbol(string latex, string expected) ...
```

### 5.4. Anti-régression

- `2x` (mult implicite ident-ident, pas number-number) inchangé →
  toujours rendu `2x` sans symbole.
- `ab` (deux idents) inchangé → toujours `ab`.
- `a*b`, `3*4` (mult `*` explicite) inchangé pour le contrat parser ;
  rendu change selon setting (cf. brief frère).
- Tests `Convert_NER` du corpus → vérifier que `3.4` dans une phrase ne
  casse pas le span. Re-tokenisation peut affecter les positions.

## 6. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `core-csharp/src/MathCursor.Core/Lattice/Lexer.cs` | Tokenisation `.` comme Op + arrêt du Number sur `.` |
| `core-csharp/src/MathCursor.Core/Lattice/Parser.cs` | `IsOp("*", ".")` dans ParseTerm |
| `core-csharp/src/MathCursor.Core/Lattice/LatexRenderer.cs` | RenderBin : 3 cas (`.`, `*` non-vec, `*` vec) |
| `core-csharp/src/MathCursor.Core/RenderOptions.cs` | NOUVEAU — config rendering injectable |
| `core-csharp/src/MathCursor.Core/Lattice/AlternativeGenerator.cs` | Étendre cascade vec à `Bin(".")` + nouvelle `RuleDecimalVsMultiplication` |
| `core-csharp/src/MathCursor.Core/LatexToUnicodeMath.cs` | Ajouter `\\times → ×` si manquant |
| `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` ou similaire | Lire Registry au démarrage, configurer `LatexRenderer.GlobalOptions` |
| `docs/dev/briefs/2026-04-30-explicit-mult-times-vs-cdot.md` | Brief frère, à séquencer avant ce brief |

## 7. Ce qu'il NE faut PAS faire

- ❌ Rendre `.` configurable : `.` = `\cdot` toujours (lecture littérale).
- ❌ Mélanger `.` et `*` dans le même `Bin` Op : ils rendent différemment,
  l'AST doit distinguer (`Bin(".")` vs `Bin("*")`).
- ❌ Casser le décimal anglo silencieusement : alt décimal proposée pour
  `\d+\.\d+`, pas pour `\w+\.\d+` ni `\d+\.\w+`.
- ❌ Étendre la règle aux intervalles : `[0.5, 1]` n'est pas à toucher
  en V1 (la virgule sépare déjà low/high, ajouter `.5` casserait).
- ❌ Toucher au core depuis le code Windows : `RenderOptions` est dans le
  core, l'adapter VSTO le configure via une API.
- ❌ Bypass setting pour Vec : Vec*Vec ou Vec.Vec → toujours `\cdot`,
  hard-codé dans LatexRenderer (test xUnit dédié).

## 8. Validation

1. `dotnet build MathCursor.sln` → 0 erreur, 0 warning.
2. `dotnet test core-csharp/tests/` → tests existants verts + nouveaux §5.
3. Test manuel pipeline complet :
   - Setting Auto + culture FR → `a*b` rend `a \times b`, `a.b` rend `a \cdot b`.
   - Setting Auto + culture EN → `a*b` rend `a \cdot b`, `a.b` rend `a \cdot b`.
   - Setting forcé Cdot → `a*b` rend `a \cdot b`.
   - Setting forcé Times → `a*b` rend `a \times b`.
   - `vec u * vec v` rend `\vec{u} \cdot \vec{v}` quel que soit le setting.
   - `2 3` rend `2 \times 3` ou `2 \cdot 3` selon setting.
   - `3.4` rend `3 \cdot 4` par défaut, popup propose `3{,}4`.
4. Test manuel Word :
   - Modifier le Registry à la main, redémarrer Word, vérifier le
     changement de rendu.
5. ADR créé : `docs/dev/decisions/2026-04-XX-Feat-dot-as-multiplier.md`.

## 9. Estimation

| Tâche | Durée |
|-------|-------|
| Lecture Lexer + tokenisation Number actuelle | 30 min |
| Modif Lexer (`.` Op + Number stop on `.`) | 1-2 h |
| Modif Parser (alias `*` → `*`/`.`) | 30 min |
| Création `RenderOptions` + injection LatexRenderer | 1 h |
| Modif `RenderBin` (3 cas + Vec detection + Number juxtaposition) | 1-2 h |
| AlternativeGenerator (extension cascade vec + nouvelle décimal) | 2 h |
| Lecture Registry côté adapter VSTO | 30 min |
| Tests xUnit (~15 cas) | 2 h |
| ADR + commit propre | 30 min |
| **Total V1** | **~9-11 h ≈ 1.5 jours** |

## 10. Phasing

**Dépendance forte** : ce brief consomme `RenderOptions` qui doit aussi
servir au brief frère `explicit-mult-times-vs-cdot`. Recommandation :

- **Phase 0** : créer `RenderOptions` + injection LatexRenderer (commun aux
  deux briefs).
- **Phase 1** : appliquer brief frère (rendu `*` configurable) — vérifier
  setting Times/Cdot/Auto + Registry.
- **Phase 2** : appliquer ce brief (`.` comme entrée, mêmes règles
  parser, rendu fixe `\cdot`, alt décimal).
- **Phase 3** : tests d'intégration adapter (revert, registry, culture).

Si on veut tout livrer ensemble, ~1.5-2 jours.

## 11. Décisions actées

Suite à validation utilisateur du 2026-04-30 :

1. **Vec · Vec forcé `\cdot`** : OUI, hard-codé dans LatexRenderer pour
   les deux opérateurs `*` et `.`, indépendamment du setting.
2. **`2 3` (Number juxtaposition)** : insérer le symbole explicite (selon
   setting). Fix d'un bug existant (rendu actuellement `23` collés).
3. **Storage setting** : Windows Registry `HKCU\Software\MathCursor\
   Rendering\MultiplicationSymbol` (`Times` | `Cdot` | `Auto`).
4. **Mode édition revert** : utilise le texte source brut (déjà OK).
5. = (1).
6. **Couche OMath** : ajouter `\\times → ×` dans `LiteralReplacements`
   si manquant.

> Citations utilisateur :
> - "peux tu forcer que vec * vec ou vec.vec est toujours un cdot ?"
> - "2 3 (mult * et regle culture)"
> - "registry"
> - "revert recuperer le brut !"
