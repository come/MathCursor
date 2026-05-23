# Fix — Leading unary `+`/`-` préservé dans engine v2

**Date :** 2026-05-23
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- ADR [2026-05-23-Feat-engine-v2-promotion](2026-05-23-Feat-engine-v2-promotion.md) (= engine v2 promu moteur principal P32, ce fix comble une dette POC laissée par P11.4-6).
- Test probe `PlusY2BugProbeTests` (= 3 cas reproducteurs, créés 2026-05-23).
- Bug user-reported 2026-05-23 « x2 commit + y2 commit le + est mangé ».

## Citation acté

> « oui go ! et apres avoir fixé, il faudra lire tous les commentaires du POC pour verifier ce qu'on a laissé en TODO ou equivalent ;) » — utilisateur, 2026-05-23

## Contexte

Bug user-reported : commit `x2`, puis tape `+y2` et fait Ctrl+Espace → la popup affiche `y^{2}` (le `+` initial est dropé), commit → le doc final contient `x²` collé à `y²` sans le `+` entre.

`+` n'étant pas un merge-marker (`IntraOMathsMerger.IsMergeMarker` = `<=>`, `=>`, `=`, `{`), il n'y a pas de cross-merge avec l'OMath voisine. La zone Ctrl+Espace est donc `+y2` en isolation, soumise à `MathEngine.Resolve`.

**Cause root identifiée** dans `StackParser.cs:97-101` :

```csharp
if (_vocab.Relations.TryGetValue(tok.Text, out var rel))
{
    // Opérande gauche obligatoire — sinon ignore (= leading
    // unary, pas géré au POC).
    if (operands.Count == 0)
    {
        i++;
        continue;   // ← le `+` initial est silencieusement skip
    }
```

Le commentaire **avoue la dette POC**. Pour `+y2` (tokens `[+, y, 2]`), le `+` est skip → operands=[y, 2] → InfixNode(`\cdotIM`, y, 2) → `y^{2}` via la règle letter-sup-number (P26). Le `+` est définitivement perdu.

Cas `+ y2` (avec espace) : plus pathologique encore. Les tokens deviennent `[+, Sep, y, 2]`. `ParseFlatOperand` (`MathEngine.cs:271`) break sur Sep → bucket=[+] → StackParser skip → flatLatex=`""`. Puis MathEngine continue : SkipSep → ti=`y`, mais le check « top-level operator » (`MathEngine.cs:139`) exige Symbol/Glue → tokens[ti]=Word `y` ne matche pas → break du loop. Résultat : `operandLatex=[""]` → `finalLatex=""`. `EngineZoneSource` fallback identity → popup affiche le source brut `+ y2`. L'user verrait du LaTeX cassé si commit. Bug moins « silencieux » que le cas collé mais aussi cassé.

Le POC engine v2 a été promu moteur principal hier (ADR `2026-05-23-Feat-engine-v2-promotion` P32). Les limitations POC laissées en commentaire deviennent maintenant des bugs visibles utilisateurs. **Il faut combler.**

## Décision

### F1 — Nouveau AST node `UnaryPrefixNode`

```csharp
public sealed record UnaryPrefixNode(string Op, AstNode Operand) : AstNode;
```

Modèle propre pour les opérateurs unaires préfixes (`+x`, `-x`). Pas de hack `AtomNode("")` qui produirait un AST sémantiquement bizarre.

### F2 — StackParser : whitelist `{+, -}` comme prefix-unary

