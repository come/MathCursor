# Feat — Sélecteur de police math (Latin Modern / STIX / Cambria)

**Date :** 2026-06-22
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-20-Feat-encadres-callouts-boxed](2026-06-20-Feat-encadres-callouts-boxed.md) (même veine « rédiger visuellement chouette »), [2026-06-10-Feat-ribbon-columns-settings-culture](2026-06-10-Feat-ribbon-columns-settings-culture.md) (modèle `AppSettings` + persistance `SettingsStore`)

## Citation acté

> « rajouter la police math du document que je t'ai envoyé » — utilisateur, 2026-06-22

> Polices à proposer = « Latin et STIX stp » ; portée = « Les deux »
> (police math par défaut du doc **+** préréglage des insertions MathCursor) —
> réponses utilisateur aux questions de cadrage, 2026-06-22.

> « oui go » — utilisateur, 2026-06-22 (validation du plan).

## Contexte

L'utilisateur rédige des notes de maths sur le modèle d'un poly de cours et veut
retrouver **la police math du document** (rendu type LaTeX). Besoin : un sélecteur
de police math dans le ruban MathCursor, exposant au minimum **Latin Modern Math**
(look Computer Modern / LaTeX) et **STIX Two Math**, en plus de **Cambria Math**
(défaut Word).

Contrainte de rendu : Word ne typographie correctement les maths qu'avec une fonte
possédant une **table OpenType « MATH »**. Proposer une police de texte ordinaire
(Arial, Calibri…) comme « police math » tromperait — le rendu serait cassé. D'où
une **liste curatée** de fontes math, pas un sélecteur libre.

## Décision

Un **menu déroulant ruban** « Police math » listant une sélection curatée de
fontes math (`Cambria Math` défaut, `Latin Modern Math`, `STIX Two Math`). Au
choix d'une police, portée **double** (demande utilisateur « les deux ») :

1. **Doc entier (existant)** — itération de `doc.OMaths`, pose
   `om.Range.Font.Name = police` sur **toutes les équations actuelles**.
2. **Préréglage (futur)** — la police est mémorisée dans `AppSettings.MathFont`
   (nouveau champ nullable, `null` = défaut Word/Cambria) et `OMathInserter`
   l'applique à **chaque nouvelle équation MathCursor** (étape post-build, avant
   l'échappement caret).

### Détails

