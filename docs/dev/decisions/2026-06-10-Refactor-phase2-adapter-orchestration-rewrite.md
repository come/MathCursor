# Refactor — Phase 2 beta-clean : réécriture de l'orchestration adapter + ribbon à neuf

**Date :** 2026-06-10
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** `PLAN.md` §4-5 (consolidation beta) ; [2026-05-19-Feat-anchor-cc-pattern.md](2026-05-19-Feat-anchor-cc-pattern.md) (séquence d'insertion conservée à l'identique) ; [2026-06-02-Feat-omml-insertion.md](2026-06-02-Feat-omml-insertion.md) (chemin OMML conservé) ; commits `5fff99a` (Phase 1 prune), `d77bcfb` (moteur forest 280/280), `47ebf83` (couverture LaTeX→OMML)

## Citation acté

> « oui repars de 0 la dessus. Et refais completment le ribbon. » — utilisateur, 2026-06-10
> (en réponse au plan : « supprimer le monolithe SuggestionService et écrire un
> ConversionController + OMathInserter neufs, plutôt que de tenter de sauver
> davantage des 2647 lignes »)

## Contexte

Après le portage du moteur forest (Phase 1b, 280/280 fixtures) et la
sécurisation du pont LaTeX→OMML (Phase 1c, 341 candidats sans fuite),
l'adapter VSTO restait à câbler. L'audit montre que l'orchestration
existante n'est pas réutilisable :

- `Host/SuggestionService.cs` (**2647 lignes**) est couplé à l'ancien moteur
  supprimé (LatticeEngine, MathEngine v2, `Core.Resolution`, Pipeline/Merging/
  Session/ListMode) — c'est de la glue morte, sauf ~360 lignes de primitives
  d'insertion **validées en POC** (`InsertOMathAt`, `BuildOMathViaOmml`,
  `DecideOMathTyping`).
- `UI/SuggestionPopupWindow.cs` (863 l.) est structurée autour du modèle
  d'ambiguïté de l'ancien moteur (AmbiguityAlternative/AmbiguityMatch/
  PatternCompletion/SourceMutation/sidecars). Le modèle forest est plus
  simple : une **liste classée de candidats LaTeX** + décision popup/auto.
- `RibbonCallback.cs` (1400 l.) + `Ribbon.xml` : majoritairement des POC
  debug et des features hors périmètre beta (colonnes, constructions,
  cheatsheet, inspector).
- `ThisAddIn.cs` initialise l'ancien moteur et un polling 200 ms.

## Décision

### 1. Orchestration réécrite de zéro, primitives extraites

- **`Host/OMathInserter.cs`** (nouveau) : extraction fidèle de la séquence
  d'insertion validée — normalize bornes (`SetRange` + readback) →
  `ZoneCleaner.ClearZone` → ZWSP plain `Font.Hidden` (ordre liste/hors-liste
  conservé) → OMML via `MathCursor.Serialization.LatexToOmml` + `InsertXML`
  chirurgical sur range placeholder 1-char → `DecideOMathTyping`
  (Display/Inline, Left) → anchor CC en DERNIER + Tag JSON `MCMeta` →
  échappement caret `MoveRight`. Zéro dépendance au reste (Word + CCMeta +
  ZoneCleaner + Serialization uniquement). L'early-bail `LatexToUnicodeMath`
  est retiré : la couverture est désormais garantie par les tests de
  sérialisation (Phase 1c).
- **`Host/ConversionController.cs`** (nouveau) : orchestrateur du flux manuel —
  lecture ¶ borné (`WordContextReader`) → calcul de span (remontée jusqu'à
  délimiteur / stopword / OMath / début ¶, logique reprise de
  `ManualTriggerController.ComputeSpanStart`, stopwords+délimiteurs FR portés
  en table depuis `data/locale/fr.yml`) → `ForestEngine.Analyze` → popup →
  commit sous `UndoRecordScope` (Ctrl+Z = un seul undo) via
  `ParagraphPositionTranslator` (string→positions internes). Ctrl+Espace
  répété popup ouverte = **extension itérative** d'un cran à gauche
  (comportement conservé).
