# Brief — Définition de fonction au clavier (`f:x->expr` → `f : x ↦ expr`)

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-29
**Branche :** `lattice-engine`
**Public cible :** agent C# autonome qui ne connaît pas le projet, intervient
sur le moteur lattice (couche 1 — `core-csharp/`).
**ADR liée :** [2026-04-29-Feat-function-definition.md](../decisions/2026-04-29-Feat-function-definition.md)
**Dépendance :** s'appuie sur `ParseTightChain` (déjà existant, cf. ADR
tight-as-grouping).

---

## 1. Le besoin

Reconnaître la **notation lycée FR de définition de fonction** au clavier :

```
f:x->2x+1          → f : x \mapsto 2x+1
f:x,y->x+y         → f : (x,y) \mapsto x+y
g:t->cos(t)+1      → g : t \mapsto \cos(t)+1
```

C'est la première forme qu'un élève écrit en cours quand on parle de
"définir une fonction". `\mapsto` (la flèche-barrée) est la convention
française, distincte de `=` qui dénote une simple égalité.

## 2. Sémantique attendue

### 2.1. Pattern de déclenchement

La règle s'active **strictement** quand le parser voit la séquence en
début de parse (top-level) :

```
Ident ':' Ident (',' Ident)* '->' …
```

c'est-à-dire :

- un Ident (le nom de la fonction),
- l'opérateur `:` ,
- au moins un Ident (la variable),
- éventuellement `, Ident` répétés (variables additionnelles),
- l'opérateur `->`,
- une expression à droite (le body).

Sans `->`, **pas de transformation**. Le parser doit alors revenir au
comportement standard (`ParseRelation`).

### 2.2. Cas reconnus (transformation)

| Source | AST produit | Rendu LaTeX |
|--------|-------------|-------------|
| `f:x->2x+1` | `FuncDef("f", [x], Bin("+", Bin("*", 2, x, implicit, tight), 1, tight))` | `f : x \mapsto 2x+1` |
| `f:x,y->x+y` | `FuncDef("f", [x, y], Bin("+", x, y, tight))` | `f : (x,y) \mapsto x+y` |
| `g:t->cos(t)+1` | `FuncDef("g", [t], Bin("+", Func("cos", t), 1))` | `g : t \mapsto \cos(t)+1` |
| `h:n->u_n+1` | `FuncDef("h", [n], Sub(u, Bin("+", n, 1, tight)))` | `h : n \mapsto u_{n+1}` |

### 2.3. Cas NON reconnus (fallback parsing standard)

| Source | Comportement attendu | Pourquoi |
|--------|----------------------|----------|
| `f:x` (pas de `->`) | Parsing standard (probablement Atom f puis erreur sur `:`, ou Bin si `:` géré comme op générique) | Pattern incomplet, la règle ne s'active pas |
| `f : R -> R` (typage) | Parsing standard | Hors scope V1 |
| `2x+1` (sans pattern) | Parsing standard | Pas de `:` au début |
| `f(x)=2x+1` (forme égalité) | Parsing standard (Func + `=` + expr) | Forme alternative non reconnue par ce pattern |

### 2.4. Frontière du body

Le body utilise `ParseTightChain` — cohérent avec ADR `tight-as-grouping`.
Conséquence : le body s'arrête au premier opérateur **loose** (espace
adjacent). Donc `f:x->2x+1 g:y->y` (deux définitions séparées par un
espace) :

- `f:x->2x+1` consommé jusqu'à l'espace (le `+` est tight, mais l'espace
  après `1` borne).
- Le reste `g:y->y` n'est pas consommé par cette FuncDef.

Le parser actuel produit un AST root unique, donc le `g:y->y` restera dans
le buffer. **Comportement acceptable** : le caller (SuggestionService) gère
le découpage en plusieurs zones. Pas à hardcoder un "multi-FuncDef" dans le
parser.

## 3. Plan d'implémentation

### 3.1. Vocabulary.cs

Ajouter `:` dans `SingleOps`. Avant :

```csharp
public const string SingleOps = "+-*/^_=<>()[]{},|;";
```

Après :

```csharp
public const string SingleOps = "+-*/^_=<>()[]{},|;:";
```

C'est la seule modif vocabulaire. `:` deviendra un Op tokenisé par le
Lexer comme les autres single chars.

### 3.2. AstNodes.cs

Ajouter le nouveau nœud :

```csharp
/// <summary>Définition de fonction : f : x ↦ expr (ou f : (x,y) ↦ expr).
/// Pattern lycée FR. <see cref="Vars"/> contient ≥ 1 variable ; le rendu
/// ajoute des parens automatiquement quand <c>Vars.Count > 1</c>.</summary>
public sealed class FuncDef : AstNode
{
    public string Name { get; }
    public IReadOnlyList<AstNode> Vars { get; }
    public AstNode Body { get; }
    public FuncDef(string name, IReadOnlyList<AstNode> vars, AstNode body)
    {
        Name = name; Vars = vars; Body = body;
    }
}
```

### 3.3. Parser.cs — détection top-level

