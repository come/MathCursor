# Host Contract — Définition abstraite des interfaces

Quatre interfaces principales composent le contrat d'hôte. Chaque adapter
(`adapter-vsto`, futur `adapter-officejs`) les implémente.

## IDocumentHost

Interaction avec le document texte : lecture contexte, insertion/édition équations,
événements de position curseur.

Méthodes clés :
- `ReadContextAroundCaretAsync(int charsBefore, int charsAfter)` → texte autour du curseur
- `InsertEquationAsync(TextZone zone, EquationOutput eq)` → insère + retourne handle
- `UpdateEquationAsync(EquationHandle h, EquationOutput eq)` → met à jour
- `RevertEquationAsync(EquationHandle h)` → remplace par le source texte
- `GetCaretEquationAsync()` → handle de l'équation au curseur, ou null

Events :
- `CaretMoved` (debounced dans l'adapter)
- `EquationEntered` / `EquationExited`
- `ConversionRequested` (raccourci ou bouton)

## IEquationStore

Persistance des sources d'équations (pour édition ultérieure).

- `StoreAsync(handle, source, metadata)`
- `RetrieveAsync(handle)` → `StoredEquation?`
- `UpdateAsync(handle, newSource)`
- `RemoveAsync(handle)`
- `ListAllAsync()` (diagnostic)

VSTO : `Document.CustomXMLParts`.

## IEditorSurface

UI de présentation des suggestions et édition.

- `ShowSuggestions(RankedCandidate[])`
- `HideSuggestions()`
- `ShowEditMode(source, handle)` / `ExitEditMode()`
- `Notify(message, level)`

Events :
- `SuggestionSelected(int index)`
- `EditCommitted(string newSource)`

VSTO : popup WPF `TopMost` ancrée au caret.

## IUserFeedback

Logging local opt-in (pas de télémétrie réseau).

- `LogSuggestionShown(RankedCandidate[])`
- `LogSuggestionSelected(int index)`
- `LogSuggestionRejected(string? reason)`
- `LogParsingError(input, error)`
- `LogPlatformCapability(name, supported)`

VSTO : fichier JSON dans `%AppData%\MathCursor\logs\`.

## Types partagés

Voir `host-contract-csharp/src/MathCursor.HostContract/Types.cs` pour les
définitions normatives. Résumé :

```csharp
public class ContextText {
    public string TextBefore;
    public string TextAfter;
    public int CaretOffset;
    public string? LanguageHint;
}

public class TextZone {
    public int StartOffset;
    public int EndOffset;
}

public class EquationOutput {
    public string Source;                    // texte originel, pour revert/édition
    public string Latex;
    public string? Omml;
    public string? UnicodeFallback;
    public EquationMetadata Metadata;
}

public class EquationHandle {
    public string Id;                        // GUID opaque
}
```
