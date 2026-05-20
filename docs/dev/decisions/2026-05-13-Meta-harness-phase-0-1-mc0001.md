# Meta — Harnais Phase 0+1 : projet analyzer + règle MC0001

**Date :** 2026-05-13
**Kind :** Meta
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [docs/dev/architecture/cartography.md](../architecture/cartography.md)
+ brief externe `MATHCURSOR_HARNESS_BRIEF.md` (téléchargé 2026-05-13).

## Décision

Setup du **harnais d'analyse statique** par un projet Roslyn dédié, posé en
parallèle du refacto archi-axes (étape 2 = `MathCursor.Core.Abstractions`).

### Structure créée

```
analyzers/
├── MathCursor.Analyzers/                # netstandard2.0 — règles MC
│   ├── MathCursor.Analyzers.csproj
│   ├── MC0001_RegexOnStructured.cs
│   ├── AnalyzerReleases.Shipped.md      # RS2008 tracking
│   └── AnalyzerReleases.Unshipped.md    # RS2008 tracking
└── MathCursor.Analyzers.Tests/          # net8.0 — xUnit + Roslyn testing
    ├── MathCursor.Analyzers.Tests.csproj
    └── MC0001_RegexOnStructuredTests.cs

.editorconfig                            # sévérités MC racine
```

### Règle MC0001 — Regex sur contenu structuré

**Détecte** : `new Regex(...)` et `Regex.Match/Replace/Split/IsMatch/...`
dans 3 contextes structurés :

1. Le fichier contient `using System.Xml.Linq` ou `using System.Xml`.
2. Le nom de fichier matche `*OMath*`, `*Serializer*`, `*Parser*`,
   `*Renderer*`, `*Splicer*`.
3. Un littéral d'argument du `new Regex(...)` contient `<` ou `xmlns`.

**Sévérité par défaut** : `Info` dans l'analyzer ; promue à `warning` via
`.editorconfig` racine. Pas d'`error` à cette phase — on observe les hits
existants avant promotion.

**Anti-promotion-prématurée** : `MathCursor.Core.csproj` ajoute
`<WarningsNotAsErrors>MC0001</WarningsNotAsErrors>` pour que le build ne
casse PAS sur les hits existants (`LatexToUnicodeMath.cs` utilise Regex
pour les environnements `\begin{cases}` / `\begin{pmatrix}` — 6 hits
remontés). Promotion en `error` après nettoyage et/ou ADR de suppression
ciblée sur les sites légitimes.

### Branchement

Pour Phase 0+1 : branchement **projet-par-projet** via `ProjectReference`
avec `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`. Pour
l'instant **`MathCursor.Core` uniquement** — c'est là que vit la dette
Regex/OMath identifiée par la cartographie.

Phase 1 (next) ou 2 : `Directory.Build.props` racine qui généralise à
tous les `.csproj` de la sln. Reporté tant qu'on valide le comportement
sur 1 projet d'abord.

## Pourquoi

- **Brief harnais** propose 3 couches : Roslyn analyzers (Tier 1 + 2),
  diff summarizer Python (Tier 2/3 + agrégation), agents/skills.
  Phase 0+1 = le minimum utile = Tier 1 / 1 règle.
- **MC0001 = ROI immédiat le plus élevé** : la dette Regex sur OMath
  existe (cartographie 13-05, dette Niveau 3). L'analyzer la rend visible
  systématiquement, fait office de garde-fou contre l'ajout de nouvelles
  Regex sur du XML dans Core.
- **Stratégie « warning visible mais non bloquant »** : permet d'observer
  les hits dans l'IDE et MSBuild output sans forcer un nettoyage immédiat.
  La promotion progressive (Info → Warning → Error) avec ADRs de
  suppression localisée est la bonne discipline.
- **MC0002 (VSTO leak) sera la 2e règle** mais nécessite que le Core
  soit cleanly isolé (étape 5 du refacto archi, sortie des chaînes FR).
  C'est dans l'ordre du plan validé.

## Tradeoff & alternatives écartées

- **Démarrer en `error` directement.** Rejeté : casse le build sur
  `LatexToUnicodeMath.cs` (6 hits) → bloque le travail courant. Migration
  progressive plus saine.
- **`<NoWarn>MC0001</NoWarn>` partout au lieu de `WarningsNotAsErrors`.**
  Rejeté : `NoWarn` masque la diagnostique entièrement, on perd la
  visibilité IDE. `WarningsNotAsErrors` garde le squiggle jaune dans VS
  + l'output MSBuild, juste ne casse pas la CI.
- **Mettre l'analyzer dans `core-csharp/`.** Rejeté : un analyzer n'est
  pas du code Core, il s'applique à plusieurs projets. Le dossier
  `analyzers/` racine reflète son statut de couche transverse.
- **Référencer l'analyzer depuis l'adapter VSTO aussi.** Reporté à
  Phase 1 (Directory.Build.props). Tester d'abord sur Core évite un
  débordement de signal initial sur 100+ fichiers adapter.
- **Implémenter MC0002 dans le même PR.** Reporté : MC0002 dépend de
  l'isolation du Core (étape 5 du refacto axes). Sans cette isolation,
  MC0002 ne capte rien d'utile (Core n'utilise pas VSTO par convention,
  pas par mécanique).

## Conséquences

- **Build adapter VSTO inchangé** : pas de branchement de l'analyzer
  côté adapter pour l'instant.
- **6 hits MC0001 visibles** dans `LatexToUnicodeMath.cs` (lignes 48, 60,
  63, 66, 69, 79). Action future : refacto en parser dédié OU 6 ADRs de
  suppression `[SuppressMessage("MathCursor.Architecture", "MC0001",
  Justification = "ADR-XXX: ...")]` ciblées.
- **Tests** : 11/11 verts pour MC0001 (positifs sur 3 heuristiques + cas
  négatifs sur regex texte plat). 935/944 Core conservés (6 préexistants),
  419/419 adapter conservés.
- **Convention `SuppressMessage` à formaliser** : toute suppression de
  diagnostic MC doit citer un ADR existant. Phase 2+ : règle MC9999 qui
  audite les `SuppressMessage` sans `Justification = "ADR-..."` (cf. brief).

## Validé par l'utilisateur

> « ok » (validation du plan Phase 0+1 : analyzer setup + MC0001 seul,
> branché sur Core uniquement, severity warning non bloquante)

## Plan harnais — état d'avancement

- [x] **Phase 0** — Setup `analyzers/MathCursor.Analyzers` + tests + sln
- [x] **Phase 1** — MC0001 (Regex sur contenu structuré) actif sur Core
- [ ] **Phase 2** — Generalisation Directory.Build.props (adapter inclus) — 0.5j
- [ ] **Phase 3** — MC0002 (VSTO leak in Core) après étape 5 du refacto axes
- [ ] **Phase 4** — MC0003 + MC0004 + MC0005 + MC9999 (suppression sans ADR) — 1.5j
- [ ] **Phase 5** — Diff summarizer Python (SARIF + filtres + rapport) — 2j
- [ ] **Phase 6** — Sources Tier 2 (NEWTYPE, COMPLEXITY-DELTA, etc.) — 2j
- [ ] **Phase 7** — Sources Tier 3 (COVERAGE-GAP, DOC-DENSITY) — 1j
- [ ] **Phase 8** — Agents + Skills (architecte/dev/auditeur/maintainer) — 1.5j
- [ ] **Phase 9** — Boucle feedback (verdicts + recalibration) — 1.5j
