# adapter-vsto

Add-in VSTO pour Word Desktop Windows — **Phase 1 du projet MathCursor**.

## Prérequis

- **Visual Studio 2022 Professional ou Enterprise** (Community ne supporte pas VSTO)
- Charge de travail **"Développement Office/SharePoint"** installée dans VS Installer
- **.NET Framework 4.8 Developer Pack**
- Microsoft Office Word 2016+ (Desktop) pour tester

## Build

Le projet VSTO est dans le repo : `adapter-vsto/src/MathCursor/MathCursor.csproj`
(nom du projet : **`MathCursor`**). Il référence le moteur pur :
- `engine/src/MathCursor.Engine/MathCursor.Engine.csproj`
- `serialization/src/MathCursor.Serialization/MathCursor.Serialization.csproj`
- `host-contract/src/MathCursor.HostContract/MathCursor.HostContract.csproj` (DTO `EquationHandle`)

Ouvre `MathCursor.sln` dans VS2022 → régénérer → F5 (détails : [`INSTALL.md`](INSTALL.md)).

## Structure (principaux fichiers)

```
adapter-vsto/src/MathCursor/
├── Host/
│   ├── ConversionController.cs        # orchestration : zone → moteur → popup → commit OMath
│   ├── OMathInserter.cs              # insertion OMML dans Word + SourceMap
│   ├── SpanComputer.cs              # zone Ctrl+Espace (délimiteurs)
│   ├── EditMode/EditModeController.cs # « revenir à la saisie » (mode édition)
│   ├── SourceMap/                    # persistance source ↔ OMath (CustomXMLParts)
│   └── Detection/MathNerDetector.cs  # NER ONNX (auto-détection) + fenêtrage
├── UI/
│   ├── SuggestionPopupWindow.cs      # popup WPF au caret (rendu WpfMath)
│   └── EditModePopupWindow.cs
├── ThisAddIn.cs                      # point d'entrée VSTO
├── RibbonCallback.cs                 # ruban (convertir, encadrer, colonnes, police…)
└── MathCursor.csproj
```

## Flux runtime

1. Déclenchement **explicite** : **Ctrl+Espace** ou bouton ruban — *pas de polling*.
2. `ConversionController` calcule la zone (`SpanComputer`) puis appelle le **moteur pur**
   `ForestEngine.Analyze` (texte → candidats LaTeX classés).
3. Popup WPF au caret si plusieurs candidats : Entrée commit, ↓/↑ navigue, Échap masque.
   Popup masquée si le caret est sur un OMath existant.
4. Commit : `LatexToOmml` → OMML inséré via `Range.InsertXML` ; la source est
   enregistrée (`SourceMap`) → « revenir à la saisie » possible. (Une auto-détection
   NER peut armer la popup en plus du déclenchement manuel.)

## Packaging / déploiement

Installeur **Inno Setup** (`adapter-vsto/installer/`, build via `build.ps1`), distribué
sur R2 + page de téléchargement Cloudflare Pages. Skills `/build-iss` et `/deploy-prod`.

## Tests

Tests de l'adapter dans `adapter-vsto/tests/MathCursor.Tests/` (logique pure extraite :
SpanComputer, zones, fenêtre NER, walker, canonicalizer…). Moteur : `engine/tests/`.
Gate de test complet : `scripts/run-tests.ps1`.
