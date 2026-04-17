// Polyfill pour utiliser `init` properties (C# 9+) sur .NET Standard 2.0.
// Voir même fichier dans host-contract-csharp pour le détail.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }
