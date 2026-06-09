# engine — moteur de reconnaissance (portage `forest`)

Portage C# du moteur web `D:\web\MathCursor\forest\` (grammaire ambiguë → forêt
de parses → classement par coût → décision popup/auto → **LaTeX**). Pur, sans
dépendance Word/VSTO.

Voir le mapping fichier→fichier et la stratégie dans [`../PLAN.md`](../PLAN.md) §3.

| `forest/*.js` | → `MathCursor.Engine/*.cs` | rôle |
|---------------|----------------------------|------|
| `vocabulary.js` | `Vocabulary.cs` | table déclarative (le seul à nommer des opérateurs) |
| `lexer.js` | `Lexer.cs` | chars → tokens + juxtaposition |
| `parser.js` | `Parser.cs` + `Node.cs` | tokens → forêt (chart parser ambigu) |
| `score.js` | `Score.cs` | `crossesCut` + `cost` |
| `segment.js` | `Segment.cs` | bornage perf + recombinaison |
| `index.js` | `ForestEngine.cs` | orchestrateur `Analyze` |
| `render.js` | `LatexRenderer.cs` | AST → LaTeX |
| `units.js` | `Units.cs` | unités composées |
| `fixtures.js` | `../tests/MathCursor.Engine.Tests/` | non-régression (contrat de fidélité) |
