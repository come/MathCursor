# Feat — Engine v2 : capacité multi-line (align*/cases) portée depuis le legacy

**Date :** 2026-05-23
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- ADR [2026-05-23-Feat-engine-v2-promotion](2026-05-23-Feat-engine-v2-promotion.md) (= P32 a cassé le multi-line en supprimant le fallback legacy ; cet ADR comble le trou).
- ADR [2026-05-04-Feat-multiline-edit-cascade-merge](2026-05-04-Feat-multiline-edit-cascade-merge.md) (= mécanisme `MarkerChainCascadeMerger` côté adapter qui produit le `mergedSource` avec `\n`).
- ADR [2026-05-05-Feat-cases-multiline-phase2](2026-05-05-Feat-cases-multiline-phase2.md) (= Phase 2 cases du legacy).
- Bug user-reported 2026-05-23 « le merge interligne vers multiligne est completement cassé, ca merge en mettant tout sur une ligne ».
- Test probe `MultiLineBugProbeTests` (= 2 cas rouges qui valident le port).

## Citation acté

> « oui j'aimerait porter le legacy multiline dans le nouveau systeme » — utilisateur, 2026-05-23
>
> « go, valide le plan » — utilisateur, 2026-05-23

## Contexte

Le `MarkerChainCascadeMerger` (adapter VSTO, ADR 2026-05-04) produit un `mergedSource` avec `\n` séparateurs quand le user commit une ligne `<=> 2x` après une OMath déjà committée (= cascade montante). Avant P32 (= promotion engine v2 en moteur principal hier), `EngineZoneSource` retournait `null` quand engine v2 ne savait pas → fallback `LatticeEngine` legacy qui SAVAIT gérer le multi-line via `MultiLineBlock` AST.

P32 a changé la politique : `EngineZoneSource` retourne désormais TOUJOURS un `ResolvedZone` (= identité si TopLatex vide, jamais null). Pour un source `\n`-séparé, engine v2 produit un LaTeX **non-vide mais incorrect** (= concat 1-ligne) parce que son tokenizer skip silencieusement tous les whitespaces (incluant `\n`). Test probe :

```
input='a+b\n= c'  →  top='a+b = c'  ❌ (attendu : multi-ligne aligned)
input='x = y\n= z\n= w'  →  top='x = y = z = w'  ❌
```

Plus aucun fallback ne se déclenche → bug visible en prod après 24h d'engine v2 en moteur principal.

Deux options :

- **(A) Guard `\n` dans EngineZoneSource** : retour `null` si source contient `\n` → fallback legacy. Simple, rétablit le comportement « semaine passée ». Mais perpétue la dépendance legacy `[Obsolete]` pour un cas central.
- **(B) Porter la capacité multi-line dans engine v2** : tokenizer émet `Sep("\n")`, nouveau AST `MultiLineBlockNode`, pre-pass dans `MathEngine.Resolve`, emit `\begin{align*}` / `\begin{cases}` dans `LatexEmitter`. Plus de coût initial, mais engine v2 devient autonome pour ce cas central.

User choisit (B).

## Décision

Porter dans `MathCursor.Engine` la capacité multi-line align/cases du legacy `MathCursor.Core.Lattice.Parser`, en mappant 1-pour-1 la logique éprouvée.

### E1 — Tokenizer émet `Sep("\n")`

`Tokenization/Tokenizer.cs` : avant, `if (char.IsWhiteSpace(c)) { i++; continue; }` skip TOUS les whitespaces. Après : `\n` (et `\r\n`) produit un token `Sep` avec `Text="\n"`. Les autres whitespaces restent skipped (= continue), et le post-process insère `Sep(" ")` comme aujourd'hui pour les boundaries entre tokens.

Rationale du choix `Sep("\n")` vs `TokenKind.LineBreak` dédié : économie d'un kind, le `Text` suffit à disambiguer dans le pre-pass. Le `StackParser` traite déjà `Sep` whitespace en interne et break sur `Sep` `,` / `;` — donc `Sep("\n")` est mécaniquement traité comme un Sep ordinaire si jamais il atteint le parser (= fallback safe).

### E2 — AST `MultiLineBlockNode`

```csharp
public sealed class MultiLineBlockNode : AstNode
{
    public override string Kind => "multiLineBlock";
    public string Mode { get; }                              // "align" | "cases"
    public IReadOnlyList<AstNode> Lines { get; }
    public IReadOnlyList<string> LinePrefix { get; }         // ["", "\\Leftrightarrow ", …]
}
```

### E3 — `MathEngine.Resolve` pre-pass

Au début de `Resolve`, après le tokenize : `TryBuildMultiLineBlock(tokens)`. Si match → return EngineResult avec topLatex = `_flatEmitter.Emit(multiLineBlockNode)`. Sinon → loop top-level actuel inchangé (= comportement existant pour single-line, tous les 214 tests verts préservés).

**Algo `TryBuildMultiLineBlock`** (port direct de `Parser.TryParseMultiLineBlock`) :

