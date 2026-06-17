using System.Collections.Generic;

namespace MathCursor.Engine;

// units.js — TABLE des unités (DONNÉES). Externalisée dans data/engine/units.json
// (source unique C#/Python, cf. ADR portable-engine-universal-vocab). Le moteur ne
// connaît que le flag unitWord + le connecteur ·unit. Une unité = mot reconnu
// APRÈS un nombre (1 cm, 2kg).
internal static class Units
{
    private static readonly Dictionary<string, object?> Data = EngineData.Obj(EngineData.Load("units.json"));

    // Mot-unité → entrée VOCAB { unitWord:true }. Doublons inter-catégories fusionnés.
    public static readonly IReadOnlyList<string> Words = BuildWords();

    // Unités COMPOSÉES, reconnues UNIQUEMENT après un nombre (5 m/s). Clé = saisie
    // exacte ; valeur = LaTeX intérieur (mis en \mathrm{…} par le connecteur ·unit).
    public static readonly Dictionary<string, string> Compound = BuildCompound();

    private static List<string> BuildWords()
    {
        var seen = new HashSet<string>();
        var list = new List<string>();
        foreach (var cat in EngineData.Arr(Data["categories"]))
            foreach (var w in EngineData.Arr(cat))
            {
                var s = EngineData.Str(w);
                if (seen.Add(s)) list.Add(s);
            }
        return list;
    }

    private static Dictionary<string, string> BuildCompound()
    {
        var d = new Dictionary<string, string>();
        foreach (var kv in EngineData.Obj(Data["compound"]))
            d[kv.Key] = EngineData.Str(kv.Value);
        return d;
    }
}