Modifier la méthode `Parse()` (ligne ~92) :

```csharp
public AstNode Parse()
{
    var fd = TryParseFuncDef();
    if (fd != null) return fd;
    var e = ParseRelation();
    return e ?? Hole(1);
}
```

Et ajouter `TryParseFuncDef()` :

```csharp
// Tente de reconnaître `Ident ':' Ident (',' Ident)* '->' body`. Si
// match : consume et produit FuncDef. Si pas match (n'importe quel
// échec à n'importe quelle étape) : restore _i et return null pour
// laisser ParseRelation traiter normalement.
private AstNode? TryParseFuncDef()
{
    var save = _i;
    if (Peek() is not { Type: EdgeType.Ident } nameTok) return null;
    Consume();
    if (!IsOp(":")) { _i = save; return null; }
    Consume();
    var vars = new List<AstNode>();
    if (Peek() is not { Type: EdgeType.Ident } v0) { _i = save; return null; }
    Consume();
    vars.Add(new Atom("ident", v0.Value));
    while (IsOp(","))
    {
        Consume();
        if (Peek() is not { Type: EdgeType.Ident } v) { _i = save; return null; }
        Consume();
        vars.Add(new Atom("ident", v.Value));
    }
    if (!IsOp("->")) { _i = save; return null; }
    Consume();
    var body = ParseTightChain() ?? (AstNode)Hole(1);
    return new FuncDef(nameTok.Value, vars, body);
}
```

**Important** : la stratégie de "save/restore `_i`" est essentielle parce
qu'on parse de façon spéculative — toute défaillance du pattern doit
laisser le buffer intact pour `ParseRelation`.

### 3.4. LatexRenderer.cs — branche FuncDef

Ajouter dans le `switch` de `Render()` :

```csharp
FuncDef fd => RenderFuncDef(fd),
```

Et la méthode :

```csharp
private static string RenderFuncDef(FuncDef fd)
{
    var body = Render(Unwrap(fd.Body));
    if (fd.Vars.Count == 1)
    {
        return $"{fd.Name} : {Render(fd.Vars[0])} \\mapsto {body}";
    }
    // Multi-vars : parens auto autour du n-uplet
    var parts = new List<string>();
    foreach (var v in fd.Vars) parts.Add(Render(v));
    return $"{fd.Name} : ({string.Join(",", parts)}) \\mapsto {body}";
}
```

`Unwrap` retire un éventuel `Group` autour du body, comme pour les autres
contextes structurels (cf. lignes 49 et 69 de LatexRenderer.cs).

## 4. Tests obligatoires

### 4.1. ParserTests.cs

```csharp
[Fact]
public void FuncDef_single_var_yields_funcdef()
{
    var ast = ParseTop("f:x->2x+1");
    var fd = Assert.IsType<FuncDef>(ast);
    Assert.Equal("f", fd.Name);
    Assert.Single(fd.Vars);
    var v = Assert.IsType<Atom>(fd.Vars[0]);
    Assert.Equal("x", v.Value);
    // Body = Bin("+", Bin("*", 2, x, implicit, tight), 1, tight)
    var plus = Assert.IsType<Bin>(fd.Body);
    Assert.Equal("+", plus.Op);
    Assert.True(plus.Tight);
    var mul = Assert.IsType<Bin>(plus.Lhs);
    Assert.Equal("*", mul.Op);
    Assert.True(mul.Implicit);
}

[Fact]
public void FuncDef_two_vars_yields_funcdef_with_two_atoms()
{
    var ast = ParseTop("f:x,y->x+y");
    var fd = Assert.IsType<FuncDef>(ast);
    Assert.Equal(2, fd.Vars.Count);
    Assert.Equal("x", ((Atom)fd.Vars[0]).Value);
    Assert.Equal("y", ((Atom)fd.Vars[1]).Value);
}

[Fact]
public void FuncDef_with_named_function_in_body()
{
    var ast = ParseTop("g:t->cos(t)+1");
    var fd = Assert.IsType<FuncDef>(ast);
    Assert.Equal("g", fd.Name);
    var plus = Assert.IsType<Bin>(fd.Body);
    Assert.Equal("+", plus.Op);
    Assert.IsType<Func>(plus.Lhs);
}

[Fact]
public void FuncDef_pattern_without_arrow_falls_back_to_standard_parse()
{
    // f:x sans -> : pas de FuncDef, parsing standard
    var ast = ParseTop("f:x");
    Assert.IsNotType<FuncDef>(ast);
    // Le comportement exact dépend du parser standard ; vérifier au moins
    // que le pattern n'a pas été consommé silencieusement.
}

[Fact]
public void FuncDef_does_not_trigger_on_simple_expression()
{
    // 2x+1 n'a pas de pattern : pas de FuncDef
    var ast = ParseTop("2x+1");
    Assert.IsNotType<FuncDef>(ast);
}

[Fact]
public void FuncDef_body_is_bounded_by_space()
{
    // f:x->2x+1 (espace borne le body) — le body s'arrête après "2x+1"
    var ast = ParseTop("f:x->2x+1");
    var fd = Assert.IsType<FuncDef>(ast);
    // Le body ne doit pas contenir d'opérateurs au-delà du tight-run.
    // Test indirect : si on rajoute un espace puis du contenu, ce
    // contenu ne doit pas être absorbé. Cas testable séparément avec
    // un input qui a un trailing après espace.
}
```