```
1. Identifier les indices où tokens[i].Kind == Sep && tokens[i].Text == "\n" → lineBoundaries
2. Si 0 boundary → return null (single-line, fallback)
3. lineStarts = [0, b1+1, b2+1, ...] (= début de chaque ligne hors marker)
4. Detect cases : si line[0] commence par `{` (Op) ET aucune ligne ne contient un `}` non-fermant
     Si TOUTES les lignes commencent par `{` → mode "cases"
     Sinon → return null (= pas un cases pur)
5. Detect align : pour chaque ligne 2+, vérifier que le 1er token est un marker align
     Si UNE seule ligne 2+ sans marker → return null (= fallback single-line)
6. Pour chaque ligne, slicer tokens et parser via StackParser.Parse (en skippant le marker initial pour lignes 2+ en align, ou le `{` initial en cases)
7. Construire MultiLineBlockNode(mode, lines, prefixes)
```

**Mapping markers align** (porté tel quel) :

```
"="     → ""                  (chaîne d'égalités, aligné via &)
"<=>", "<==>", "⇔", "↔", "⟺"  → "\\Leftrightarrow "
"=>", "==>", "⇒", "⟹"          → "\\Rightarrow "
"<=", "<==", "⇐", "⟸"          → "\\Leftarrow "
```

### E4 — `LatexEmitter` render

```csharp
case MultiLineBlockNode mb:
    RenderMultiLineBlock(mb, sb);
    break;
```

Port direct de `LatexRenderingVisitor.Visit(MultiLineBlock)` legacy :
- Mode "align" : `\begin{align*} line0 \\ prefix1 line1 \\ … \end{align*}`
- Mode "cases" : `\begin{cases} line0 \\ line1 \\ … \end{cases}`

### Scope explicitement hors

- **FuncDef** (= pattern legacy `Ident: ... -> body` dans `Parser.cs:183`) : pas porté dans ce changement. Si besoin observé → ADR séparé.
- **`{ ... }` mode cases mixte avec align dans le même bloc** : exclu (comportement legacy = pas de mix, brief 30-04 §3.4).
- **Rendu Word-matrix-2-colonnes** pour cases (= legacy aligne sur `=` via matrix interne dans `RenderCasesLine`) : porté en V1 simplifié (= concat sans alignement intra-cell). Raffinement possible plus tard.

## Tradeoff & alternatives écartées

- **(A) Guard `\n` dans EngineZoneSource → fallback legacy** : rejetée par user. Rationale : perpétue la dépendance silencieuse à `MathCursor.Core` `[Obsolete]` pour un cas central. La promotion engine v2 (P32) visait l'autonomie ; ce port la complète.

- **Nouveau `TokenKind.LineBreak` dédié** : rejeté. `Sep("\n")` suffit à disambiguer via Text, économise une enum value, et reste safe si jamais le pre-pass rate (= `\n` traité comme Sep ordinaire = comportement actuel buggy mais non-pire).

- **Pre-pass dans `StackParser.Parse`** : rejeté. `StackParser` est documenté comme « parser flat operands » (cf. ADR 2026-05-23-Refactor-zonespan ; même session). Le multi-line est une orchestration de N operands flat, c'est l'affaire de `MathEngine.Resolve`. Pre-pass au niveau orchestrateur = scope correct.

## Conséquences

- **Code touché** :
  - `core-csharp/src/MathCursor.Engine/Tokenization/Tokenizer.cs` (~10 lignes : émission `Sep("\n")`)
  - `core-csharp/src/MathCursor.Engine/Ast/AstNode.cs` (+ `MultiLineBlockNode` ~10 lignes)
  - `core-csharp/src/MathCursor.Engine/MathEngine.cs` (pre-pass + helpers `TryBuildMultiLineBlock`, `MapAlignMarkerToLatex`, `IsCasesLineStart`, ~120 lignes)
  - `core-csharp/src/MathCursor.Engine/Emit/LatexEmitter.cs` (case + `RenderMultiLineBlock` ~30 lignes)

- **Tests** :
  - `MultiLineBugProbeTests` (= 2 cas) doit passer 2/2.
  - 214/214 tests engine v2 existants doivent rester verts (= aucun ne dépend du `\n` skip silencieux ; à confirmer par run complet).
  - Pas de test adapter VSTO impacté (= mécanique merger inchangée).

- **API publique** : `MultiLineBlockNode` est `public` (cohérent avec les autres AST nodes). Pas de breaking change.

- **Règles MC impactées** : aucune.

## Validation post-fix

1. `MultiLineBugProbeTests` 2/2 verts.
2. `dotnet test core-csharp/tests/MathCursor.Engine.Tests/` → 216/216 verts (= 214 + 2).
3. Test manuel Word : commit OMath ligne 1, taper `<=> 2x` ligne 2, Ctrl+Espace → vérifier `\begin{align*} … \\ \Leftrightarrow 2x \end{align*}` propre.
4. Idem cases : `{ x=1` puis `{ y=2` sur lignes suivantes.

## Plan en cours — état d'avancement

Cette ADR clôt la régression P32 sur le multi-line. Le fallback legacy `[Obsolete]` continue de servir pour les ~10% autres cas non couverts (= FuncDef notamment).
