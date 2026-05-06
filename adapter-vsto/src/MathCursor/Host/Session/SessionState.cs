namespace MathCursor.Host.Session
{
    /// <summary>
    /// États de la <see cref="EquationSession"/> — modélisent le cycle de
    /// vie « user a une popup ouverte sur une zone math en cours de
    /// résolution ». Cf. ADR <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </summary>
    /// <remarks>
    /// Diagramme des transitions (validées dans <see cref="EquationSession"/>) :
    /// <code>
    /// [Idle]
    ///   → OpenOnZone  → [Open]
    ///   → EnterEditing → [Editing]
    /// [Open]
    ///   → OpenOnZone  → [Open]    (re-frappe, nouvelle zone)
    ///   → StartCommitting → [Committing]
    ///   → Reset       → [Idle]    (Esc, sortie zone)
    /// [Editing]
    ///   → StartCommitting → [Committing]
    ///   → Reset       → [Idle]    (revert, abandon)
    /// [Committing]
    ///   → Close       → [Idle]    (succès)
    ///   → Reset       → [Idle]    (échec insert)
    /// </code>
    /// </remarks>
    internal enum SessionState
    {
        Idle,
        Open,
        Editing,
        Committing,
    }
}