Quand `operands.Count == 0` ET `tok.Text ∈ {+, -}` : consommer le prochain token comme operand, encapsuler dans `UnaryPrefixNode(op, operand)`, push dans `operands`. Pour les autres operators (=, *, /, <, >, etc.) — conserver le `continue` actuel (= ces opérateurs n'ont pas de sémantique unaire valide en début, et leur leading drop est de la robustesse).

### F3 — LatexEmitter : case `UnaryPrefixNode`

```csharp
case UnaryPrefixNode unary:
    sb.Append(unary.Op);
    Render(unary.Operand, sb);
    break;
```

Pas d'espace après l'op (= conv math compact, cohérent avec `IsArithmeticOp`).

### F4 — MathEngine : produit implicite top-level entre operands consécutifs

Pour le cas `+ y2` (le `+` isolé top-level puis `y2` séparé par Sep), le loop top-level voit 2 operands consécutifs sans opérateur infixe explicite entre. Comportement actuel : break (= cas non couvert). Comportement cible : **insérer un `\cdotIM` implicite** entre eux, cohérent avec ce que `StackParser.EnsureImplicitMulIfNeeded` fait à l'intérieur d'un operand.

Concrètement, à l'endroit du `break;` ligne 151 dans `MathEngine.Resolve` :
- Si le token courant peut commencer un autre operand (= Word/Number/OpenDelim/Symbol prefix-unary) → injecter un `opToken` synthétique `\cdotIM`, continuer.
- Sinon → break (= EOF effectif).

### Limites du fix

- `*y2`, `=y2`, `/y2`, etc. restent SKIP. Justification : sémantique unaire non définie pour ces opérateurs, et leur usage légitime est dans le contexte cross-merge (qui passe par `IntraOMathsMerger.IsMergeMarker`, pas par engine v2 isolé).
- Le fix ne couvre PAS `±` (signe plus-ou-moins). À ajouter quand le besoin sera observé, simple ajout dans la whitelist.

## Tradeoff & alternatives écartées

- **Hack `AtomNode("", "word")` comme LHS d'un InfixNode** : -25 lignes, mais introduit un AST « atom vide » sémantiquement faux que tous les détecteurs de collision et passes ultérieures devraient ignorer. Refusé — incohérent avec la phase big bang « propreté avant tout ».

- **Whitelist permissive (TOUS les operators)** : risque de produire du LaTeX bizarre (`=y^{2}`, `*y^{2}` en isolation). Refusé — la sémantique unaire de `=`, `*`, `/` n'a aucun sens math. Mieux vaut rester restrictif.

- **Reporter à Phase 2 (= traiter avec les autres dettes POC après lecture audit)** : aurait laissé un bug user-visible jusqu'à Phase 2. Refusé — bug bloquant pour l'usage quotidien.

## Conséquences

- **Code touché** :
  - `core-csharp/src/MathCursor.Engine/Ast/AstNode.cs` (+1 record `UnaryPrefixNode`)
  - `core-csharp/src/MathCursor.Engine/Parsing/StackParser.cs` (~10 lignes : whitelist + push UnaryPrefixNode)
  - `core-csharp/src/MathCursor.Engine/Emit/LatexEmitter.cs` (+1 case dans `Render`)
  - `core-csharp/src/MathCursor.Engine/MathEngine.cs` (~5 lignes : produit implicite top-level dans le loop)

- **Tests** :
  - 3 cas du probe `PlusY2BugProbeTests` rouges → verts.
  - 211 tests engine v2 actuels : préservés (aucun ne dépend du SKIP actuel).
  - Tests adapter VSTO : ne touchent pas engine v2, préservés.

- **API publique** : `UnaryPrefixNode` est `public` (cohérent avec les autres AST nodes). Pas de breaking change.

- **Règles MC impactées** : aucune.

## Validation post-fix

1. `PlusY2BugProbeTests` 3/3 verts.
2. `dotnet test core-csharp/tests/MathCursor.Engine.Tests/` → 214/214 verts (= 211 + 3).
3. Build VSTO + tests adapter 393/393 préservés.
4. Test manuel Word : commit `x2` puis `+y2` Ctrl+Espace → `Soit *x²* + *y²*` propre.

## Plan en cours — état d'avancement

Suite immédiate : **audit des commentaires POC engine v2** (= grep `POC`, `pas géré`, `P11.X`, `TODO`, `hack` dans `MathCursor.Engine/`). Identifier toutes les dettes laissées par le brief v4 → v5 et décider lesquelles méritent un Fix similaire avant de considérer engine v2 production-ready.
