// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
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
