namespace MathCursor.Engine.Vocabulary
{
    /// <summary>
    /// Niveaux de précédence des opérateurs infixes, du plus fort au plus
    /// faible. La table 5-6 tiers énoncée dans le brief v4 §1.2 est étendue
    /// à 9 valeurs pour couvrir la palette FR (set ops + ∧/∨/⇒/⇔ séparés).
    ///
    /// <para>Ordre : <c>funcpow > muldiv > addsub > setop > comp > rel
    /// > and > or > implies > iff</c>. Cf. ADR
    /// <c>2026-05-22-Feat-engine-poc-isolation</c>.</para>
    /// </summary>
    public enum PrecedenceTier
    {
        // Plus fort en haut. Comparaison par (int) valeur ASC = plus fort.
        Funcpow = 0,
        Muldiv  = 1,
        Addsub  = 2,
        Setop   = 3,
        Comp    = 4,
        Rel     = 5,
        And     = 6,
        Or      = 7,
        Implies = 8,
        Iff     = 9,
    }
}
