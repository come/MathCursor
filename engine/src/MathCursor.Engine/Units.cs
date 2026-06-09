using System.Collections.Generic;

namespace MathCursor.Engine;

// units.js — TABLE des unités (DONNÉES). Le moteur ne connaît que le flag unitWord
// + le connecteur ·unit. Une unité = mot reconnu APRÈS un nombre (1 cm, 2kg).
internal static class Units
{
    private static readonly string[][] Categories =
    {
        new[] { "nm", "um", "mm", "cm", "dm", "m", "km" },                         // LENGTH
        new[] { "mg", "g", "kg", "t" },                                            // MASS
        new[] { "ns", "us", "ms", "s", "min", "h" },                               // TIME
        new[] { "K" },                                                             // TEMP
        new[] { "A" },                                                             // CURRENT
        new[] { "mol" },                                                           // MOLE
        new[] { "cd" },                                                            // LUMEN
        new[] { "Hz", "Pa", "J", "W", "V", "F", "S", "Wb", "H", "T", "lm", "lx", "Bq", "Gy", "Sv" }, // SI_DER (N, C → ensembles)
        new[] { "L", "mL", "cL", "dL" },                                           // VOLUME
        new[] { "eV", "cal", "kcal", "Wh", "kWh" },                                // ENERGY
        new[] { "bar", "atm" },                                                    // PRESS
        new[] { "rad", "deg", "grad", "sr" },                                      // ANGLE
        new[] { "bit", "o", "ko", "Mo", "Go", "To", "B", "kB", "MB", "GB", "TB" }, // INFO
    };

    // Mot-unité → entrée VOCAB { unitWord:true }. Doublons inter-catégories fusionnés.
    public static readonly IReadOnlyList<string> Words = BuildWords();

    private static List<string> BuildWords()
    {
        var seen = new HashSet<string>();
        var list = new List<string>();
        foreach (var cat in Categories)
            foreach (var w in cat)
                if (seen.Add(w)) list.Add(w);
        return list;
    }

    // Unités COMPOSÉES, reconnues UNIQUEMENT après un nombre (5 m/s). Clé = saisie
    // exacte ; valeur = LaTeX intérieur (mis en \mathrm{…} par le connecteur ·unit).
    public static readonly Dictionary<string, string> Compound = new()
    {
        ["m/s"] = "m/s",
        ["km/h"] = "km/h",
        ["m/s2"] = "m/s^2",
        ["km/s"] = "km/s",
        ["m.s-1"] = "m\\cdot s^{-1}",
        ["m.s-2"] = "m\\cdot s^{-2}",
        ["km.h-1"] = "km\\cdot h^{-1}",
        ["rad/s"] = "rad/s",
        ["tr/min"] = "tr/min",
        ["kg/m3"] = "kg/m^3",
        ["g/mol"] = "g/mol",
        ["mol/L"] = "mol/L",
    };
}