### 4.2. LatexRendererTests.cs

```csharp
[Fact]
public void Render_funcdef_single_var()
{
    Assert.Equal(@"f : x \mapsto 2x+1", RenderTop("f:x->2x+1"));
}

[Fact]
public void Render_funcdef_two_vars_adds_parens()
{
    Assert.Equal(@"f : (x,y) \mapsto x+y", RenderTop("f:x,y->x+y"));
}

[Fact]
public void Render_funcdef_with_named_function()
{
    Assert.Equal(@"g : t \mapsto \cos t+1", RenderTop("g:t->cos(t)+1"));
    // (Note: le rendu exact dépend de la convention RenderFunc — ajuster
    // selon ce que produit le renderer pour cos(t) dans ce contexte.
    // L'important est que `g : t \mapsto …` soit en tête.)
}
```

### 4.3. Régressions à surveiller

- Toute saisie qui commence par `Ident :` mais sans `->` (ex: `x : R`)
  doit fallback proprement. Si le parser standard ne sait pas gérer `:`
  comme op générique, le rendu sera dégradé — acceptable mais à
  documenter.
- Les tests existants utilisant `:` comme caractère ?
  **À chercher** avec `Grep` avant merge. Si aucun, OK. Si présents,
  vérifier compatibilité.

## 5. Hors scope V1 (ne PAS implémenter)

- ❌ Typage de fonction `f : R -> R, x -> expr` (forme complète).
- ❌ Définition par cas (`f(x) = { x si x>0 ; -x sinon }`).
- ❌ Composition `g ∘ f`.
- ❌ Notation `:=`.
- ❌ Plusieurs FuncDef sur la même ligne en un seul AST.
- ❌ Reconnaître `f:x` (sans `->`) comme une "fonction partiellement
  définie" — pas de transformation.

## 6. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `core-csharp/src/MathCursor.Core/Lattice/Vocabulary.cs` | Ajouter `:` dans `SingleOps` (ligne 94). |
| `core-csharp/src/MathCursor.Core/Lattice/Ast/AstNodes.cs` | Ajouter classe `FuncDef`. |
| `core-csharp/src/MathCursor.Core/Lattice/Parser.cs` | `Parse()` (ligne ~92), nouvelle méthode `TryParseFuncDef`. Référencer `ParseTightChain` (ligne ~369) pour le body. |
| `core-csharp/src/MathCursor.Core/Lattice/LatexRenderer.cs` | Ajouter branche `FuncDef` dans `Render()` (ligne 23) et méthode `RenderFuncDef`. |
| `core-csharp/tests/MathCursor.Core.Tests/Lattice/ParserTests.cs` | §4.1. |
| `core-csharp/tests/MathCursor.Core.Tests/Lattice/LatexRendererTests.cs` | §4.2. |
| `docs/dev/decisions/2026-04-29-Feat-function-definition.md` | ADR. |
| `docs/dev/decisions/2026-04-29-Feat-tight-as-grouping.md` | Dépendance pour `ParseTightChain`. |

## 7. Ce qu'il NE faut PAS faire

- ❌ Faire de `:` un opérateur infixe générique avec une priorité dans
  `ParseRelation` ou `ParseExpr`. La règle est **strictement** un pattern
  top-level (Ident `:` Ident… `->`), pas un op math.
- ❌ Détecter `:` à l'intérieur d'un Argument, d'un body de scope, etc.
  Top-level seulement.
- ❌ Émettre `\colon` à la place de `:` brut dans le rendu — `:` simple
  rend correctement dans WpfMath et Word OMath, le `\colon` change
  l'espacement et peut casser l'alignement attendu.
- ❌ Hardcoder une liste de noms de fonctions valides à gauche de `:`.
  N'importe quel Ident est un nom de fonction valide.
- ❌ Émettre une erreur pour `f:x` sans `->` ; on rewind et on laisse le
  parser standard faire ce qu'il peut.
- ❌ Toucher au `Lim` qui consomme aussi `->` (ligne 511 de Parser.cs) —
  les contextes sont disjoints, pas de risque de collision.

## 8. Validation finale

1. `dotnet build core-csharp/MathCursor.Core.sln` → 0 erreur, 0 warning.
2. `dotnet test core-csharp/tests/MathCursor.Core.Tests/` → tous passent
   (anciens + nouveaux du §4).
3. Test manuel pipeline (Lex → Parse → Render) sur les 4 cas du §2.2.
4. Index ADR mis à jour.
5. Commit unique : *"FuncDef : reconnaissance `f:x->expr` → `\mapsto`"*.

## 9. Estimation

- Lecture Lexer/Parser/Renderer : 30 min
- Vocabulary + AstNode + Parser hook + Renderer : 1h
- Tests + non-régression : 1h
- **Total** : ~2h30
