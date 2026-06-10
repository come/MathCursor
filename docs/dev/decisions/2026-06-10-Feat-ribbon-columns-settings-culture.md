# Feat — Ribbon : retour des Colonnes + bouton Paramètres (culture FR/US)

**Date :** 2026-06-10
**Kind :** Feat
**Température :** forte (injection culture par paramètre) / molle (valeurs des presets FR/US)
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Refactor-phase2-adapter-orchestration-rewrite.md](2026-06-10-Refactor-phase2-adapter-orchestration-rewrite.md) (ribbon refait à 3 boutons), ADR DocMath `2026-05-11-Feat-ribbon-home-duo-plus-dedicated-tab` (origine du composant colonnes)

## Citation acté

> « oui ok. pour les colonnes on laisse tout dans l'onglet math cursor » — utilisateur, 2026-06-10

(validation du plan présenté : restauration colonnes + popup paramètres, presets culture
avec overrides nullables, EngineCulture paramétré, store JSON.)

## Contexte

Le ribbon Phase 2 beta-clean a été réduit à 3 boutons (Convertir / Signaler / À propos).
Deux besoins remontent :

1. **Colonnes** — le menu « Insérer des colonnes » (tableau 1×N, barres séparatrices
   seules, largeurs égales) existait dans DocMath (`ColumnLayoutInserter.cs`) et sert
   réellement à la prise de cours. À restaurer.
2. **Paramètres** — le moteur forest hardcode la culture FR en 4 points :
   `Lexer.cs:151-154` (décimale), `Parser.cs:137` (séparateur d'intervalle),
   `LatexRenderer.cs:31,37` (env matrice + intervalle), via le statique
   `Vocabulary.Locale` + `Vocabulary.Matrix` (`(` → `pmatrix` uniquement). Aucun réglage
   utilisateur n'existe. On veut une popup Paramètres : culture (FR/US), séparateur,
   visualisation de matrice `(` ou `[` — extensible au fur et à mesure.

## Décision

### A. Colonnes — onglet MathCursor uniquement

- Port de `ColumnLayoutInserter` depuis DocMath **sans la logique bookmarks `mcEq_`**
  (`CollectOMathHandlesInRange` / `ReattachOMathBookmarks`) : obsolète, le pattern
  anchor CC (Tag JSON `MCMeta`) voyage avec la copie `FormattedText`.
- Menu ribbon 1→4 colonnes dans un groupe « Mise en page » de l'onglet MathCursor.
  **Pas de retour dans TabHome** (décision explicite utilisateur, cohérent avec la
  réécriture Phase 2 qui a supprimé le groupe Accueil).
- Icônes `imageMso` natives (le beta-clean a abandonné les PNG embarqués).
- Règle dure Word API respectée : POC préalable vérifiant que `FormattedText` copy
  préserve le CC anchor + son Tag avant d'acter le port.

### B. Paramètres — culture preset + overrides nullables

- **Modèle** : on persiste `Culture` (FR | US) + des overrides **nullables** par
  réglage (`null` = suit la culture). Presets :
  - **FR** : décimale `,` (sortie `{,}`), séparateur d'intervalle `;`, matrice `(` (pmatrix)
  - **US** : décimale `.`, séparateur d'intervalle `,`, matrice `[` (bmatrix)
- **Moteur** : nouveau type public `EngineCulture` (DecimalsIn / DecimalTex /
  IntervalSep / MatrixEnv) + presets `Fr` / `Us`. Threadé **en paramètre** :
  `ForestEngine.Analyze(src, EngineCulture? = null)` → Lexer / Parser / LatexRenderer.
  Défaut = FR (les 280 fixtures restent inchangées).
- **Persistance** : JSON `%APPDATA%\MathCursor\settings.json`, champ `V` (= 1),
  fallback silencieux sur les défauts + log si fichier corrompu. Cohérent avec les
  logs déjà en `%APPDATA%\MathCursor\`.
- **UI** : `SettingsWindow` WPF (grande popup) ouverte par un bouton Paramètres du
  ribbon. Changement de culture → re-preset des champs ; champ modifié à la main →
  override. Apply immédiat sans redémarrage (l'`EngineCulture` est reconstruite à
  chaque Trigger).
- La sérialisation OMML gère déjà `bmatrix` (`LatexToOmml.cs:339`) — couverture
  verrouillée par test, rien à écrire.

## Tradeoff & alternatives écartées

- **Muter `Vocabulary.Locale` statique au changement de réglage** : état global →
  fixtures xUnit parallèles non déterministes, risque d'état périmé en cours de
  conversion. Rejeté (d'où la température forte sur l'injection par paramètre).
- **`Properties.Settings` (.NET user.config)** : chemins versionnés par assembly →
  réglages perdus aux upgrades VSTO, fichier opaque pour le diagnostic. Rejeté au
  profit du JSON versionné par champ `V`.
- **Persister les valeurs effectives (pas d'overrides nullables)** : figerait des
  défauts périmés — un utilisateur « FR pur » n'hériterait pas des bons défauts des
  futures options liées à la culture. Rejeté.
- **Colonnes aussi dans TabHome (comme DocMath)** : rejeté par l'utilisateur, le
  beta-clean assume l'onglet unique.

## Conséquences

- **Code touché** :
  - L1 : `engine/src/MathCursor.Engine/` (nouveau `EngineCulture.cs`, signature
    `ForestEngine.Analyze`, lectures culture dans Lexer/Parser/LatexRenderer)
  - L2 : `adapter-vsto/src/MathCursor/Host/ColumnLayoutInserter.cs` (port amputé),
    `Host/Settings/` (AppSettings + SettingsStore), `ConversionController` (construit
    l'EngineCulture au Trigger)
  - L3 : `UI/SettingsWindow` (nouveau), `Ribbon.xml`, `RibbonCallback.cs`, `Strings.cs`
- **Tests** : 280 fixtures engine inchangées (défaut FR) ; nouveau `CultureTests.cs`
  US ; InlineData `bmatrix` dans `OmmlCoverageTests` ; round-trip + JSON corrompu pour
  le SettingsStore. Colonnes : validation manuelle (harnais d'intégration Word absent
  du beta-clean).
- **API publique** : `ForestEngine.Analyze` gagne un paramètre optionnel —
  rétro-compatible.
- **Règles MC impactées** : aucune.

## Validation post-fix

1. `dotnet test` engine + serialization + adapter : tout vert, 280 fixtures intactes.
2. Word : popup Paramètres → culture US → convertir `1.5` (décimale point), un
   intervalle (séparateur `,`), une matrice (rendu crochets) ; retour FR → comportement
   actuel à l'identique.
3. Word : menu Colonnes 2 → tableau 1×2 séparateur vertical seul ; sélection contenant
   une équation MathCursor → équation déplacée en col 1, CC anchor + Tag préservés
   (edit mode toujours fonctionnel).