- **`Host/MathFontApplier.cs` (nouveau)** : source de vérité de la liste curatée
  (`Cambria Math`, `Latin Modern Math`, `STIX Two Math`) + `IsInstalled(app, font)`
  (via `Application.FontNames`) + `ApplyToDocument(doc, font, log)` (itère
  `doc.OMaths`, try/catch par équation — cosmétique, n'échoue jamais) +
  `ApplyToRange(range, font)` réutilisé par l'inserter.
- **`Host/Settings/AppSettings.cs`** : `public string MathFont { get; set; }`
  (`null` = défaut). Champ **additif nullable** → rétro-compatible, `V` inchangé.
- **`Host/OMathInserter.cs`** : après le typage Display/Inline et avant
  l'échappement caret, si `SettingsStore.Current.MathFont` est non vide,
  `MathFontApplier.ApplyToRange(om.Range, font)` (try/catch — la fonte est
  cosmétique, jamais bloquante pour le commit).
- **`Ribbon.xml`** : `<dropDown id="MathFontDropDown">` dans
  `MathCursorToolsGroup` (callbacks `getItemCount/getItemID/getItemLabel/
  getSelectedItemIndex/onAction` + `getLabel/getScreentip`). Les fontes non
  installées sont annotées « (à installer) » dans le libellé.
- **`RibbonCallback.cs`** : callbacks dropDown → `MathFontApplier.ApplyToDocument`
  + `SettingsStore.Save` (clone/mutate/save, comme les toggles existants) +
  invalidation ruban.
- **`Strings.cs`** : libellé/screentip menu FR/EN (les noms de fontes sont des
  noms propres, non traduits ; suffixe « (défaut) »/« (default) » et
  « (à installer) »/« (not installed) » localisés).

### Addendum 2026-06-22 — polices embarquées dans l'installeur + repli lien

> « on peux pas embed les polices pour installer ? ou au moins ouvrir sur le
> lien qui va bien ? » — utilisateur, 2026-06-22.

Deux niveaux, livrés ensemble :

1. **Repli « ouvrir le lien »** (runtime, tout poste) : si la police choisie
   n'est pas installée, le menu propose (Oui/Non) d'ouvrir sa **page officielle
   gratuite** (`MathFontCatalog.DownloadUrl` : GUST e-foundry pour Latin Modern,
   stixfonts.org pour STIX). Sert aux installs sideloadées et aux Windows < 1809.
2. **Bundling installeur** (zéro friction, le cas nominal) : les deux `.otf` sont
   **embarqués** dans `adapter-vsto/installer/fonts/` et installés **par
   utilisateur** par Inno Setup (`DestDir: {autofonts}` + `FontInstall`, flags
   `onlyifdoesntexist uninsneveruninstall`) — **sans UAC**, cohérent avec
   `PrivilegesRequired=lowest`. Les licences (GUST Font License, SIL OFL) sont
   déposées dans `{app}\fonts-licenses`.

**Licences** : Latin Modern Math (GUST Font License, type LPPL) et STIX Two Math
(SIL OFL 1.1) sont **librement redistribuables**. OFL impose de fournir la licence
et interdit de réutiliser le « Reserved Font Name » pour un dérivé — on ne modifie
ni ne renomme les fontes, on les embarque telles quelles : conforme.

**Caveat** : l'installation de police **par utilisateur** requiert **Windows 10
1809+**. Sur un système antérieur, `{autofonts}` retombe sur le périmètre commun
(UAC) ou échoue ; le repli « ouvrir le lien » couvre alors le cas. La conséquence
« à installer côté utilisateur » de la version initiale n'est donc plus vraie pour
le chemin installeur nominal.

**Conformité licences** (question utilisateur 2026-06-22 : « on a le droit
légalement ? faut pas rajouter des trucs dans le à propos ? et la licence
globale ? ») :
- **Droit de bundler** : oui. OFL 1.1 clause 2 (STIX) et GUST/LPPL (Latin Modern)
  autorisent explicitement la redistribution embarquée avec un logiciel, à
  condition de **joindre la licence + le copyright** — fait via
  `{app}\fonts-licenses`. Fontes **non modifiées** (clauses Reserved Font Name /
  renommage sans objet), **non vendues isolément**.
- **Licence globale** : **inchangée**. MathCursor est **GPL v3** ; les polices
  sont un **agrégat** (GPL §5 — fichiers séparés non liés au binaire), donc pas
  de contamination dans un sens ni dans l'autre. OFL et LPPL sont compatibles GPL
  pour l'agrégation.
- **Actions de conformité livrées** : section « Licences » ajoutée au « À propos »
  (`Strings.HelpDialogBody`, FR/EN : GPL v3 + les 2 polices) + fichier racine
  `THIRD-PARTY-NOTICES.md` (polices + autres composants ; le modèle NER reste à
  documenter, hors périmètre).

### Gate POC #0 (règle dure word-api, avant câblage complet)

Confirmer **dans Word** que `om.Range.Font.Name = "STIX Two Math"` **re-typographie**
réellement l'équation (comportement standard d'une fonte à table MATH sélectionnée
sur une zone math, mais à vérifier). Si KO → repli à étudier (manipulation
`settings.xml`, hors interop). Confirmer aussi que `Application.FontNames` liste les
fontes installées.

## Tradeoff & alternatives écartées

- **Sélecteur libre de toutes les fontes installées** : écarté — les fontes sans
  table MATH cassent le rendu math, l'utilisateur croirait pouvoir mettre n'importe
  quoi. Liste curatée = honnête.
