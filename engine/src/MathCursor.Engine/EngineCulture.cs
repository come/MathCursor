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

using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Engine;

/// <summary>
/// Réglages de culture du moteur : notation décimale, séparateur d'intervalle,
/// environnement matriciel rendu, alias lexicaux actifs. Injectée en PARAMÈTRE
/// d'<see cref="ForestEngine.Analyze"/> — jamais de statique mutable (cf. ADR
/// 2026-06-10-Feat-ribbon-columns-settings-culture). Défaut moteur =
/// <see cref="Fr"/> (contrat de fidélité des 280 fixtures).
/// </summary>
public sealed class EngineCulture
{
    /// <summary>Caractères acceptés comme séparateur décimal en ENTRÉE (entre deux chiffres).</summary>
    public char[] DecimalsIn { get; }

    /// <summary>LaTeX émis pour le séparateur décimal (FR : <c>{,}</c> — virgule sans espace).</summary>
    public string DecimalTex { get; }

    /// <summary>Séparateur des bornes d'intervalle (FR : <c>;</c>, US : <c>,</c>).</summary>
    public string IntervalSep { get; }

    /// <summary>Environnement matriciel rendu : <c>pmatrix</c> (parenthèses) ou <c>bmatrix</c> (crochets).</summary>
    public string MatrixEnv { get; }

    /// <summary>Alias lexicaux actifs (mot saisi → clé canonique du vocabulaire),
    /// résolus par le lexer via <see cref="Canon"/>. Set fusionné générique+langue,
    /// précalculé par <see cref="Vocabulary"/> (ADR 2026-06-10-Feat-culture-scoped-aliases).</summary>
    internal IReadOnlyDictionary<string, string> Aliases { get; }

    internal string Canon(string w) => Aliases.TryGetValue(w, out var c) ? c : w;

    internal EngineCulture(char[] decimalsIn, string decimalTex, string intervalSep, string matrixEnv,
        IReadOnlyDictionary<string, string> aliases)
    {
        DecimalsIn = decimalsIn;
        DecimalTex = decimalTex;
        IntervalSep = intervalSep;
        MatrixEnv = matrixEnv;
        Aliases = aliases;
    }

    /// <summary>Clone du preset avec les réglages utilisateur non-null appliqués.
    /// Préserve tous les autres champs (alias inclus, et champs futurs) — l'adapter
    /// n'a pas à reconstruire la culture champ par champ.</summary>
    public EngineCulture WithOverrides(string? intervalSep = null, string? matrixEnv = null)
        => intervalSep == null && matrixEnv == null
            ? this
            : new EngineCulture(DecimalsIn, DecimalTex, intervalSep ?? IntervalSep, matrixEnv ?? MatrixEnv, Aliases);

    public static readonly EngineCulture Fr = FromData("fr", Vocabulary.AliasesFr);
    public static readonly EngineCulture Us = FromData("us", Vocabulary.AliasesUs);

    // Réglages (décimal/intervalle/matrice) chargés depuis data/engine/cultures.json
    // — source unique partagée avec le port Python (cf. ADR portable-engine).
    private static EngineCulture FromData(string key, IReadOnlyDictionary<string, string> aliases)
    {
        var cultures = EngineData.Obj(EngineData.Obj(EngineData.Load("cultures.json"))["cultures"]);
        var c = EngineData.Obj(cultures[key]);
        var decimalsIn = EngineData.Arr(c["decimalsIn"]).Select(x => EngineData.Str(x)[0]).ToArray();
        return new EngineCulture(decimalsIn, EngineData.Str(c["decimalTex"]),
            EngineData.Str(c["intervalSep"]), EngineData.Str(c["matrixEnv"]), aliases);
    }
}
