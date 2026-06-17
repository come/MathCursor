using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MathCursor.Engine;

// Chargement de la data universelle du moteur (data/engine/*.json), embarquée en
// EmbeddedResource avec LogicalName "MathCursor.Engine.data.<fichier>" (cf. csproj).
// Source unique partagée avec le port Python. Cf. ADR portable-engine-universal-vocab.
internal static class EngineData
{
    private static readonly Assembly Asm = typeof(EngineData).Assembly;
    private static readonly object Lock = new();
    private static readonly Dictionary<string, object?> Cache = new();

    /// <summary>Lit et parse un fichier data embarqué (ex. "units.json"). Mémoïsé
    /// (chaque fichier parsé une seule fois ; plusieurs consommateurs au démarrage).</summary>
    public static object? Load(string fileName)
    {
        lock (Lock)
        {
            if (Cache.TryGetValue(fileName, out var cached)) return cached;
            string res = "MathCursor.Engine.data." + fileName;
            using var stream = Asm.GetManifestResourceStream(res)
                ?? throw new InvalidOperationException($"ressource embarquée introuvable : {res}");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var parsed = Json.Parse(reader.ReadToEnd());
            Cache[fileName] = parsed;
            return parsed;
        }
    }

    // ── casts du modèle objet JSON ──────────────────────────────────────────
    public static Dictionary<string, object?> Obj(object? o) => (Dictionary<string, object?>)o!;
    public static List<object?> Arr(object? o) => (List<object?>)o!;
    public static string Str(object? o) => (string)o!;
    public static double Num(object? o) => (double)o!;
    public static bool Bool(object? o) => o is bool b && b;

    /// <summary>Objet JSON → Dictionary&lt;string,string&gt; (toutes valeurs = chaînes).</summary>
    public static Dictionary<string, string> StrMap(object? o)
    {
        var d = new Dictionary<string, string>();
        foreach (var kv in Obj(o)) d[kv.Key] = Str(kv.Value);
        return d;
    }
}