- **Défaut math global du document (`settings.xml` `m:mathPr/m:mathFont`)** : ce
  serait le vrai « défaut » (les équations tapées **à la main** plus tard suivraient
  aussi). Écarté : **aucune API interop live** ne l'expose, et le package OOXML est
  verrouillé tant que le doc est ouvert (édition hors-bande impossible à chaud).
  Compromis retenu : appliquer par-équation (toutes les actuelles + toutes les
  futures MathCursor). Conséquence assumée : une équation tapée à la main **après**
  un changement de police garde Cambria jusqu'à un re-clic sur le menu.

## Conséquences

- **Code touché** : `Host/MathFontApplier.cs` (nouveau, déclaré à la main dans le
  csproj VSTO old-style — pas de réécriture PowerShell, mémoire
  `adapter-csproj-file-registration`), `Host/Settings/AppSettings.cs`,
  `Host/OMathInserter.cs`, `Ribbon.xml`, `RibbonCallback.cs`, `Strings.cs`.
- **Tests** : liste curatée (`MathFontApplier` pure), round-trip `AppSettings.MathFont`
  (sérialisation `SettingsStore`).
- **API publique** : inchangée. Champ settings additif, rétro-compatible.
- **Dépendances** : aucune nouvelle (pas d'OpenXML SDK). Latin Modern Math / STIX
  Two Math sont libres mais **à installer côté utilisateur** ; le menu signale leur
  absence.
- **Règles MC** : aucune. Le port JS de la démo web n'est pas concerné.

## Validation post-fix

- **POC #0** : dans Word, `om.Range.Font.Name = "STIX Two Math"` re-typographie ;
  `Application.FontNames` non vide.
- **Unit** : liste curatée stable ; `AppSettings.MathFont` survit save/reload.
- **Manuel produit** :
  1. menu « Police math › Latin Modern Math » → toutes les équations du doc passent
     en Latin Modern ; une nouvelle conversion MathCursor naît en Latin Modern ;
  2. « › STIX Two Math » → idem STIX ;
  3. « › Cambria Math (défaut) » → retour au défaut ;
  4. police non installée → libellé « (à installer) », application sans crash
     (repli Word silencieux) ;
  5. FR et EN (libellés menu).

## Plan en cours — état d'avancement

- [x] `MathFontCatalog.cs` (liste curatée pure) + `MathFontApplier.cs` (IsInstalled + ApplyToDocument/Range) + déclaration csproj VSTO + lien `MathFontCatalog` au projet de tests
- [x] `AppSettings.MathFont` (nullable) + sérialisation `SettingsStore` (`math_font`, additive, rétro-compat)
- [x] `OMathInserter` applique le préréglage post-build (étape 5b, cosmétique try/catch)
- [x] Ruban `MathFontDropDown` (dropDown) + 7 callbacks + i18n FR/EN (`Strings`)
- [x] Tests : catalogue curaté + round-trip `math_font` (16 cas) ; collection xUnit `SettingsStore` partagée (statique `FilePath`) ; **adapter 355/355 vert, VSTO compile propre**
- [x] Repli « ouvrir le lien » : `MathFontCatalog.DownloadUrl` (GUST / stixfonts.org) + message Oui/Non → `Process.Start` ; test unitaire ajouté (adapter 17/17 sur ce fichier)
- [x] Bundling installeur : `.otf` Latin Modern + STIX Two embarqués dans `installer/fonts/` (+ licences) ; `[Files]` Inno `{autofonts}` + `FontInstall` per-user, no-UAC
- [ ] POC #0 — `om.Range.Font.Name` re-typographie réellement (à valider dans Word)
- [ ] Validation runtime Word (doc entier + préréglage nouvelle insertion + fonte non installée → lien + FR/EN)
- [ ] Validation installeur : recompiler le `.iss`, installer, vérifier que Latin Modern + STIX apparaissent dans Word **sans UAC** (Win10 1809+)
