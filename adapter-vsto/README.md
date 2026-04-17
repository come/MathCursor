# adapter-vsto

Add-in VSTO pour Word Desktop Windows — **Phase 1 du projet MathCursor**.

## Prérequis

- **Visual Studio 2022 Professional ou Enterprise** (Community ne supporte pas VSTO)
- Charge de travail **"Développement Office/SharePoint"** installée dans VS Installer
- **.NET Framework 4.8 Developer Pack**
- Microsoft Office Word 2016+ (Desktop) pour tester

## Création du projet VSTO

Le fichier `.csproj` VSTO doit être généré par Visual Studio (template spécifique).
Depuis ce dossier :

1. Visual Studio → `File > New > Project`
2. Template : **"Word VSTO Add-in"** (.NET Framework 4.8)
3. Emplacement : `D:\Software\DocMath\adapter-vsto\src\`
4. Nom du projet : `MathCursor.Vsto`
5. Une fois créé, ajouter une référence au projet :
   - `host-contract-csharp/src/MathCursor.HostContract/MathCursor.HostContract.csproj`
   - `core-csharp/src/MathCursor.Core/MathCursor.Core.csproj`

## Structure cible

```
adapter-vsto/src/MathCursor.Vsto/
├── Host/
│   ├── VstoDocumentHost.cs       # implémente IDocumentHost
│   ├── VstoEquationStore.cs      # implémente IEquationStore (CustomXMLParts)
│   ├── VstoEditorSurface.cs      # implémente IEditorSurface (popup WPF)
│   └── VstoUserFeedback.cs       # implémente IUserFeedback (fichier JSON local)
├── UI/
│   ├── CaretPopup.xaml            # popup WPF TopMost au caret
│   └── EditEquationPopup.xaml
├── Ribbon/
│   └── MathCursorRibbon.xml
├── ThisAddIn.cs                  # point d'entrée VSTO
└── MathCursor.Vsto.csproj
```

## Stratégie d'implémentation (phase C de la roadmap)

1. `ThisAddIn` crée les implémentations VSTO des 4 interfaces host-contract.
2. Les injecte dans un `MathCursorOrchestrator` (déjà dans `core-csharp`).
3. Câble les events Word : `ContentControlOnEnter`, `WindowSelectionChange`,
   raccourci global `Ctrl+Espace` (via hook Windows ou ribbon command).
4. `VstoEditorSurface` affiche une popup WPF positionnée au caret via
   `Application.ActiveWindow.RangeFromPoint` / `Application.Caret.ScreenPosition`.

## Packaging / déploiement (phase ultérieure)

- MSI signé via WiX Toolset — script de build séparé à venir
- Installation per-user dans `%AppData%\MathCursor\`
- Auto-update via ClickOnce ou vérification manifest.xml distant

## Tests

Les tests unitaires du core sont dans `core-csharp/tests/`. Pour l'adapter,
les tests intégration mockent les 4 interfaces et vérifient le comportement
bout en bout dans `adapter-vsto/tests/MathCursor.Vsto.Tests/`.
