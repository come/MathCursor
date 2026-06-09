// Polyfill pour utiliser `init` properties (C# 9+) sur .NET Standard 2.0 et .NET Framework 4.8.
// Sans ce type, le compilateur refuse les setters `init`.
// Doit être PUBLIC pour que les assemblies consommateurs (Core, adapter-vsto) puissent
// invoquer les setters `init` des types exportés par HostContract.

namespace System.Runtime.CompilerServices;

public static class IsExternalInit { }
