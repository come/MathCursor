# Cartographie MathCursor — 5 axes d'extensibilité

**Date** : 2026-05-13 (rev. après refacto DDD bigbang P2.X)
**Branche** : `refactor/big-bang-clean-arch`
**Statut** : Référence pour le refacto archi (étape 1 du plan)
**Source** : briefs `MATHCURSOR_HARNESS_BRIEF.md` + `MATHCURSOR_ARCHITECTURE_EXTENSIBILITE.md`

---

## Vocabulaire

| Axe | Description | Mécanisme cible | Statut chez nous |
|---|---|---|---|
| **A — Vocabulaire mathématique** | Ajouter une construction notationnelle (matrice, dérivée, intégrale…) | Strategy + Visitor sur l'AST | majoritairement présent, sans abstraction `IConstructStrategy` |
| **B — Domaine** | Ajouter une discipline (chimie, physique avec unités…) | Router + parseurs sibling | absent — un seul parseur math |
| **C — Locale d'entrée** | Ajouter une langue (en, de) | Lexer + lexique pluggable + NER spécifique | partiel — FR/EN mélangés dans `Vocabulary.cs`, NER multilingue mais sans abstraction `ILocaleLexer`/`ILocaleNER` |
| **D — Personnalisation user** | Raccourcis / alias / prefs user | Overlay YAML en cascade | absent — pas de mécanisme user-side |
| **E — Cible de sortie** | OMath / LaTeX / MathJax / Unicode | Sérialiseurs typés à partir d'AST + LaTeX pivot | présent (OMath via `LatexToUnicodeMath`), sans abstraction `IOutputSerializer<TFormat>` |

Synthèse : **A** et **E** sont concrètement implémentés mais sans contrat formel. **C** est partiellement présent mais mal cloisonné (FR hardcodé dans le Core). **B** et **D** n'existent pas en tant que dimensions de design — leur introduction ouvrira de nouveaux fronts.

---

## core-csharp/src/MathCursor.Core/ — racine

| Fichier | Axe(s) | Rôle | Dette | Plan |
|---|---|---|---|---|
| `ILatexEngine.cs` | A | Contrat public du moteur : `Convert(rawSpan) → LatexSuggestion[]`. Inclut `NotImplementedEngine` placeholder. | API monolithique : un seul point d'entrée pour tout. | Conserver tel quel (compat tests). Ajouter `IDomainParser` à côté pour étape 6. |
| `LatticeEngine.cs` | A | Façade : `Convert` (top-K suggestions) + `ConvertWithAmbiguity` (top-1 + ambig). Orchestrateur Lex→TopK→Parse→Render. | OK — bien isolé. | Implémentera `IDomainParser` (DomainId="math") en étape 3. |
| `ZoneResolver.cs` | A + D-like | Pipeline complet avec application des pins/hints (sidecar v2) + session prefs (V→∀). | Cœur complexe (516 lignes) mais bien factorisé : `ApplyPreferences` (source-mut), `ResolveBestAlt` (précédence), splice loop. | Conserver. La dépendance vers `Resolution/*` est saine. |
| `LatexToUnicodeMath.cs` | E | LaTeX → UnicodeMath (consommé par Word `OMaths.BuildUp`). Pure projection. | Utilise Regex pour les environnements LaTeX `\begin{cases}` (cf. MC0001 — à examiner). | Implémentera `IOutputSerializer<string>` en étape 3. Refacto Regex→parser si MC0001 active. |
| `RenderOptions.cs` | cross-axe | Options globales (`MultSymbol`). | Singleton sur `LatexRenderer.GlobalOptions` — anti-pattern léger mais isolé. | OK pour l'instant. |

---

## core-csharp/src/MathCursor.Core/Lattice/ — moteur

| Fichier | Axe(s) | Rôle | Dette | Plan |
|---|---|---|---|---|
| `Lexer.cs` | A + C (via Vocabulary) | Source → DAG d'edges pondérés. | Délègue à `Vocabulary` (FR/EN mélangés). | Sera locale-aware en étape 5. Pas de modification de logique, juste injection du lexique locale. |
| `Vocabulary.cs` | **C principalement** | Dictionnaires keywords FR + EN, functions, grec, operators. | **Mélange clair des axes** : FR (`somme`, `racine`, `intégrale`…) ET EN (`sum`, `sqrt`…) côte-à-côte. | **Splitter en ressources YAML** : `locales/fr/keywords.yaml` + `locales/en/keywords.yaml` (étape 5). |
| `LatticePathFinder.cs` | A pur | Dijkstra top-K. | Aucune. | Aucun changement. |
| `LatticeEdge.cs` | A pur | Data structure. | Aucune. | Aucun changement. |
| `Parser.cs` | A + C léger | Edges → AST. Reconnaît les scopes (lim, sum, int…). | `switch (keyword.Value)` sur les mots-clés (FR + EN mappés en symboles canoniques par Vocabulary). | Étape 4 : pas concerné. Étape 5 : keywords passent par `ILocaleLexer`. |
| `LatexRenderer.cs` | E + A (switch AST) | AST → LaTeX. | **`switch (node)` exhaustif** sur 18+ types AST (Atom, Hole, Const, Bin, Sup, Group, Frac, Sqrt, Vec, Angle, Func, Sum, Lim, Int, Interval, FuncDef, VectorCoordinates, MultiLineBlock). | **Étape 4 cible prioritaire** : conversion en `IAstVisitor<string>`. Chaque future construction ajoute une méthode au visitor, pas un `case`. |
| `AlternativeGenerator.cs` | A (+ B/C léger) | 9 scanners émettent `AmbiguityMatch` (vec/paren/bracket, V→∀, decimal vs mult, etc.). | Classe statique géante (~1100 lignes). Les 9 scanners sont déjà bien séparés en fonctions privées. | Étape 3 cible : convertir chaque scanner en `IConstructStrategy` (ou conserver le pattern statique si chaque scanner = pure fonction). À évaluer. |
| `AmbiguityDetector.cs` | A pur | Détection des segments ambigus entre paths top-K (algorithm §3). | Aucune. | Aucun changement. |

---

## core-csharp/src/MathCursor.Core/Lattice/Ast/ — types AST

| Fichier | Axe(s) | Rôle | Dette | Plan |
|---|---|---|---|---|
| `AstNode.cs` | A pur | Type de base. | Aucune. | Ajouter méthode `Accept<TResult>(IAstVisitor<TResult>)` en étape 4. |
| `AstNodes.cs` | A pur | 18 nœuds concrets (Atom, Bin, Group, Vec, Angle, Frac, Sqrt, Lim, Sum, Int, FuncDef, VectorCoordinates, MultiLineBlock, Interval, Hole, Const, Unary, Sup, Sub, Func). | Aucune ; chaque type est une donnée immutable. | Étape 4 : `Accept` overrides. Chaque nouveau type AST = un nouveau fichier ou un append ici, sans modifier les visiteurs existants (étape 4 débloque ça). |

---

## core-csharp/src/MathCursor.Core/Resolution/ — désambig contextuelle

Cette zone vise déjà l'axe D (préférences user-bound) mais l'expose au niveau session, pas user-persistent.

