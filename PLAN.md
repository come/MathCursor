# MathCursor — Plan de consolidation beta

> **Statut** : plan acté (4 décisions structurantes validées le 2026-06-09).
> **Dossier de travail** : `D:\Software\MathCursor` (« la toutouille »).
> **Sources figées, NE PAS modifier** :
> - `D:\Software\DocMath` — plugin VSTO complet (socle bon + dette accumulée autour de l'ancien moteur).
> - `D:\web\MathCursor` — prototype web, moteur de reconnaissance `forest/` jugé **parfait** par l'auteur.

L'enjeu : extraire le **bon socle** de DocMath + **porter le moteur `forest`** en C#, pour une **beta propre, sobre, à architecture découplée**, qui devienne réellement *prodable*.

---

## 1. Décisions actées (2026-06-09)

| # | Sujet | Décision |
|---|-------|----------|
| 1 | **Repo git** | Reprendre le `.git` de DocMath (327 commits, remote GitHub `come/MathCursor`), créer une **branche propre** `beta-clean`, faire le cherry-pick comme une série de commits dessus. Historique + remote préservés. |
| 2 | **Périmètre beta** | **Sobre** : conversion d'UNE équation au curseur + stockage source en anchor CC + revert (Ctrl+Z) + ré-édition in-place + feedback + NER isolé. **Reporté** : chaînes de raisonnement/align, multiline, list-mode, cases-cascade, merging multi-zones. |
| 3 | **Chemin OMML** | `forest` porté → **LaTeX** → **`LatexToOmml.cs` existant** (éprouvé) → `Range.InsertXML`. Découplage net moteur ⟂ sérialisation. |
| 4 | **Déclencheur** | **Manuel d'abord** (Ctrl+Espace / sélection). NER conservé, isolé, branché plus tard pour l'auto-détection. |

---

## 2. Architecture cible (4 couches + 1 module isolé)

```
┌─────────────────────────────────────────────────────────────┐
│  adapter-vsto         (C3 : Word Desktop / VSTO / .NET 4.8)  │
│    WPF popup au caret · insertion OMML · anchor CC · revert  │
│    edit-mode · feedback · ribbon · Ctrl+Espace               │
│        │ implémente                                          │
│        ▼                                                      │
│  host-contract        (C2 : 4 interfaces abstraites)         │
│    IDocumentHost · IEquationStore · IEditorSurface ·         │
│    IUserFeedback                                             │
│        │ utilise                                             │
│        ▼                                                      │
│  engine               (C1b : portage forest — PUR)          │
│    lexer · parser (forêt) · score · render → LaTeX           │
│    vocabulary déclaratif (le seul à nommer des opérateurs)   │
│        │                                                     │
│        ▼                                                      │
│  serialization        (C1a : LaTeX → OMML — PUR)            │
│    LatexToOmml (repris de DocMath/core-csharp)               │
└─────────────────────────────────────────────────────────────┘

   [ NER ]  module isolé (Detection/) : ONNX Runtime + WordPiece C#
            texte → DetectedZone[]. Branché en auto-détection plus tard.
```

**Règle dure (conservée de DocMath)** : `engine` et `serialization` ne connaissent
ni Word, ni VSTO, ni Office.js. Aucune référence `Microsoft.Office.*` hors `adapter-vsto`.
Les analyzers d'architecture (`analyzers/`) restent branchés via `Directory.Build.props`.

---

## 3. Le moteur `forest` (web) — ce qu'on porte

Architecture **moteur générique ⟂ vocabulaire déclaratif**. Pipeline :

```
src → lexAll → segment (coupes) → forest (tous les parses) → filtre coupes
    → cost (classement) → tri/dédup → décision popup|auto → render → LaTeX
```

| Fichier JS (`D:\web\MathCursor\forest\`) | Rôle | Cible C# |
|------------------------------------------|------|----------|
| `vocabulary.js` (314 l.) | **Le seul** à nommer des opérateurs. Chaque symbole : `shape`, `arity`, `class` (WEAK/STRONG), `looseness`, `bracketed`, `render` LaTeX. Multilingue (alias FR), cultures (décimale FR/anglo). | `Vocabulary.cs` (table déclarative) |
| `lexer.js` (237 l.) | chars → tokens, plus-long-match, juxtaposition (règles `JOIN` en données), découpe de run (itheta → i·θ), `lexAll` (niveau 2). | `Lexer.cs` |
| `parser.js` (273 l.) | tokens → **forêt** (chart parser mémoïsé, grammaire ambiguë). Atomes/groupes/préfixes/n-aires/infixes + matrices/intervalles/ensembles/abs. | `Parser.cs` + `Node.cs` |
| `score.js` (190 l.) | `crossesCut` (filtre dur) + `cost` (inversions de looseness, nichage, fidélité, cohérence globale/mode, trous). | `Score.cs` |
| `segment.js` (121 l.) | bornage perf (coupe aux relations + infixes espacés), recombinaison cohérente. | `Segment.cs` |
| `index.js` (157 l.) | orchestrateur : `assemble` (relations → n-aire tête → coupes) + `analyze` → `{ decision, ranked }`. | `ForestEngine.cs` |
| `render.js` (50 l.) | AST → LaTeX (dispatch vers le `render` du vocabulaire). | `LatexRenderer.cs` |
| `units.js` (62 l.) | unités composées (m/s, km/h…). | `Units.cs` |
| `fixtures.js` | **Snapshot de non-régression** (**280 cas** : 234 auto / 45 popup / 1 erreur ; baseline JS 280/280 vert). | `EngineTests` (xUnit) — **source de vérité du portage** |

**Sortie** : LaTeX (ex. `\frac{1}{x+1}`, `\lim_{n\to 0} \frac{1}{n}`). Décision `popup`
(plusieurs candidats dans la fenêtre de coût) ou `auto` (un seul).

**Stratégie de portage** : portage **fidèle, fichier par fichier**, en gardant la
séparation moteur/vocabulaire. Les `fixtures.js` deviennent des tests xUnit qui
**doivent rester verts** — c'est le contrat de fidélité avec le proto web.

---

## 4. Cherry-pick DocMath — inventaire

### 4.1 ON GARDE (le bon socle)

| Source DocMath | Cible | Pourquoi |
|----------------|-------|----------|
| `host-contract-csharp/**` (4 interfaces + Types) | `host-contract/` | Frontière d'archi propre, déjà parfaite. `EquationOutput` porte déjà `Latex`+`Omml`+`UnicodeFallback`. |
| `core-csharp/.../LatexToOmml.cs` | `serialization/` | LaTeX → `<m:oMath>`, éprouvé (ADR 2026-06-02, batterie 16/17). Couvre frac/sqrt/scripts/nary/lim/accents/délimiteurs/ensembles/grec. |
| `adapter-vsto/.../UI/SuggestionPopupWindow.cs`, `EditModePopupWindow.cs`, `WpfMathAdapter.cs`, `MixedLatexRenderer.cs` | `adapter-vsto/UI/` | Popup WPF au caret, rendu LaTeX (WpfMath 2.1). |
| `adapter-vsto/.../Host/Caret/CaretScreenPositionReader.cs` | idem | Positionnement écran de la popup. |
| `adapter-vsto/.../Host/CCMeta/**` (CcMetaResolver, MCMeta, MCMetaJson, Sha1Helper) | idem | **Anchor CC pattern** (ADR 2026-05-19) : CC minuscule sur ZWSP caché à côté de l'OMath, Tag JSON (source/LaTeX/hash). Backward probe O(1). |
| Logique `InsertOMathAt` + `BuildOMathViaOmml` (actuellement noyée dans `Host/SuggestionService.cs`) | `adapter-vsto/Host/` (extraite proprement en `OMathInserter`) | Séquence d'insertion validée (ZWSP plain → math → InsertXML → anchor CC → MoveRight escape). À **extraire** du monolithe, sans le merging. |
| `adapter-vsto/.../Host/ZoneCleaner.cs` | idem | Nettoyage zone + anchor au revert. |
| `adapter-vsto/.../Host/EditMode/EditModeController.cs` | idem | Ré-édition in-place (retrouve le source via CC). |
| `adapter-vsto/.../Host/Feedback/**` (FeedbackReport, IFeedbackSender, Clipboard/Http senders, UserIdStore, FeedbackJson, FeedbackSenderFactory) + `UI/FeedbackDialog.cs` | `adapter-vsto/Host/Feedback/` | Feedback utilisateur (jugé bon). |
| `adapter-vsto/.../Detection/**` (MathNerDetector, DetectedZone, WordPiece/) + `models/distilmult-v5/` | `adapter-vsto/Detection/` + `models/` | **NER isolé** : ONNX Runtime 1.16.3 (pinné Bay Trail), WordPiece C# pur. API `Detect(text)→DetectedZone[]`. Conservé, branché plus tard. |
| `adapter-vsto/.../Host/KeyboardInterceptor.cs`, `ManualTrigger/`, `RibbonCallback.cs`, `Ribbon.xml`, `ThisAddIn.cs`, `Strings.cs` | idem | Hook clavier (Tab/Enter/flèches/Esc), trigger manuel, ribbon, bootstrap add-in. À élaguer du superflu (boutons tableaux/courbes hors-scope beta). |
| `Directory.Build.props`, `analyzers/`, `.editorconfig`, `MathCursor_TemporaryKey.pfx`, build/signing VSTO | racine | Gestion des builds + analyzers d'archi + signature manifest. |
| `briefs/` (ergonomie, architecture-flow, detection-ner) | `docs/` | Briefs produit — source de vérité ergonomie. |

### 4.2 ON LAISSE DERRIÈRE (la dette autour de l'ancien moteur)

| Bloc DocMath | Raison |
|--------------|--------|
| `core-csharp/.../MathCursor.Engine` (RewriteEngine, PrimitiveRules, Rewriting/, Rules/, Yaml/, Tokenization/, Vocabulary/) | **L'ancien moteur qui driftait** → remplacé par `forest`. |
| `core-csharp/.../MathCursor.Core` (Tokenizer, Scorer, ZoneDetector, Parser, OmmlSerializer, Lattice, Resolution/Sidecar…) sauf `LatexToOmml` | Idem — ancien pipeline de reconnaissance. On ne garde que `LatexToOmml` (+ éventuellement `LatexToUnicodeMath` en garde-fou). |
| `core-csharp/.../MathCursor.Engine.Adapter` | Pont vers l'ancien moteur. |
| `Host/Merging/**` (8 fichiers), `Host/ListMode*` (5), `CasesCascadeMerger`, `IntraMergeSidecarBuilder`, `RevertedZoneMerger`, `Host/Session/**`, `ColumnLayoutInserter`, `Host/Layout/`, `Host/Pipeline/**` (8 stages) | Machinerie multi-zones/multiline/align/cases écrite pour **compenser** l'ancien moteur. `forest` gère nativement fractions/sommes/matrices/cas. Hors périmètre beta sobre. |
| `Cheatsheet/`, boutons ribbon tableaux de variation/signe/courbes/figures, `UI/Debug/`, `Host/Debug/` | Hors-scope beta. (Cheatsheet réinjectable plus tard.) |
| `archive/`, `web-demo/`, `tools/TutorialBuilder/`, `LatexToUnicodeMath` (insertion) | Obsolète / hors-scope. |

> **Note** : « laisser derrière » = ne pas porter dans la branche `beta-clean`.
> Tout reste accessible dans l'historique git et dans `D:\Software\DocMath`.

---

## 5. Roadmap d'exécution (phases)

> Chaque phase = un (ou quelques) commit(s) atomique(s) sur `beta-clean`, avec build vert.

- **Phase 0 — Fondation git + scaffold**
  - Importer le `.git` de DocMath dans `D:\Software\MathCursor`, créer la branche `beta-clean` (sans toucher l'arbo DocMath).
  - Poser la structure cible vide : `engine/`, `serialization/`, `host-contract/`, `adapter-vsto/`, `models/`, `docs/`, `analyzers/`, solution `.sln`.
  - `.gitignore`, `Directory.Build.props`, `.editorconfig`.

- **Phase 1 — Couche pure : moteur + sérialisation (sans Word)**
  - Porter `forest` → `engine/` (C#, .NET Standard 2.0), fichier par fichier.
  - Reprendre `LatexToOmml` → `serialization/`.
  - Porter `fixtures.js` → tests xUnit. **Critère de sortie : tous les cas verts.**
  - Reprendre `host-contract/`.

- **Phase 2 — Adapter VSTO minimal (chemin critique)**
  - `ThisAddIn` + ribbon élagué + `KeyboardInterceptor` + trigger Ctrl+Espace.
  - `WordContextReader` (lecture bornée paragraphe).
  - `OMathInserter` **extrait** proprement (séquence anchor CC + InsertXML + MoveRight).
  - `CcMetaResolver`/`MCMeta` (anchor CC), `ZoneCleaner`, revert.
  - Popup WPF (`SuggestionPopupWindow` + `WpfMathAdapter`) au caret.
  - Câblage : Ctrl+Espace → `engine.analyze` → popup → sélection → OMML → insert + CC.
  - **Critère : taper `1/x+1`, Ctrl+Espace, popup 2 candidats, Tab insère l'OMath, Ctrl+Z revert.**
  - *Extension 2026-06-10* : ribbon enrichi — menu Colonnes 1→4 restauré (port DocMath
    sans bookmarks, POC `scripts/poc-formattedtext-cc.ps1` PASS) + bouton Paramètres
    (culture FR/US, `EngineCulture` threadée dans `Analyze`, store JSON `%APPDATA%`).
    ADR [`Feat-ribbon-columns-settings-culture`](docs/dev/decisions/2026-06-10-Feat-ribbon-columns-settings-culture.md).

- **Phase 3 — Édition in-place + feedback**
  - `EditModeController` (clic dans OMath → retrouve source via CC → ré-édite).
  - Module feedback complet.

- **Phase 4 — NER isolé (intégration différée)**
  - Reprendre `Detection/` + `models/distilmult-v5/`, tests NER.
  - **Non branché** dans le flux live (prêt pour l'auto-détection ultérieure).

- **Phase 5 — Build, signature, doc, polish**
  - Build VSTO Release signé, installeur, CLAUDE.md/README du nouveau repo, ADRs de fondation.

---

## 6. Risques & points de vigilance

- **Fidélité OMML** : `forest` émet plus de constructions LaTeX que `LatexToOmml` n'en couvre
  (intervalles `[a;b]`, valeur absolue `\left|`, normes `\left\|`, `\mathbin{/\!/}`, `\bmod`,
  `\colon`, `\setminus` ensembliste, matrices `pmatrix`). → **Auditer la couverture** en Phase 1
  et compléter `LatexToOmml` au besoin (avec tests). C'est le principal point d'intégration.
- **Word API** : respecter l'ordre d'insertion validé (ADR anchor-cc §1bis) — ZWSP plain →
  math → InsertXML → anchor CC → `MoveRight` escape. Ne jamais remettre `LockContentControl=true`
  sans tester `cc.Delete` au revert (mémoire `feedback_word_api_workflow` de DocMath).
- **Extraction de `InsertOMathAt`** : le code vit dans un monolithe `SuggestionService` (~750 LoC)
  mêlé au merging. Extraction chirurgicale nécessaire — ne porter QUE le chemin single-OMath.
- **ONNX/Bay Trail** : garder `Microsoft.ML.OnnxRuntime` **pinné 1.16.3** (≥1.17 crashe sur CPU SSE4.2-only).
</content>
</invoke>
