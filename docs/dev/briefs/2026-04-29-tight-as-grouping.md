# Brief — Tight-as-grouping : `/`, `^`, `_` collés = groupement implicite

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-29
**Branche :** `lattice-engine`
**Public cible :** agent C# autonome qui ne connaît pas le projet, intervient
sur le moteur lattice (couche 1 — `core-csharp/`).
**ADR liée :** [2026-04-29-Feat-tight-as-grouping.md](../decisions/2026-04-29-Feat-tight-as-grouping.md)

---

## 1. Le besoin

En sténo clavier (saisie au fil de l'eau pour un cours de maths), forcer
l'utilisateur à taper des parenthèses autour des opérandes d'une fraction casse
le flow. Aujourd'hui :

```
AB/BC          → ((A·B)/B)·C        ❌ sémantique pas alignée avec l'intuition
1/x+1          → (1/x) + 1          ❌ idem
```

L'utilisateur attend, sans avoir à parenthéser :

```
AB/BC          → \frac{AB}{BC}
1/x+1          → \frac{1}{x+1}
1/x +1         → \frac{1}{x} + 1     ✓ déjà OK (`+` est loose)
```

**Règle générique à introduire :** *l'absence d'espace est un groupement
implicite autour des opérateurs structurels `/`, `^`, `_`.* L'opérande droit
absorbe toute la chaîne tight qui suit, jusqu'au premier espace.

État actuel :

- `^` et `_` : règle **déjà active** (via `ParseArgument → ParseTightChain`,
  Parser.cs:249-254). Rien à coder, on ajoute juste des tests pour pinner
  l'invariant.
- `/` : règle **à ajouter**. Aujourd'hui `/` consume seulement un `Postfix`
  à droite (Parser.cs:210-214), pas un `TightChain`.

## 2. Sémantique attendue (cas exhaustifs)

### 2.1. Cas qui changent (régressions vs comportement actuel)

| Source | AST attendu | Rendu LaTeX |
|--------|-------------|-------------|
| `AB/BC` | `Bin("/", Bin("*", A, B, implicit, tight), Bin("*", B, C, implicit, tight))` | `\frac{AB}{BC}` |
| `1/x+1` (collé) | `Bin("/", 1, Bin("+", x, 1, tight))` | `\frac{1}{x+1}` |
| `A/B/C` (tout collé) | `Bin("/", A, Bin("/", B, C))` *(droit-assoc)* | `\frac{A}{\frac{B}{C}}` |

### 2.2. Cas qui ne bougent PAS (non-régressions)

| Source | AST attendu | Pourquoi |
|--------|-------------|----------|
| `1/x +1` (espace avant `+`) | `Bin("+", Bin("/", 1, x), 1)` | `+` est loose, TightChain s'arrête |
| `1/x` simple | `Bin("/", 1, x)` | rien à grouper à droite |
| `cos(x)/sin(x)` | `Bin("/", Func(cos, x), Func(sin, x))` | les groupes explicites bornent les opérandes |
| `(a+b)/c` | `Bin("/", Group(...), c)` | parens explicites, indépendant |
| `2x` (mult implicite) | `Bin("*", 2, x, implicit, tight)` | hors périmètre |
| `n+1` | `Bin("+", n, 1, tight)` | hors périmètre |
| `u_n+1` (collé) | `Sub(u, Bin("+", n, 1, tight))` | DÉJÀ OK, à pinner par un test |
| `u_n +1` (espace) | `Bin("+", Sub(u, n), 1)` | DÉJÀ OK, à pinner par un test |
| `x^a+b` (collé) | `Sup(x, Bin("+", a, b, tight))` | DÉJÀ OK, à pinner par un test |
| `x^a +b` (espace) | `Bin("+", Sup(x, a), b)` | DÉJÀ OK |

### 2.3. Cas asymétrique connu (HORS SCOPE)

| Source | AST actuel = AST après V1 | Idéal théorique |
|--------|---------------------------|-----------------|
| `a 2x/y` (espace avant `2`, collé après `x`) | `Bin("/", Bin("*", Bin("*", a, 2), x), y)` | `Bin("*", a, Bin("/", Bin("*", 2, x), y))` |

Le parser descendant aspire `a·2·x` au niveau `Term` *avant* de voir le `/`.
Pour vraiment limiter le lhs du `/` à la chaîne tight (`2·x`), il faudrait
stratifier `Term` en `LooseTerm`/`TightTerm`, refactor invasif. **On accepte
cette imperfection en V1.** Si l'usage la révèle gênante, on tranchera dans
une ADR ultérieure.

L'utilisateur peut contourner : `a (2x/y)` ou `a 2x /y`.

## 3. Plan d'implémentation

### 3.1. Modification unique dans Parser.cs

Dans `ParseTerm` (Parser.cs:204-226), modifier la branche `IsOp("*", "/")` pour
choisir le parser du rhs selon le drapeau `tight` quand l'opérateur est `/` :

```csharp
private AstNode? ParseTerm()
{
    var lhs = ParsePostfix();
    if (lhs == null) return null;
    while (true)
    {
        if (IsOp("*", "/"))
        {
            var op = Consume();
            // Tight-as-grouping : `/` collé absorbe toute la chaîne tight
            // à droite (cf. ADR 2026-04-29-Feat-tight-as-grouping). `*`
            // garde le comportement standard (rhs = Postfix).
            AstNode? rhs;
            if (op.Value == "/" && op.Tight == true)
                rhs = ParseTightChain();
            else
                rhs = ParsePostfix();
            lhs = new Bin(op.Value, op.Tight ?? false, false, lhs, rhs ?? Hole(1));
        }
        else if (CanStartFactor()) { /* unchanged */ }
        else break;
    }
    return lhs;
}
```

C'est **la seule modification de code** nécessaire pour la feature. `^` et `_`
sont déjà OK, le LatexRenderer est déjà OK.

### 3.2. Garde sur `ParseTightChain`

Vérifier que `ParseTightChain` (Parser.cs:369-396) gère correctement le cas
où le tight-run est vide ou se termine immédiatement. Comportement attendu :
si après le `/` il n'y a pas de Postfix valide, retourne null → on tombe sur
`Hole(1)` comme aujourd'hui. Pas de changement de signature.

## 4. Tests obligatoires

### 4.1. ParserTests.cs — ajouter

```csharp
[Fact]
public void Tight_slash_AB_over_BC_groups_both_sides()
{
    // AB/BC tout collé → fraction de deux groupes implicites
    var ast = ParseTop("AB/BC");
    var slash = Assert.IsType<Bin>(ast);
    Assert.Equal("/", slash.Op);
    Assert.True(slash.Tight);

    // lhs = A·B (mult implicite tight)
    var lhs = Assert.IsType<Bin>(slash.Lhs);
    Assert.Equal("*", lhs.Op);
    Assert.True(lhs.Implicit);
    Assert.True(lhs.Tight);

    // rhs = B·C (mult implicite tight) — c'est ce que la nouvelle règle apporte
    var rhs = Assert.IsType<Bin>(slash.Rhs);
    Assert.Equal("*", rhs.Op);
    Assert.True(rhs.Implicit);
    Assert.True(rhs.Tight);
}

[Fact]
public void Tight_slash_1_over_x_plus_1_absorbs_addition()
{
    // 1/x+1 collé → 1 / (x+1) (pas (1/x) + 1)
    var ast = ParseTop("1/x+1");
    var slash = Assert.IsType<Bin>(ast);
    Assert.Equal("/", slash.Op);
    Assert.True(slash.Tight);

    var rhs = Assert.IsType<Bin>(slash.Rhs);
    Assert.Equal("+", rhs.Op);
    Assert.True(rhs.Tight);
}

[Fact]
public void Loose_slash_then_loose_plus_keeps_standard_precedence()
{
    // 1/x +1 (espace avant +) → (1/x) + 1
    var ast = ParseTop("1/x +1");
    var plus = Assert.IsType<Bin>(ast);
    Assert.Equal("+", plus.Op);
    var slash = Assert.IsType<Bin>(plus.Lhs);
    Assert.Equal("/", slash.Op);
}

[Fact]
public void Tight_slash_chain_is_right_associative()
{
    // A/B/C tout collé → A / (B/C)
    var ast = ParseTop("A/B/C");
    var outer = Assert.IsType<Bin>(ast);
    Assert.Equal("/", outer.Op);
    Assert.IsType<Atom>(outer.Lhs);
    var inner = Assert.IsType<Bin>(outer.Rhs);
    Assert.Equal("/", inner.Op);
}

[Fact]
public void Slash_between_groups_keeps_groups()
{
    // cos(x)/sin(x) → groupes explicites, pas affecté
    var ast = ParseTop("cos(x)/sin(x)");
    var slash = Assert.IsType<Bin>(ast);
    Assert.Equal("/", slash.Op);
    Assert.IsType<Func>(slash.Lhs);
    Assert.IsType<Func>(slash.Rhs);
}

[Fact]
public void Tight_underscore_groups_subscript()
{
    // u_n+1 collé → u_{n+1} (déjà OK aujourd'hui, on pin l'invariant)
    var ast = ParseTop("u_n+1");
    var sub = Assert.IsType<Sub>(ast);
    var idx = Assert.IsType<Bin>(sub.Idx);
    Assert.Equal("+", idx.Op);
    Assert.True(idx.Tight);
}

[Fact]
public void Loose_after_underscore_keeps_subscript_atomic()
{
    // u_n +1 (espace) → (u_n) + 1
    var ast = ParseTop("u_n +1");
    var plus = Assert.IsType<Bin>(ast);
    Assert.Equal("+", plus.Op);
    Assert.IsType<Sub>(plus.Lhs);
}

[Fact]
public void Tight_caret_groups_exponent()
{
    // x^a+b collé → x^(a+b) (déjà OK, on pin l'invariant)
    var ast = ParseTop("x^a+b");
    var sup = Assert.IsType<Sup>(ast);
    var exp = Assert.IsType<Bin>(sup.Exp);
    Assert.Equal("+", exp.Op);
    Assert.True(exp.Tight);
}
```

### 4.2. LatexRendererTests.cs — ajouter

```csharp
[Fact]
public void Render_AB_over_BC()
{
    Assert.Equal(@"\frac{AB}{BC}", RenderTop("AB/BC"));
}

[Fact]
public void Render_1_over_x_plus_1_tight()
{
    Assert.Equal(@"\frac{1}{x+1}", RenderTop("1/x+1"));
}

[Fact]
public void Render_1_over_x_plus_1_loose()
{
    // Le rendu exact dépend de la convention espace ; vérifier au moins que
    // c'est `\frac{1}{x}` suivi de `+1` (pas `\frac{1}{x+1}`).
    var s = RenderTop("1/x +1");
    Assert.StartsWith(@"\frac{1}{x}", s);
    Assert.Contains("+1", s);
    Assert.DoesNotContain(@"\frac{1}{x+1}", s);
}

[Fact]
public void Render_tight_subscript_grouped()
{
    Assert.Equal(@"u_{n+1}", RenderTop("u_n+1"));
}

[Fact]
public void Render_tight_exponent_grouped()
{
    Assert.Equal(@"x^{a+b}", RenderTop("x^a+b"));
}
```

### 4.3. Régressions à surveiller

Lancer la suite de tests existante en entier après le changement. Cas
particulièrement à surveiller (le changement touche la branche `/` du
parsing) :

- Toutes les divisions actuelles dans `ParserTests` et `LatexRendererTests`
  doivent rester vertes.
- Cas où le rhs de `/` est un `(...)` explicite (ex: `1/(x+1)`) : `ParsePrimary`
  intervient avant que `ParseTightChain` ne décide quoi que ce soit, donc
  inchangé. Mais à vérifier en test.
- Cas où le rhs de `/` est une fonction (`a/sin(x)`) : `ParseTightChain →
  ParsePostfix → ParsePrimary` reconnaît la fonction. Un test ne ferait pas
  de mal.

## 5. Hors scope (à NE PAS toucher)

- ❌ `LatexRenderer.cs` : aucun changement, le rendu `\frac{}{}` /
  `^{...}` / `_{...}` groupe déjà visuellement.
- ❌ Stratification `Term`/`TightTerm` (cas asymétrique `a 2x/y`) — décision
  ADR explicite de l'accepter en V1.
- ❌ Étendre la règle à `+ - *` — décision ADR explicite (Garde la
  précédence math standard pour ces opérateurs).
- ❌ Modifier `^` ou `_` dans `ParsePostfix` — déjà tight-greedy, ne pas y
  toucher.
- ❌ Changer le mécanisme `Tight` côté Lexer — propriété déjà calculée et
  fiable.
- ❌ Toucher au moteur de désambiguïsation (AlternativeGenerator) — la règle
  est déterministe, pas une ambiguïté.

## 6. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `core-csharp/src/MathCursor.Core/Lattice/Parser.cs` | Le parser. La modif est dans `ParseTerm` (ligne ~210). |
| `core-csharp/src/MathCursor.Core/Lattice/Parser.cs:369-396` | `ParseTightChain` — celui qu'on appelle pour le rhs `/` tight. |
| `core-csharp/src/MathCursor.Core/Lattice/Parser.cs:249-254` | `ParsePostfix` branche `^`/`_` — référence pour comprendre pourquoi ces deux-là sont déjà OK. |
| `core-csharp/src/MathCursor.Core/Lattice/LatexRenderer.cs` | Renderer LaTeX. Aucun changement requis. |
| `core-csharp/tests/MathCursor.Core.Tests/Lattice/ParserTests.cs` | Suite de tests parser (xUnit). Y ajouter §4.1. |
| `core-csharp/tests/MathCursor.Core.Tests/Lattice/LatexRendererTests.cs` | Suite renderer. Y ajouter §4.2. |
| `docs/dev/decisions/2026-04-29-Feat-tight-as-grouping.md` | ADR de référence. |

## 7. Ce qu'il NE faut PAS faire

- ❌ Refactorer `ParseTerm` au-delà du minimum (juste switcher rhs entre
  `ParsePostfix` et `ParseTightChain` selon `op.Value == "/" && op.Tight`).
- ❌ Introduire un nouveau type AST. Le `Bin("/", lhs, rhs, tight)` existant
  porte déjà toute l'info nécessaire au renderer.
- ❌ Ajouter un drapeau "this Bin came from tight-grouping" sur l'AST. La
  structure (rhs = Bin) suffit, pas besoin de métadonnée.
- ❌ Toucher au LatexRenderer pour ajouter des `\left(...\right)` autour des
  opérandes de `\frac` — `\frac` groupe déjà visuellement, ajouter des parens
  serait redondant et moche.
- ❌ Faire un commit qui mélange le changement parser et autre chose. Un
  commit dédié, avec l'ADR référencée dans le message.

## 8. Validation finale

1. `dotnet build core-csharp/MathCursor.Core.sln` (ou `MathCursor.sln` à la
   racine) → 0 erreur, 0 warning.
2. `dotnet test core-csharp/tests/MathCursor.Core.Tests/` → tous les tests
   passent (anciens + 8 nouveaux du §4).
3. Test manuel pipeline complet : taper `AB/BC` puis `1/x+1` dans la popup
   ou via un test d'intégration `Lex → Parse → Render`. Vérifier le LaTeX
   produit visuellement.
4. Index ADR mis à jour : `docs/dev/decisions/README.md` contient la nouvelle
   entrée en haut de la section 2026-04-29.
5. Commit unique : *"Tight `/` collé absorbe le rhs comme groupe (cf. ADR
   tight-as-grouping)"*.

## 9. Estimation

- Lecture Parser.cs + tests existants : 30 min
- Modification + nouveaux tests : 1 h
- Vérification non-régression + build : 30 min
- **Total** : ~2 h

Petit changement, gros impact ergo. Vérifier que la suite passe encore est la
partie la plus longue.
