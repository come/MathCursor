// MathCursor: capturing mathematical intent from linear keyboard input.
// Copyright (C) 2026  Côme de Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
