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

    // Formes « préfixables » (mot tapé → clé canonique de Vocab) : clés Vocab
    // alphabétiques (→ elles-mêmes, grec inclus) + alias alphabétiques (→ cible).
    // Précalculé une fois par culture. Cf. ADR backlog moteur #2 (préfixes).
    private readonly IReadOnlyList<KeyValuePair<string, string>> _expandable;

    private static bool IsAlphaWord(string s)
    {
        if (s.Length == 0) return false;
        foreach (var c in s)
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
        return true;
    }

    /// <summary>Mots-clés/alias dont <paramref name="word"/> est un préfixe STRICT
    /// (≥ 3 lettres, alphabétique, et word PAS une forme exacte connue — l'exact-
    /// match prime). Dédup par cible canonique, en gardant la forme la plus longue
    /// pour l'affichage. Trié (déterministe). Retour : (FormeComplète, CléCanonique).</summary>
    internal List<(string Form, string Canon)> PrefixMatches(string word)
    {
        var empty = new List<(string, string)>();
        if (word.Length < 3 || !IsAlphaWord(word)) return empty;
        // Exact-match prioritaire, y compris insensible à la casse (le lexer
        // résout « Int » → int via repli minuscule — autocapitalisation Word).
        if (Vocabulary.Vocab.ContainsKey(Canon(word))) return empty;
        var lower = word.ToLowerInvariant();
        if (lower != word && Vocabulary.Vocab.ContainsKey(Canon(lower))) return empty;

        var best = new Dictionary<string, string>(); // canon → forme affichée (la + longue)
        foreach (var kv in _expandable)
        {
            if (kv.Key.Length <= word.Length) continue;
            if (!kv.Key.StartsWith(word, System.StringComparison.Ordinal)) continue;
            if (!best.TryGetValue(kv.Value, out var cur) || kv.Key.Length > cur.Length)
                best[kv.Value] = kv.Key;
        }
        return best
            .Select(kv => (Form: kv.Value, Canon: kv.Key))
            .OrderBy(t => t.Canon, System.StringComparer.Ordinal)
            .ToList();
    }

    internal EngineCulture(char[] decimalsIn, string decimalTex, string intervalSep, string matrixEnv,
        IReadOnlyDictionary<string, string> aliases)
    {
        DecimalsIn = decimalsIn;
        DecimalTex = decimalTex;
        IntervalSep = intervalSep;
        MatrixEnv = matrixEnv;
        Aliases = aliases;

        // Index des formes préfixables (Vocab alpha → self + alias alpha → cible).
        var exp = new List<KeyValuePair<string, string>>();
        foreach (var key in Vocabulary.Vocab.Keys)
            if (IsAlphaWord(key)) exp.Add(new KeyValuePair<string, string>(key, key));
        foreach (var kv in aliases)
            if (IsAlphaWord(kv.Key)) exp.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
        _expandable = exp;
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
