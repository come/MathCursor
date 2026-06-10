namespace MathCursor.Host.Detection
{
    /// <summary>
    /// Texte de zone destiné au MOTEUR. Les bornes de zone sont trimées
    /// (l'espace tapé reste dans le doc après le commit, l'ancre popup ne
    /// bouge pas), mais le lexer distingue <c>R*␣</c> (étoile postfixe
    /// détachée → ℝ^*) de <c>R*</c> en fin d'entrée (multiplication
    /// incomplète → R×□) : l'espace EST le signal, il faut le restituer
    /// (ADR 2026-06-10-Feat-culture-scoped-aliases, régression « R*␣ »).
    ///
    /// Pure compute (pas d'interop Word) : compilé aussi par MathCursor.Tests.
    /// </summary>
    internal static class ZoneEngineText
    {
        /// <summary>Ajoute UN espace à <paramref name="zoneText"/> si le ¶ en
        /// contient un juste après <paramref name="stringEnd"/> (le lexer n'a
        /// besoin que du signal « détaché », pas du run entier).</summary>
        public static string WithTrailingSpaceSignal(string zoneText, string paragraphText, int stringEnd)
            => stringEnd < paragraphText.Length && char.IsWhiteSpace(paragraphText[stringEnd])
                ? zoneText + " "
                : zoneText;
    }
}
