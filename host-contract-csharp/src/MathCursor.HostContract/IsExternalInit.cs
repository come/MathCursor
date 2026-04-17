// Polyfill pour utiliser `init` properties (C# 9+) sur .NET Standard 2.0.
// Sans ce type, le compilateur refuse les setters `init`.
// Le type est interne, partagé via InternalsVisibleTo si nécessaire.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }
