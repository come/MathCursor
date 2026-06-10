namespace MathCursor.Engine;

/// <summary>
/// Réglages de culture du moteur : notation décimale, séparateur d'intervalle,
/// environnement matriciel rendu. Injectée en PARAMÈTRE d'<see cref="ForestEngine.Analyze"/>
/// — jamais de statique mutable (cf. ADR 2026-06-10-Feat-ribbon-columns-settings-culture).
/// Défaut moteur = <see cref="Fr"/> (contrat de fidélité des 280 fixtures).
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

    public EngineCulture(char[] decimalsIn, string decimalTex, string intervalSep, string matrixEnv)
    {
        DecimalsIn = decimalsIn;
        DecimalTex = decimalTex;
        IntervalSep = intervalSep;
        MatrixEnv = matrixEnv;
    }

    public static readonly EngineCulture Fr = new(new[] { '.', ',' }, "{,}", ";", "pmatrix");
    public static readonly EngineCulture Us = new(new[] { '.' }, ".", ",", "bmatrix");
}