| Fichier | Axe(s) | Rôle | Dette | Plan |
|---|---|---|---|---|
| `ResolutionSidecar.cs` | A (sidecar par zone) | Pins/overrides/votes attachés à une zone. | Aucune. | Aucun changement. |
| `SpanPin.cs`, `RulePin.cs`, `SpanOverride.cs` | A | Data structures de pins. | Aucune. | Aucun changement. |
| `MatchSignature.cs` | A | Identifiant stable d'un match (signature par rule + position). | Aucune. | Aucun changement. |
| `SidecarSerializer.cs` | A | JSON v2 serialize/deserialize. | Aucune. | Aucun changement. |
| `SidecarMerger.cs` | A | Fusion de sidecars lors d'un cross-merge. | Aucune. | Aucun changement. |
| `GlobalContext.cs` | A + D (hub signaux session) | Hub central des `IContextSignal`. Snapshot pour le scorer. | Aucune. | Conserver. Pourra accueillir un `UserPreferencesSignal` (étape 7). |
| `IContextSignal.cs` | A | Contrat des signaux contextuels. | Aucune. | Modèle propre. **Déjà-une-Strategy** au sens du brief. |
| `ScoringHints.cs`, `ContextScorer.cs`, `ContextSnapshot.cs` | A | Agrégation des signaux → hints. | Aucune. | Aucun changement. |
| `Signals/SidecarSignal.cs` | A | Signal qui lit le sidecar courant. | Aucune. | Aucun changement. |
| `Signals/ParagraphResolutionsSignal.cs` | A | Signal qui apprend des pins ¶ récents. | Décay exponentiel récent (commit d11b77d). | Aucun changement. |
| `ZoomLevel.cs` | A | Enum (Span/Zone/Paragraph). | Aucune. | Aucun changement. |

