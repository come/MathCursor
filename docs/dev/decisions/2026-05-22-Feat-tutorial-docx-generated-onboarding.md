# Feat — Tutoriel Word `.docx` généré, ouvert en fin d'install

**Date :** 2026-05-22
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Fournir avec MathCursor un tutoriel Word `.docx` qui présente les principales
capacités de saisie via un format à 2 colonnes (consigne à gauche, espace
d'essai à droite) groupées par sections thématiques. Le document est
**généré** à partir d'une spec JSON par un projet `tools/TutorialBuilder/`
utilisant `DocumentFormat.OpenXml`, et un test xUnit garantit que chaque
consigne reste cohérente avec le comportement actuel de
`LatticeEngine.ConvertWithAmbiguity`. L'installer propose à l'utilisateur,
via une checkbox `[Tasks]`, d'ouvrir le tutoriel à la fin de l'installation.

| Élément | Cible |
|---|---|
| Source de vérité du contenu | `tools/TutorialBuilder/tutorial-spec.json` |
| Builder | `tools/TutorialBuilder/` (console .NET) |
| Test anti-stale | `tools/TutorialBuilder.Tests/SpecMatchesEngineTests.cs` |
| Asset généré | `adapter-vsto/installer/payload/MathCursor-Tutoriel.docx` |
| Destination install | `{userdocs}\MathCursor\MathCursor-Tutoriel.docx` (uninsneveruninstall) |
| Ouverture post-install | `[Tasks]/[Run]` Inno Setup, `shellexec postinstall nowait` |

Sections couvertes en V1 : Fractions, Exposants/Indices, Vecteurs, Ensembles,
Intervalles, Racines/Fonctions, Sommes/Intégrales, Désambiguïsation popup.
32 items par langue.

**I18n** : 2 specs `tutorial-spec.fr.json` + `tutorial-spec.en.json`. Le builder
produit `MathCursor-Tutoriel-fr.docx` et `MathCursor-Tutoriel-en.docx` ; l'ISS
filtre via `Languages: french` / `Languages: english` (= choix du wizard) et
copie via `DestName: MathCursor-Tutorial.docx` (nom universel sur disque pour
que l'entrée `[Run]` reste unique). Le test anti-stale itère sur les 2 specs
en alignant `LatexRenderer.GlobalOptions.MultSymbol` sur la lang (`\times ` FR,
`\cdot ` EN).

## Pourquoi

### Critère validation phase 1

Le critère de succès produit (CLAUDE.md §Validation) est *"usable au quotidien
par un élève PAP et quelques profs"*. L'onboarding natif Word, où l'utilisateur
peut essayer directement dans le doc à côté de la consigne, dérisque l'adoption
mieux qu'une page web externe : pas de context-switch, le test du produit a
lieu *dans* le produit.

### Génération vs binaire manuel — anti-stale

L'historique récent montre que les règles parser bougent vite : trois ADRs
tight en une semaine (29-04, 30-04 revert, 30-04 final), `asterisk-tightness`,
`dot-as-multiplier`, `vec-dot-product`, plus tout le chantier patterns
(P1-P11) du 21-05 qui rebat les cartes côté détection. Un docx binaire tenu à
la main mentirait dans le mois.

La génération **depuis une spec JSON** + **test xUnit anti-stale** crée un
verrou explicite : à chaque changement de règle, soit le test casse et force
à MAJ la spec, soit on regrette le changement de règle. Pas de dérive
silencieuse possible.

### Pourquoi OpenXML SDK et pas Word interop

`DocumentFormat.OpenXml` est le SDK officiel Microsoft, expose les éléments
Word en objets typés (`Body`, `Paragraph`, `Table`, `Run`...), et tourne sans
Word installé sur la machine de build. Word interop côté script PowerShell
était l'autre option mais (1) non-déterministe cross-machines, (2) requiert
Word, (3) tente la manipulation de XML brut → risque MC0001 frontal. OpenXML
SDK respecte MC0001 par construction si on **interdit strictement** la
manipulation de XML brut dans `DocxRenderer.cs`.

## Conséquences

### Code et arborescence

- **Nouveau projet `tools/TutorialBuilder/`** (console .NET 8) :
  - `TutorialSpec.cs` — POCO + désérialisation JSON
  - `DocxRenderer.cs` — render OpenXML, **règle dure : objets typés uniquement,
    zéro regex, zéro XML brut** (anti-MC0001 par construction)
  - `Program.cs` — CLI `--in tools/TutorialBuilder/tutorial-spec.json --out <path>`
- **Nouveau projet `tools/TutorialBuilder.Tests/`** (xUnit) :
  - `TutorialSpecTests.cs` — round-trip JSON
  - `DocxRendererTests.cs` — structure (sections, items par section, tableau 2 col)
  - `SpecMatchesEngineTests.cs` — pour chaque item, vérifie
    `engine.ConvertWithAmbiguity(item.input).TopLatex == item.expectedLatex` +
    correspondance `showsPopup` ↔ `result.Spot != null`
- **Nouveau fichier `tools/TutorialBuilder/tutorial-spec.json`** — spec contenu pédagogique. Placé sous le projet (pas sous `data/`) pour ne pas être embarqué dans `MathCursor.Core.dll` via le wildcard `<EmbeddedResource Include="data/*.json">`.
- **`adapter-vsto/installer/build.ps1`** — appel `dotnet run --project
  tools/TutorialBuilder/ -- --out payload/MathCursor-Tutoriel.docx` avant ISCC
- **`adapter-vsto/installer/MathCursor.iss`** :
  - `[Files]` : `Source: "payload\MathCursor-Tutoriel.docx"; DestDir:
    "{userdocs}\MathCursor"; Flags: ignoreversion uninsneveruninstall`
  - `[Tasks]` : `Name: "opentutorial"; Description: "Ouvrir le tutoriel
    maintenant"; GroupDescription: "Pour bien démarrer :"`
  - `[Run]` : `Filename: "{userdocs}\MathCursor\MathCursor-Tutoriel.docx";
    Description: "Ouvrir le tutoriel"; Flags: shellexec postinstall nowait
    skipifsilent; Tasks: opentutorial`

### Dépendances

- **Nouvelle dépendance NuGet build-time** : `DocumentFormat.OpenXml` (officiel
  Microsoft). Côté `tools/TutorialBuilder/` uniquement — **pas embarquée dans
  le payload runtime** de l'add-in VSTO. L'installer ne livre que le `.docx`
  généré, pas le SDK.

### Règles MC

- **MC0001 (regex sur XML structuré)** : risque frontal côté `DocxRenderer.cs`
  si on tente de manipuler du WordOpenXML à la main. **Conformité par
  construction** : règle dure documentée dans le projet — utilisation
  exclusive des objets typés OpenXML. Pas de `SuppressMessage` prévu. Si un
  cas exige du XML brut, ADR séparé requis.
- MC0006, MC0009 : non applicables (pas de splice LaTeX, pas de suppression
  de diagnostic).

### Tests

- ~30 items dans `tutorial-spec.json` × 1 assertion engine = ~30 tests
  anti-stale.
- Tests structure docx : ~5 (nb sections, nb items, tableau 2 col, rendering
  d'un item).
- Round-trip JSON : 1 test.

### Maintenance

- Le `.docx` généré peut être commité (repro bit-exact en review) ou
  `.gitignore` (builder seule source). **À trancher en revue de la PR
  d'implémentation** — pas figé par cet ADR.
- Ajout d'une section ou d'un item = édition `tutorial-spec.json` + rebuild.
- Si un test anti-stale casse au CI suite à un changement de règle parser :
  forcer le contributeur à MAJ la spec ou à reverter — pas de skip toléré.

## Validé par l'utilisateur

Proposition initiale du concept :

> "je me dit qu'il peut etre interessant de creer un docx tuto, avec
> differentes section pour permettre à l'utilisateur de voir ce qui est
> possible (ptetre à gauche la consigne) et à droite un espace blanc pour
> qu'il essaie .. quelque chose du genre.. tu en penses quoi ? a la fin
> d'install on proposerait d'ouvrir ce document"

Décision chantier dédié (après proposition de cadrage formel) :

> "oui chantier à part mais go"

Validation du plan (Option B retenue : génération OpenXML depuis JSON + test
anti-stale + checkbox ISS `[Tasks]/[Run]`) :

> "go"

## Statut

acté
