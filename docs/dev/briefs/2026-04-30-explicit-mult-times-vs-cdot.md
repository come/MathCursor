# Brief — Multiplication explicite `*` rendue en `×` (et non `·`)

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-30
**Branche :** `lattice-engine`
**Public cible :** agent C# qui ne connaît pas le projet, intervient sur
la couche `core-csharp/src/MathCursor.Core/Lattice/`.

---

## 1. Le besoin

Quand l'utilisateur tape une multiplication **explicite** avec `*` (ex.
`a*b`, `3*4`, `2*pi`), le rendu actuel produit le **point centré**
(`\cdot`, ·) :

- `a*b` → `a · b`
- `3*4` → `3 · 4`
- `2*pi` → `2 · π`

Or au lycée français, **le signe officiel de la multiplication est `×`**
(times, U+00D7). Le `·` (cdot) reste valide en algèbre mais c'est
clairement la convention universitaire ; les profs et élèves attendent
`×` dans les copies (notamment quand on multiplie deux nombres).

**Doctrine cible** :

| Saisie | Rendu actuel | Rendu attendu |
|---|---|---|
| `a*b` | `a · b` | `a × b` |
| `3*4` | `3 · 4` | `3 × 4` |
| `2*pi` | `2 · π` | `2 × π` |
| `x*y*z` | `x · y · z` | `x × y × z` |

**Cas spécial à NE PAS toucher** : le **produit scalaire vectoriel**
(`\vec{a} \cdot \vec{b}`) — `\cdot` est la notation mathématique
correcte du produit scalaire, on la garde.

## 2. État actuel — où ça se joue

**Deux endroits produisent `\cdot` aujourd'hui** :

### 2.1. `LatexRenderer.cs:63` — rendu du `Bin("*", a, b)`

```csharp
if (b.Op == "*") return $"{lhs}\\cdot {rhs}";
```

C'est la **forme canonique** rendue pour tout nœud `Bin` d'opérateur `*`
produit par le parser. Concerne tous les cas : `a*b`, `3*4`, `2*pi`,
`x*y*z` (chaîne récursive).

### 2.2. `AlternativeGenerator.cs:890-891` — règle "lettre `*` lettre"

```csharp
string defaultLatex = $"{la.Value}\\cdot {ra.Value}";
string altLatex = $"\\vec{{{la.Value}}} \\cdot \\vec{{{ra.Value}}}";
```

Génère 2 alternatives pour `a*b` (lettres simples) :
- **default** : produit scalaire littéral (`a · b`) — à modifier
- **alt** : produit scalaire vectoriel (`\vec{a} \cdot \vec{b}`) — à
  garder en `\cdot`

## 3. Modifications proposées

### 3.1. `LatexRenderer.cs:63`

```diff
- if (b.Op == "*") return $"{lhs}\\cdot {rhs}";
+ if (b.Op == "*") return $"{lhs}\\times {rhs}";
```

Une ligne. Couvre tous les cas via le parser canonique.

### 3.2. `AlternativeGenerator.cs:890`

```diff
- string defaultLatex = $"{la.Value}\\cdot {ra.Value}";
+ string defaultLatex = $"{la.Value}\\times {ra.Value}";
  string altLatex = $"\\vec{{{la.Value}}} \\cdot \\vec{{{ra.Value}}}";  // INCHANGÉ
```

L'alternative vec garde `\cdot` (notation produit scalaire).

### 3.3. Vérifier la propagation `\times` → Word OMath

`LatexToUnicodeMath.cs` doit avoir `\\times → ×` dans `LiteralReplacements`.
Si absent, l'ajouter (au même endroit que `\\cdot → ·`) :

```csharp
new KeyValuePair<string, string>("\\cdot", "·"),
new KeyValuePair<string, string>("\\times", "×"),  // si manquant
```

WpfMath supporte `\times` nativement, pas besoin d'adapter dans
`WpfMathAdapter.cs`.

## 4. Tests à ajouter

Dans `core-csharp/tests/MathCursor.Core.Tests/Lattice/` :

- `a*b` → render `a\times b` (default), `\vec{a} \cdot \vec{b}` (alt)
- `3*4` → render `3\times 4`
- `x*y*z` → render `x\times y\times z`
- `2*pi` → render `2\times \pi`
- Ne pas casser le **produit implicite** par adjacence (ex. `2x`, `ab`) :
  ces cas ne passent pas par l'op `*`, ils sont juxtaposés via `Mul`
  implicite — donc pas affectés. Mais ajouter un test sanity.

## 5. Configurabilité — Registry + auto-detect culture

**Décision actée 2026-04-30** : le choix du symbole de multiplication est
**configurable** via Windows Registry et **auto-detecté** depuis la culture
au premier lancement.