**Verdict Resolution/** : architecture **déjà conforme** au pattern interne du brief (`IZoneDetector` / `IContextSignal` Strategy). Pas de dette ici.

---

## host-contract-csharp/src/MathCursor.HostContract/ — contrats plateforme

| Fichier | Axe(s) | Rôle | Dette | Plan |
|---|---|---|---|---|
| `IDocumentHost.cs` | L2 boundary | Interaction document : lire contexte, insérer/éditer équation, events curseur. | Aucune. Interface stable. | Aucun changement. **C'est la "frontière 4 couches" du CLAUDE.md.** |
| `IEquationStore.cs` | L2 boundary | Persistance des sources (CustomXMLParts VSTO). | Aucune. | Aucun changement. |
| `IEditorSurface.cs` | L3 boundary | UI suggestions + edit mode. | Aucune. | Aucun changement. |
| `IUserFeedback.cs` | L3 boundary | Logging local opt-in. | Aucune. | Aucun changement. |
| `Types.cs` | L2 boundary | DTOs partagés (`TextZone`, `EquationHandle`, `EquationOutput`, `CaretMovedListener`, etc.). | Aucune. | Aucun changement. |
| `IsExternalInit.cs` | infra | Shim pour `init` setters en .NET Standard 2.0. | Aucune. | Aucun changement. |

**Verdict HostContract/** : déjà une couche **abstraction pure**. Le Core ne la référence d'ailleurs PAS (cf. règle CLAUDE.md). À conserver telle quelle ; pourra ajouter `IDomainParser` / `IOutputSerializer<TFormat>` côté Core.

---

## adapter-vsto/src/MathCursor/Host/ — couche L2 (VSTO Word)

Refacto DDD bigbang couvrant P2.1 → P2.17 (17 itérations). Sous-dossiers thématiques en place :

| Dossier | Axe(s) | Contenu | Statut |
|---|---|---|---|
| `Bookmarks/` | L2 | Registry de bookmarks d'équations (1 fichier — `EquationBookmarkRegistry`). | OK (P2.8) |
| `Caret/` | L2 | `CaretPositioner` (positionnement post-insert) + `CaretScreenPositionReader` (Win32 isolation). | OK (P2.15, P2.17) |
| `Detection/` | L2 (axe C léger) | `ZoneRefiner` (raffinement de zone post-NER, logique pure testée). | OK (P2.14) |
| `EditMode/` | L2 | `EditModeController` — détection entrée/sortie OMath, bounded context. | OK (P2.10) |
| `Feedback/` | L3 + `IUserFeedback` impl | `FeedbackReport`, senders (Clipboard / HTTP), JSON, factory. 8 fichiers. | OK |
| `Inserters/` | L2 (axe E côté plateforme) | 4 stratégies d'insertion OMath (Pure fast path / Inline splice / Atomic range / OMath finder) — `InsertContext`. | **Déjà Strategy de fait.** Formaliser `IInserter` utile seulement si on en prévoit un 5e. |
| `Layout/` | L2 | `PostCommitLayoutFinalizer` — alignement OMath post-commit. | OK (P2.13) |
| `ManualTrigger/` | L2 | `ManualTriggerController` — gestion Ctrl+Espace explicite (301 lignes, gros morceau extrait). | OK (P2.16) |
| `Merging/` | L2 | Mergers cross-paragraphe (Intra, RevertedMultiLine, MarkerChain, CasesChain) + pipeline + `IZoneMerger`. | **Déjà Strategy (IZoneMerger).** Conforme. |
| `Pipeline/Stages/` | L2 | Pipeline de commit en stages (Merger → Resolver → Snapshot → Renderer → Inserter → Store → Layout → Caret). | **Pattern Pipeline formalisé via `ICommitStage`.** Très propre. |
| `Session/` | L2 | État de session (`EquationSession`, `IterativeExpansion`, `ListModeAnchor`, `RevertedMultiLineZone`). | OK |
| **Top-level** | L2 mixte | `SuggestionService` (1775 lignes), `WordContextReader` (165), `VstoEquationStore` (157), `KeyboardInterceptor`, `OMathStagingService`, `OMathXmlCache`, `ParaXmlPrefetcher`, `WordParaXmlSource`, `InlineOMathSplicer`, `AutocorrectNormalizer`, `ListMode*`, etc. | God class principale : `SuggestionService`. Reste à dépouiller mais le **plus gros** est déjà extrait. |

**Note** : `adapter-vsto/src/MathCursor/Detection/` (top-level, **hors Host/**) contient `MathNerDetector.cs` + `WordPiece/WordPieceTokenizer.cs` + `DetectedZone.cs`. Pas migré sous `Host/Detection/` — ces 3 fichiers restent au niveau adapter top-level. Hypothèse : le NER est une dépendance technique transverse (ONNX Runtime) plutôt qu'un domaine DDD du Host. À confirmer.

**Verdict Host/** : couche L2 **excellente isolation DDD** post-bigbang. 11 sous-domaines explicites + un Pipeline formalisé. L'archi cible du brief (Strategy/Visitor) est **déjà majoritairement atteinte côté Adapter**. Hors scope du refacto archi-axes (qui vise le Core).

---

## adapter-vsto/src/MathCursor/Detection/ — couche L2 (NER multilingue)

| Fichier | Axe(s) | Rôle | Dette | Plan |
|---|---|---|---|---|
| `MathNerDetector.cs` | L2 + C (NER) | DistilBERT multilingue (B-MATH / I-MATH labels). 1 modèle ONNX pour toutes les locales. | Pas d'abstraction `ILocaleNER`. Le détecteur est instancié direct avec chemin modèle. | Étape 5 : extraire `INerDetector` ou `ILocaleNER`. Modèle multilingue actuel reste un seul fichier, mais l'abstraction permet de switcher mono-locale plus tard si besoin. |
| `WordPiece/WordPieceTokenizer.cs` | infra | Tokenizer pure C# pour le modèle DistilBERT. | Aucune. | Aucun changement. |
| `DetectedZone.cs` | L2 | DTO résultat. | Aucune. | Aucun changement. |

**Note** : il y a aussi `adapter-vsto/src/MathCursor/Host/Detection/ZoneRefiner.cs` (untracked, WIP user). Probable migration de la détection vers `Host/Detection/`.

---

## Dette structurelle identifiée — synthèse

### Niveau 1 — Bloquant pour les 5 axes
1. **`Vocabulary.cs` mélange FR + EN** dans des dictionnaires statiques C# → bloque axe C. **Étape 5 cible.**
2. **`LatexRenderer.cs` switch exhaustif sur AST** → freine axe A (toute nouvelle construction = modifier le switch). **Étape 4 cible.**

### Niveau 2 — Sérieux mais contournable
3. **Pas d'`IConstructStrategy` formalisé** : les 18 nœuds AST sont définis comme classes pures, mais chaque introduction ajoute un fichier ET modifie des switches partout. **Étape 3 + 4.**
4. **`LatexToUnicodeMath.cs` n'implémente pas `IOutputSerializer`** : isolé fonctionnellement, mais sans contrat. **Étape 3 (ajout pur).**
5. **`MathNerDetector` non abstrait** : modèle multilingue couvre le besoin actuel, mais la facade `ILocaleNER` manquera pour ajouter un modèle anglais dédié. **Étape 5.**

### Niveau 3 — À surveiller (pas urgent)
6. **`SuggestionService.cs` ~1775 lignes** : god class côté Adapter. Le bigbang DDD P2.1-P2.17 a déjà sorti 17 bounded contexts (Bookmarks, Caret, Detection, EditMode, Feedback, Inserters, Layout, ManualTrigger, Merging, Pipeline, Session, OMathStagingService, etc.). Reste l'**orchestrateur principal** plus une poignée d'utilitaires top-level (`WordContextReader`, `ListModeController`, `KeyboardInterceptor`, `InlineOMathSplicer`...). Pas dans le scope du refacto archi-axes — c'est de la mécanique L2 propre à VSTO.
7. **`LatexToUnicodeMath.cs` utilise Regex** sur `\begin{cases}` / `\begin{pmatrix}`. **Cible MC0001** une fois l'analyzer activé — soit refacto en parser, soit ADR de suppression.
8. **`AlternativeGenerator.cs` ~1100 lignes** : statique avec 10 scanners internes (incluant le nouveau `ScanDecoratedTwoThreeUpper` du commit 9ab248b). Déjà bien factorisé. Pas urgent à splitter en `IConstructStrategy` séparées, sauf si on prévoit une 11e construction (matrices, dérivées).

### Niveau 0 — Pas de dette
- Toute la couche `Resolution/` (Sidecar + Signals + Scorer) : **déjà conforme** au pattern Strategy.
- `host-contract-csharp/*` : abstraction pure, à conserver.
- `Pipeline/Stages/` côté adapter : Pipeline déjà formalisé.
- `Merging/` : `IZoneMerger` déjà Strategy.
- `Inserters/` : 4 stratégies coexistent sans formalisme `IInserter` mais comportement Strategy de fait.

---

## Plan refacto étapes 2-5 (priorisé par dépendance)

### Étape 2 — Interfaces (Abstractions) — 1j, ajout pur

Créer un nouveau projet `core-csharp/src/MathCursor.Core.Abstractions/` (.NET Standard 2.0) avec :

```
MathCursor.Core.Abstractions/
├── IConstructStrategy.cs       ← axe A
├── IDomainParser.cs            ← axe B (placeholder)
├── ILocaleLexer.cs             ← axe C
├── ILocaleNER.cs               ← axe C (côté adapter mais contrat ici)
├── IOutputSerializer.cs        ← axe E (générique TFormat)
├── IAstVisitor.cs              ← pattern interne (débloque étape 4)
└── ParseContext.cs             ← porteur de locale courante
```

Aucun type existant n'implémente. Aucun comportement ne change. ADR à créer.

### Étape 3 — Implémentation par les types existants — 1.5j, refacto type-safe

- `LatticeEngine` implémente `IDomainParser` (DomainId="math").
- `LatexToUnicodeMath` implémente `IOutputSerializer<string>` (FormatId="omath-unicodemath").
- `LatexRenderer.Render` devient une implémentation de `IAstVisitor<string>` (FormatId="latex-pivot").
- À ce stade, **les switches dans LatexRenderer ne changent pas encore** — c'est l'étape 4 qui les remplace.

Aucun test ne casse. Tests existants couvrent le comportement.

### Étape 4 — Visitor sur l'AST — 1j, refacto

Conversion du `switch (node)` exhaustif en `IAstVisitor<TResult>` avec dispatch via méthode virtuelle `AstNode.Accept`. Concerne :
- `LatexRenderer` (mainstream)
- `LatexToUnicodeMath` (déjà par regex pour cases/pmatrix ; refacto secondaire après MC0001)
- `Parser` (potentiellement, à évaluer — beaucoup de switches sur Edge.Type, pas Node.Type)

Refacto type-safe : ajouter un nouveau nœud AST nécessitera d'ajouter une méthode au visitor (compile error si oubli). Plus de drift silencieux.

### Étape 5 — Sortir les chaînes FR du Core — 0.5j

```bash
grep -rE "\b(racine|fraction|puissance|intégrale|matrice|dérivée|somme|vecteur|chapeau|appartient|forall|exists|union|inter)\b" core-csharp/src/MathCursor.Core/
```

Migrer vers `locales/fr/keywords.yaml` chargé par `FrenchLocaleLexer : ILocaleLexer`. Le Core ne contient plus de mot français. `Vocabulary.cs` devient `FrenchLocaleLexer.Build()` côté adapter (ou côté locale).

**À ce stade : activation de MC0002** (le Core est cleanly isolé, l'analyzer peut bannir `Microsoft.Office.*` et `System.Windows.*` sans risque de faux positif).

---

## Anti-patterns à surveiller (futurs signaux Tier 2)

Issu du brief extensibility :

1. **`switch (node.Type)` / `switch (domain)` / `switch (locale)`** dans le code partagé → analyzer MC0003 (déjà prévu) couvre la première forme.
2. **Paramètre `domain` / `locale` / `language`** dans une méthode du Core → signal Tier 2.
3. **Chaîne hardcodée en français** dans `MathCursor.Core` → signal Tier 2 (régex sur dossier ciblé).
4. **God strategy** : une `IConstructStrategy` qui gère plusieurs constructions → revue manuelle.
5. **Logique métier dans `IOutputSerializer`** : le serializer doit être pure projection → signal Tier 2 si méthode publique non-`Serialize`.

À ajouter au harnais (Phase 2-8) une fois les abstractions en place.

---

## Mapping vers les extensions prévues

| Extension prévue | Axe principal | Touche aussi | Brief dédié à créer |
|---|---|---|---|
| **Matrices** | A | — | `briefs/EXT_MATRICES.md` |
| **Dérivées** | A | C léger (mots-clés `dérivée de`) | `briefs/EXT_DERIVEES.md` |
| **Chimie** | B (nouveau) | C (notation FR vs EN) | `briefs/EXT_CHIMIE.md` |
| **Anglais (en-US)** | C | — | `briefs/EXT_MULTILINGUE.md` |
| **Allemand (de-DE)** | C | — | (idem) |
| **Raccourcis user** | D (nouveau) | — | `briefs/EXT_RACCOURCIS_CUSTOM.md` |
| **Cible MathJax (Obsidian)** | E | — | `briefs/EXT_CIBLES_OUTPUT.md` |

Chaque extension peut être traitée en isolation **après** étapes 2-5. Aucune ne nécessite la connaissance des autres.

---

## Conclusion

Le bigbang DDD P2.X a livré **une couche L2 (Adapter) déjà à l'archi cible** : 11 bounded contexts, Pipeline formalisé via `ICommitStage`, 3 Strategy patterns (Mergers, Signals, Inserters de fait).

Le **Core est conforme à l'esprit** de l'archi 5-axes mais sans contrats formels :
- Strategy déjà présent (`IContextSignal`, `IZoneMerger`, `ICommitStage`, `Inserters/`).
- AST + LaTeX pivot universel est l'épine dorsale.

La dette principale reste dans **Vocabulary** (axe C mélangé FR+EN) et **LatexRenderer** (switch exhaustif sur AST, frein axe A). Les étapes 2-5 résolvent ces deux points sans toucher au comportement runtime.

Aucun refacto n'est sur le chemin critique pour résoudre des bugs courants. Le refacto archi sert l'**ajout de futures extensions** (matrices, chimie, EN/DE, raccourcis user, MathJax) sans débordement entre axes.

**Implication pratique du bigbang DDD** : étape 4 (Visitor) devient le **plus gros bénéfice immédiat** pour le Core, car c'est ce qui débloque l'ajout de constructions sans modifier les switches. Étape 5 (sortie chaînes FR) reste rapide. Étapes 6-8 (DomainRouter, ShortcutResolver) sont du pur ajout.

Prochaine étape : **étape 2** — créer `MathCursor.Core.Abstractions/` avec les 7 interfaces. Ajout pur, aucun test ne casse.