- **`SuggestionService.cs` supprimé**, ainsi que les satellites de l'ancien
  moteur : `EquationHandleRegistry` (sidecars), `ContextResolveEventArgs`,
  `LastActionTracker/Snapshot` (pipeline), `ManualTriggerController`
  (remplacé), `CcSticky` (caduc depuis l'anchor pattern), `PocStepRunner`
  (POC debug).

### 2. Popup candidats simplifiée (modèle forest)

`UI/SuggestionPopupWindow.cs` réécrite : `Show(candidats LaTeX[], x, y)` —
liste verticale une-ligne-par-candidat, rendu `MixedLatexRenderer`, nav
↑/↓, « + N autres » au-delà de 2, clic ou Enter commit, lien « Signaler une
erreur ». Les acquis durs sont conservés : `WS_EX_NOACTIVATE | TOOLWINDOW`
(la popup ne vole jamais le focus de Word), fade in/out, nav-mode opt-in
(pas de surlignage avant la première flèche). La popup est montrée **même à
candidat unique** (l'élève voit ce que Tab va produire — trigger manuel =
confirmation visuelle) ; la décision `popup`/`auto` du moteur détermine
seulement le nombre de candidats listés.

### 3. Ribbon refait à neuf (sobre)

Un seul onglet `MathCursor`, trois boutons : **Convertir** (Ctrl+Espace),
**Signaler un problème**, **À propos**. Icônes `imageMso` natives (plus de
PNG embarqués, plus de callbacks getImage). Disparaissent : groupe TabHome,
colonnes, constructions grisées, cheatsheet, inspector, ~20 boutons POC
debug. `RibbonCallback.cs` réécrit (~100 l.), `Strings.cs` réduit aux
libellés réellement utilisés.

### 4. Événements natifs, pas de polling

`ThisAddIn` réécrit : plus de `DispatcherTimer` 200 ms. Le mode édition
(popup revert sur OMath à nous) est piloté par **`WindowSelectionChange`**
(event natif VSTO) → `EditModeController.Sync` (conservé, débarrassé de ses
deps mortes). La popup suggestion se ferme sur déplacement du caret. Le
hook clavier (`KeyboardInterceptor`, conservé tel quel) gère Ctrl+Espace /
Tab / Enter / ↑ ↓ / Esc.

### 5. NER non démarré (différé Phase 4)

`Detection/` (MathNerDetector ONNX + WordPiece) et `Host/Detection/*`
restent compilés mais **ne sont plus initialisés au startup** — l'add-in
démarre sans modèle sur disque. L'auto-détection reviendra en Phase 4
derrière un réglage.

## Tradeoff & alternatives écartées

- **Sauver davantage de SuggestionService** (extraction incrémentale) :
  les 2300 lignes restantes orchestrent des sous-systèmes supprimés
  (mergers, list-mode, sidecars, polling NER) ; les adapter coûterait plus
  en risque de régression que réécrire un contrôleur de ~300 lignes dont
  chaque étape s'appuie sur des helpers déjà validés.
- **Adapter la popup existante au moteur forest** : son modèle (spots
  d'ambiguïté + splice + préférences par règle) n'a pas d'équivalent forest
  (le classement de candidats remplace tout ça) ; la garder aurait maintenu
  900 lignes pour en utiliser 150.
- **Commit direct sans popup quand le moteur dit `auto`** : écarté pour la
  beta — le trigger manuel doit montrer ce qui va être inséré (prévisibilité
  PAP, cf. brief ergonomie « comportement prévisible ») ; on pourra ajouter
  un réglage « insertion directe » plus tard.
- **Garder le polling 200 ms** : contraire à la règle projet « triggers
  explicites + events natifs » ; inutile sans NER actif.

## Conséquences

- **Code supprimé** : `Host/SuggestionService.cs` (2647 l.),
  `RibbonCallback.cs` (1400 l., réécrit), `UI/SuggestionPopupWindow.cs`
  (863 l., réécrite), `Host/ManualTrigger/`, `Host/EquationHandleRegistry.cs`,
  `Host/ContextResolveEventArgs.cs`, `Host/LastAction*.cs`,
  `Host/CCMeta/CcSticky.cs`, `Host/CCMeta/PocStepRunner.cs`.
- **Code nouveau** : `Host/OMathInserter.cs`, `Host/ConversionController.cs`,
  `Ribbon.xml` + `RibbonCallback.cs` + `ThisAddIn.cs` + `Strings.cs` réécrits.
- **Code conservé intact** : ZoneCleaner, WordContextReader,
  KeyboardInterceptor, CCMeta (Resolver/MCMeta/Json/Sha1), Caret,
  Feedback (7 fichiers + dialog + bundle), UndoRecordScope,
  AutocorrectNormalizer, ParagraphPositionTranslator, Detection (NER),
  EditModePopupWindow, WpfMathAdapter, MixedLatexRenderer.
  `EditModeController` : 2 imports morts retirés, logique inchangée.
- **`MathCursor.csproj`** : références projets → `engine/`, `serialization/`,
  `host-contract/` (nouveaux chemins) ; liste `<Compile>` purgée ; packages
  ONNX 1.16.3 (pinné) + WpfMath 2.1 conservés.
- **API publique** : aucune (add-in final).
- **Tests** : couche pure inchangée (281 verts) ; tests adapter purgés des
  fixtures de l'ancien moteur.

## Validation post-fix

1. Build MSBuild VS2022 de la solution VSTO → 0 erreur.
2. Sur machine utilisateur (Word requis) : taper `1/x+1`, Ctrl+Espace →
   popup 2 candidats ; Tab insère l'OMath (inline) ; Ctrl+Z restaure le
   texte source en un coup ; re-conversion sur ¶ vide → Display ;
   clic dans l'OMath → popup edit → revert OK.
3. Ribbon : 3 boutons visibles, Convertir équivalent à Ctrl+Espace,
   Signaler ouvre le FeedbackDialog pré-rempli.
