# Feat — Encadrés visuels : callouts typés + notation `\boxed`

**Date :** 2026-06-20
**Kind :** Feat
**Température :** molle
**Statut :** acté — ⚠️ **volet A1 rétracté** (voir Superseded by)
**Supersedes :** —
**Superseded by (partiel, A1) :** [2026-06-22-Refactor-boxed-post-hoc-only-out-of-engine](2026-06-22-Refactor-boxed-post-hoc-only-out-of-engine.md) — `boxed` n'est plus une notation saisie au moteur (A1 retiré) ; A2 (serializer), A3 (walker), A4 (bouton) et le volet B (callouts) restent actés.
**Lié à :** [2026-05-19-Feat-anchor-cc-pattern](2026-05-19-Feat-anchor-cc-pattern.md) (retracté, pipeline d'insertion historique), [2026-06-11-Feat-hash-source-map-no-cc](2026-06-11-Feat-hash-source-map-no-cc.md) (pipeline actuel réutilisé), plan validé `recursive-napping-cocke.md`

## Citation acté

> « plutot peripherique comme truc.. pas dans le core » — utilisateur, 2026-06-20

> « sauf pour le boxed en effet » — utilisateur, 2026-06-20

> Callouts = « Jeu typé (doc) » ; boxed = « boxed() ou boxed  + Bouton » —
> réponses utilisateur aux questions de cadrage, 2026-06-20, puis approbation
> explicite du plan.

## Contexte

L'utilisateur veut rédiger des notes de maths **visuellement chouettes** dans
Word, sur le modèle d'un PDF de cours (ASE203) : encadrés colorés typés
(THEOREM rouge, DEFINITION bleu, WORKED EXAMPLE vert, COEFFICIENT RULES rose —
barre d'accent gauche + fond teinté + titre en petites capitales) et formules-
résultats entourées d'un cadre fin (`\boxed` LaTeX, `m:borderBox` OMML).

Deux besoins de **nature différente** :
- l'encadré de document (callout) est de la **mise en page** — sans rapport avec
  la sémantique math ni le moteur de conversion ;
- le cadre autour d'une formule (`\boxed`) fait partie de la **notation math** —
  il a vocation à être une notation first-class, tapable en sténo.

L'utilisateur a explicitement cadré le périmètre : rester **périphérique
(adapter-vsto)**, **sauf le `boxed`** qui doit passer par le **core**.

## Décision

Deux volets, découpés par couche conformément à la contrainte utilisateur.

### Volet A — `boxed`, notation math first-class (passe par le core)

La chaîne existante (moteur forest → LaTeX → `LatexToOmml.Convert` → OMML →
walker `OmmlToOMathBuilder` → OMath natif) est réutilisée intégralement ; on
greffe `boxed` à chaque maillon :

- **A1 — Data moteur (sténo → `\boxed{…}`)** : une **entrée data seule**, zéro
  code moteur. Dans `data/engine/symbols.json` :
  `"boxed": { "shape": "prefix", "arity": 1, "class": "STRONG", "looseness": "APP", "render": "\\boxed{{0}}" }`
  (calqué sur `sqrt`). Le Lexer/Parser/Renderer génériques produisent
  `\boxed{arg}` automatiquement. Alias FR optionnels (`encadre`, `cadre`) dans
  `data/engine/cultures.json`. Fixtures ajoutées (`boxed x`, `boxed(x=2)`, cas
  `popup` ambigu).
- **A2 — Serializer core (`\boxed{…}` → `m:borderBox`)** : branche dans
  `serialization/.../LatexToOmml.cs` émettant
  `<m:borderBox><m:e>…</m:e></m:borderBox>` (sans `borderBoxPr` → 4 bords par
  défaut), réutilisant le parsing d'argument accolade existant. Test unitaire.
- **A3 — Walker adapter (`m:borderBox` → OMath natif)** : `case "borderBox"`
  dans `OmmlWalkerWhitelist` + `OmmlToOMathBuilder`
  (`om.Functions.Add(at, WdOMathFunctionType.wdOMathFunctionBorderBox)`, arg `.E`
  rempli récursivement, schéma identique à `acc`/`rad`). Fixture
  `WalkerCoverageTests`.
- **A4 — Bouton « Encadrer le résultat »** : bouton ruban qui prend l'OMath au
  caret, résout sa source via `SourceMapResolver.ResolveConfirmed` (K2), recompose
  `\boxed{<latex>}` (+ source `boxed(<sténo>)`) et ré-insère sur `om.Range` via
  `OMathInserter.Insert` sous `UndoRecordScope` puis `FlushPendingRecord` hors
  scope → réutilise ZoneCleaner, walker, source-map, undo (1 Ctrl+Z annule).
  Garde-fou : OMath non « à nous » → no-op + `IUserFeedback`.

A1+A2+A3 sont livrés **ensemble** : le verrou `WalkerCoverageTests` exige que
tout candidat du corpus moteur soit constructible — dès que `boxed` entre dans le
corpus, le walker doit savoir le bâtir.

### Volet B — Callouts typés (100 % périphérique, adapter-vsto)

Pur style de paragraphe Word, aucune OMath, aucun moteur, aucun source-map.

- **B1 — `Host/CalloutInserter.cs` (nouveau)** : table de 4 types
  (`CalloutStyle { Label FR/EN, accent WdColor, fill RGB }`) — Théorème (rouge
  bordeaux / rose pâle), Définition (bleu), Exemple (vert), Propriété (rose
  framboise) — couleurs calquées sur le doc. `Insert(app, style)` cible la
  sélection (sinon le ¶ courant), pose une barre d'accent **gauche** + fond
  teinté (`para.Borders` / `para.Shading.BackgroundPatternColor`) + padding
  (`ParagraphFormat.SpaceBefore/After`) + un **run titre** en petites capitales.
  S'appuie sur les patterns bordures déjà présents dans `ColumnLayoutInserter`.
- **B2 — Ruban + i18n** : `<menu id="CalloutMenu">` (calqué sur `ColumnsMenu`),
  1 bouton/type, dispatché dans `OnInsertCalloutClicked(control)` sur `control.Id`.
  Libellés FR/EN dans `Strings.cs`.

### Gate POC #0 (règle dure word-api, avant prod)

- **A** : bouton POC minimal insérant `\boxed{x}` via le walker — confirme que
  `wdOMathFunctionBorderBox` existe dans l'interop Word 2019 et se construit
  proprement.
- **B** : bouton POC minimal posant bordure gauche + shading sur le ¶ courant —
  valide le rendu **avant** les variantes typées et le run titre.

Ordre de livraison : **Volet A d'abord** (chemin core net, testable en fixtures),
puis **Volet B**.

## Tradeoff & alternatives écartées

- **Boxed via manipulation Word directe (sans core)** : wrapper l'OMath au caret
  en `borderBox` côté Word sans toucher au moteur. Écarté : l'utilisateur veut le
  boxed **first-class** (tapable en sténo `boxed(...)`) ; la voie data+serializer
  est plus juste **et** réutilise tout le pipeline existant.
- **Callouts via un style Word nommé (`Styles.Add`)** : plus « propre »
  documentairement mais lourd (cycle de vie du style, conflits de template). Le
  style direct sur paragraphe est suffisant et local.
- **Callouts en tableau 1×1** (comme le layout colonnes existant) : cadre net mais
  rigidifie le flux d'édition. Bordures de paragraphe = plus fluide pour taper
  dedans.
- **Callouts en boîte neutre / sans titre** : écarté au profit du jeu typé fidèle
  au doc (choix utilisateur explicite).

## Conséquences

- **Code touché** :
  - Data : `data/engine/symbols.json`, `data/engine/cultures.json`.
  - Core : `serialization/src/MathCursor.Serialization/LatexToOmml.cs`.
  - Adapter walker : `Host/SourceMap/OmmlWalkerWhitelist.cs`,
    `Host/OmmlToOMathBuilder.cs`.
  - Adapter ruban/orchestration : `Ribbon.xml`, `RibbonCallback.cs`, `Strings.cs`,
    `Host/ConversionController.cs` (box-at-caret).
  - Adapter callouts : `Host/CalloutInserter.cs` (nouveau — déclaration manuelle
    dans le csproj VSTO old-style, pas de réécriture PowerShell).
- **Tests** : fixtures moteur (`boxed x` / `boxed(x=2)`), test unitaire
  `LatexToOmml` (`m:borderBox`), fixture `WalkerCoverageTests` (borderBox
  constructible), adapter xUnit.
- **API publique** : inchangée (les méthodes publiques `Analyze` /
  `OMathInserter.Insert` gardent leur signature).
- **Règles MC impactées** : aucune. Le port JS de la démo web n'est pas concerné.

## Validation post-fix

- Moteur : `dotnet test` engine → fixtures boxed vertes.
- Serializer : test `LatexToOmml` `\boxed{x}` → `m:borderBox` vert.
- Walker : `WalkerCoverageTests` vert ; adapter xUnit vert.
- POC #0 : dans Word, insertion `\boxed{x}` encadrée + callout posé sur le ¶
  courant.
- Manuel produit :
  1. `boxed(x=2)` + `Ctrl+Espace` → résultat encadré, `Ctrl+Z` unique annule ;
  2. caret dans une équation → bouton « Encadrer le résultat » → encadrée ;
  3. sélection d'un ¶ → menu « Encadré › Théorème » → barre rouge + fond rosé +
     titre « THÉORÈME » ; idem Définition/Exemple/Propriété ;
  4. FR et EN (libellés + alias sténo).

## Plan en cours — état d'avancement

Volet A (boxed) :
- [x] A1 — data `symbols.json` + alias `cultures.json` (`encadre`/`cadre`/`encadrer`) + fixtures (moteur 21/21 vert) — ⚠️ **RÉTRACTÉ le 2026-06-22** (ADR `2026-06-22-Refactor-boxed-post-hoc-only-out-of-engine`) : `boxed` sort du moteur, plus de saisie inline. Le reste du volet A (A2/A3/A4) est conservé.
- [x] A2 — serializer `LatexToOmml` `\boxed` → `m:borderBox` + 2 tests (serialization 62/62 vert)
- [x] A3 — walker `borderBox` (whitelist + builder, `wdOMathFunctionBorderBox` confirmé à la compile VSTO) + WalkerCoverage. **Aperçu popup** : `MixedLatexRenderer` dessine un `Border` WPF autour du contenu pour `\boxed{…}` pleine formule (rendu PNG vérifié) ; déballage `WpfMathAdapter` (`\boxed{X}`→`X`) gardé en filet pour les `\boxed` nichés. POC #0 A validé en prod (log : `boxed(x+1)`→`\boxed{x+1}` commité). Adapter 339/339 vert.
- [x] A4 — bouton « Encadrer le résultat » : `ConversionController.BoxAtCaret` (resolve K2 → re-wrap `\boxed{…}` → `Insert` sous undo) + ruban `BoxResultButton` + callback + i18n. Garde-fous : hors map / déjà encadrée / bloc multiligne. VSTO compile. **Validation runtime Word à faire.**

Volet B (callouts) :
- [x] B1 — `Host/CalloutInserter.cs` : 4 types (Théorème/Définition/Exemple/Propriété), barre d'accent gauche + cadre fin + fond teinté + titre petites caps ; cible sélection ou ¶ courant ; styling 100 % try/catch. Déclaré au csproj.
- [x] B2 — menu ruban `CalloutMenu` (4 boutons, dispatch sur id) + callbacks + i18n (libellés types réutilisent `CalloutInserter.Styles[].Label`). VSTO compile.
- [ ] POC #0 B + validation runtime Word (rendu bordure/fond/titre, sélection mono/multi-¶).
