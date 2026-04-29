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

## Structure actuelle

```
adapter-vsto/src/MathCursor/
├── Host/
│   ├── SuggestionService.cs       # orchestration : tick NER + popup + commit OMath
│   ├── KeyboardInterceptor.cs     # hook Up/Down/Enter/Esc pour la popup
│   ├── WordContextReader.cs       # lecture du paragraphe courant
│   └── VstoEquationStore.cs       # IEquationStore via CustomXMLParts (phase édition)
├── Detection/
│   ├── MathNerDetector.cs         # modèle NER XLM-RoBERTa ONNX
│   └── Sp/                        # tokenizer SentencePiece C# pur
├── UI/
│   └── SuggestionPopupWindow.cs   # popup WPF TopMost au caret (WPF-Math)
├── ThisAddIn.cs                   # point d'entrée VSTO
├── RibbonCallback.cs              # bouton "À propos"
└── MathCursor.csproj
```

## Flux runtime

1. `ThisAddIn` charge le NER, l'engine YAML, le store (CustomXMLParts),
   et installe `SuggestionService` + `KeyboardInterceptor`.
2. `SuggestionService` tick toutes les 200 ms + `WindowSelectionChange` :
   lit le paragraphe, invoque le NER (thread pool), affiche la popup.
3. Popup masquée si le caret est sur/collé à un OMath existant (évite
   de relancer l'algo sur du LaTeX déjà rendu).
4. `KeyboardInterceptor` : Down → NavMode, Up/Down navigue, Enter →
   commit (insère OMath à partir du LaTeX via UnicodeMath), Esc masque.

## Packaging / déploiement (phase ultérieure)

- MSI signé via WiX Toolset — script de build séparé à venir
- Installation per-user dans `%AppData%\MathCursor\`
- Auto-update via ClickOnce ou vérification manifest.xml distant

## Tests

Les tests unitaires du core sont dans `core-csharp/tests/`. Pour l'adapter,
les tests intégration mockent les 4 interfaces et vérifient le comportement
bout en bout dans `adapter-vsto/tests/MathCursor.Vsto.Tests/`.
