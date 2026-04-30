# Fix — Fonction trigo + Number tight + Group avale la suite (`cos2(x)+1`)

**Date :** 2026-04-30
**Kind :** Fix
**Température :** molle
**Statut :** acté

## Contexte

Bug user 2026-04-30 (popup preview, `Cos2(x)+sin2(x)/4`) : la sortie LaTeX
était `\cos(2(x)+\sin(\frac{2(x)}{4}))` au lieu de
`\cos(x)^2+\sin(x)^2/4`. La fonction trigo aspirait toute la suite comme
argument.

### Reproduction

| Input | Output actuel | Attendu |
|---|---|---|
| `Cos2(x)` seul | `\cos\left(x\right)^{2}` | ✓ |
| `cos(x)` seul | `\cos\left(x\right)` | ✓ |
| `Cos2(x)+1` | `\cos\left(2\left(x\right)+1\right)` | `\cos\left(x\right)^{2}+1` |
| `sin2(x)/4` | `\sin\left(\frac{2\left(x\right)}{4}\right)` | `\frac{\sin\left(x\right)^{2}}{4}` |
| `Cos2(x)+sin2(x)/4` | nested cos/sin n'importe quoi | identité trig divisée par 4 |

### Cause racine

`Parser.cs:584` (parsing d'un `Func`) :
```csharp
var arg = ParseArgument() ?? Hole(1);
```
`ParseArgument` délègue à `ParseTightChain` qui absorbe les ops `+/-/*//`
quand le Lexer les marque `Tight=true` (pas d'espace). Pour `(x)+1`,
le `+` est tight, donc l'arg du `Func` aspire `+1`.

Ensuite, le remap `Sup(Func, Number)` (Parser.cs:591-596) requiert un
pattern strict `arg = Bin(*, Number, Group)`. Avec `+1` absorbé, l'arg
devient `Bin(+, Bin(*, Number, Group), 1)` → ne match plus → fallback sur
`Func(name, tout-l'arg)`. La fonction garde le gros arg.

## Décision

**Option choisie : parsing dédié au moment du `Func` (option 1).**

Au lieu d'appeler `ParseArgument()` générique pour un `Func`, on regarde
le préfixe immédiat des tokens :

- **Si le token suivant est `Number` tight-adjacent au `Func`, et le
  token d'après est `Group` `(`** → consommer pile `Number` + `Group`,
  émettre `Sup(Func(name, Group), Number)`. Le reste est laissé au parser
  parent.
- **Sinon** → flow actuel (ParseArgument générique, fallback puissances
  sans parens, tight chain, etc.)

### Pourquoi pas l'option 2 (split a posteriori)

L'option « laisser ParseTightChain absorber et re-décomposer après » est
plus tolérante mais imprévisible : enchâssements `Bin(+, Bin(*, ...))` à
plusieurs niveaux sont rapidement intractables. La sémantique "un Func
suivi tight de `Number` puis `Group` est UN exposant + UN appel" est
claire et localisée.

### Cas couverts

| Input | Comportement attendu post-fix |
|---|---|
| `Cos2(x)` | `\cos(x)^2` (inchangé, marche déjà) |
| `cos(x)` | `\cos(x)` (inchangé) |
| `Cos2(x)+1` | `\cos(x)^2 + 1` |
| `sin2(x)/4` | `\frac{\sin(x)^2}{4}` |
| `Cos2(x)+sin2(x)/4` | `\cos(x)^2 + \frac{\sin(x)^2}{4}` |
| `cos2(x)*y` | `\cos(x)^2 \cdot y` (mult tight implicite) |
| `cos 2(x)` (espace après cos) | `\cos(2(x))` — pas tight, fallback normal |
| `cos2x` (sans parens) | `\cos 2x` (inchangé, fallback Number sans Group) |
| `cos^{-2}(x)` | inchangé (parsé via `^` explicite, pas par cette règle) |

### Cas non gérés (volontairement hors scope)

- Power négatif via syntaxe tight (`cos-2(x)`) — l'utilisateur tape
  `cos^{-2}(x)` pour ça.
- Power non-numérique (`cosn(x)` pour `\cos^n(x)`) — pas demandé,
  ouvrirait une boîte de Pandore (`cosx(y)` ?).
- `(cos(x))^2` notation explicite — déjà gérée via `^` séparé.

## Implémentation

`Parser.cs`, dans le case `t.Type == EdgeType.Function` (vers ligne 581) :

```csharp
if (t.Type == EdgeType.Function)
{
    Consume();
    // Pattern dédié `Func Number Group` (tight) → puissance + appel.
    // Cf. ADR 2026-04-30-Fix-trig-func-power-tight-arg.
    var t1 = Peek();
    if (t1 != null && t1.Type == EdgeType.Number && t1.Tight == true)
    {
        // Sauvegarder _i pour rollback si le pattern complet ne match pas
        int save = _i;
        var numTok = Consume();
        var t2 = Peek();
        if (t2 != null && t2.Type == EdgeType.Op && t2.Value == "(" && t2.Tight == true)
        {
            var group = ParsePrimary();
            if (group is Group g)
                return new Sup(new Func(t.Value, g), new Atom("number", numTok.Value), isImplicit: true);
        }
        _i = save; // pas le pattern, rollback
    }
    var arg = ParseArgument() ?? (AstNode)Hole(1);
    // … remap historique conservé pour le cas `cos2x(y)` où Number et
    // Group sont parsés ensemble par ParseTightChain en Bin(*, num, group).
    if (arg is Bin bin && bin.Op == "*" && bin.Implicit && bin.Tight
        && bin.Lhs is Atom lhsAtom && lhsAtom.Kind == "number"
        && bin.Rhs is Group)
    {
        return new Sup(new Func(t.Value, bin.Rhs), bin.Lhs, isImplicit: true);
    }
    return new Func(t.Value, arg);
}
```

Le double check (avant ParseArgument + après via remap) reste utile : le
nouveau check prend le pattern strict `Func + Number tight + Group`, le
remap historique reste pour les cas où ParseTightChain a déjà mangé.

## Tests

- `core-csharp/tests/MathCursor.Core.Tests/Lattice/CosBugRepro.cs`
  converti en tests assertant le comportement attendu post-fix. Ils sont
  ROUGES tant que le fix n'est pas appliqué.
- Les tests existants (`AlternativeGeneratorTests`, `LatexRendererTests`,
  `LatticeEngineTests`, `RenderConformanceOmathTests`) doivent rester
  verts — vérification de non-régression.

## Validé par l'utilisateur

> oui on attaque le fix en option la plus robuste possible