### 5.1. Setting

- **Path Registry** : `HKCU\Software\MathCursor\Rendering`
- **Valeur** : `MultiplicationSymbol` (REG_SZ)
- **Valeurs possibles** :
  - `Times` → rendu `\times` (forcé)
  - `Cdot` → rendu `\cdot` (forcé)
  - `Auto` (default) → resolve via `CultureInfo.CurrentUICulture` :
    - FR* (fr-FR, fr-CA, fr-BE...) → `\times`
    - autres → `\cdot`

### 5.2. Architecture core ↔ adapter

Nouvelle classe `RenderOptions` dans `MathCursor.Core` (couche 1, agnostique
de Windows) :

```csharp
public sealed class RenderOptions
{
    public string MultSymbol { get; set; } = ResolveDefault();
    private static string ResolveDefault()
    {
        var c = System.Globalization.CultureInfo.CurrentUICulture;
        return c.TwoLetterISOLanguageName == "fr" ? "\\times " : "\\cdot ";
    }
}
```

L'adapter VSTO (`MathCursor.csproj`) lit le Registry au démarrage et
**override** la default si `Times` ou `Cdot` est explicitement set :

```csharp
// Dans ThisAddIn_Startup ou SuggestionService init
var key = Microsoft.Win32.Registry.CurrentUser
    .OpenSubKey(@"Software\MathCursor\Rendering");
var setting = key?.GetValue("MultiplicationSymbol") as string ?? "Auto";
LatexRenderer.GlobalOptions.MultSymbol = setting switch
{
    "Times" => "\\times ",
    "Cdot" => "\\cdot ",
    _ => LatexRenderer.GlobalOptions.MultSymbol  // Auto = défaut culture
};
```

Tests xUnit save/restore `LatexRenderer.GlobalOptions.MultSymbol` autour de
chaque test qui vérifie un setting spécifique.

## 5.bis. Cas exceptionnels (ne pas appliquer le setting)

Le setting **NE s'applique PAS** aux cas suivants — `\cdot` est forcé :

- **Vec * Vec** (les deux opérandes sont des `Vec` nodes, ex: `vec u *
  vec v`). Convention math du produit scalaire.
- **Cascade `RuleVecDotProduct`** : alt `\vec{a} \cdot \vec{b}` reste en
  `\cdot` (déjà le cas).

Hard-codé dans `LatexRenderer.RenderBin` :

```csharp
if (b.Op == "*" && b.Lhs is Vec && b.Rhs is Vec)
    return $"{lhs}\\cdot {rhs}";
```

## 5.ter. Fix Number-Number juxtaposition (NEW)

**Bug existant** : `2 3` (deux nombres avec espace) est aujourd'hui rendu
`23` (concaténation pure, mathématiquement faux). Avec ce brief, on insère
**le symbole explicite** (selon setting) pour éviter la confusion :

```csharp
// Dans RenderBin pour Bin("*", _, _, implicit=true) :
if (b.Lhs is Atom la && la.Kind == "number"
    && b.Rhs is Atom ra && ra.Kind == "number")
    return $"{lhs}{RenderOptions.MultSymbol}{rhs}";
return $"{lhs}{rhs}";  // cas standard inchangé
```

Concerne `2 3`, `2 3 4` (chaîne), etc. NE concerne PAS `2x` (number+letter
reste concat sans symbole).

## 6. Effort estimé (révisé)

| Tâche | Durée |
|-------|-------|
| `RenderOptions` class + culture detect | 30 min |
| Modif `RenderBin` (3 cas : `.` cdot / `*` setting / `*` vec / number-juxtaposition) | 1 h |
| Adapter VSTO : lecture Registry + injection options | 30 min |
| Ajouter `\\times → ×` dans `LatexToUnicodeMath` | 5 min |
| Tests xUnit (~6 cas couvrant les variations) | 1 h |
| Smoke test Word + Registry | 30 min |
| ADR + commit | 30 min |
| **Total** | **~4 h** |

## 7. Risques / points d'attention

- **`\cdot` en composition (ex. `a \cdot b \cdot c`)** : si quelque chose
  d'autre dans le pipeline génère `\cdot` (Render des fonctions, etc.),
  vérifier que la modif n'affecte pas. Recherche grep complète sur le
  repo : `grep -rn "\\\\\\\\cdot" core-csharp/src/`.
- **Word AutoCorrect** : `*` est parfois auto-corrigé en `•` (bullet) en
  début de paragraphe. Pas notre problème (existe avant ce brief), mais
  noter pour ne pas attribuer un éventuel bug à cette modif.
- **Tests régression NER** : le NER ne voit que le texte source, pas le
  rendu LaTeX → aucun impact.
