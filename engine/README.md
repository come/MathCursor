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
| — | `PhysChem.cs` + `ChemFormula.cs` | mode physique-chimie (option de culture, ADR 2026-10-16) |

## Non-régression

- **Gate** (et CI Rust) : `scripts/run-tests.ps1` rejoue `fixtures.json` (C# + Rust `conformance`) et les tests unitaires Rust (`cargo test -p mc-engine` : composition multiligne et systèmes).
- **Large, avant de commiter une évolution du moteur** : `python -I -X utf8 tools/engine-diff/engine_diff.py`.
  Compare, sur ~17 700 entrées générées, le moteur du commit HEAD à l'arbre de travail
  (décision, candidats, coûts, plus ~1 100 compositions multiligne `=` / `<=>` et systèmes `{`)
  et le moteur C# au moteur Rust, dans les modes fr, us,
  fr+physchem, us+physchem. Attendu : 0 écart, sauf évolution voulue (`--expect-diff`).
