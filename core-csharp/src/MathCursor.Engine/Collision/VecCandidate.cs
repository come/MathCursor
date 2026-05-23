namespace MathCursor.Engine.Collision
{
    /// <summary>
    /// Critères communs pour identifier un mot vec-candidat (= 1 lettre OU
    /// 2 majuscules adjacentes). Brief : notation géométrique standard.
    /// </summary>
    internal static class VecCandidate
    {
        public static bool IsVecCandidate(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Length == 1 && char.IsLetter(text[0])) return true;
            if (text.Length == 2
                && char.IsUpper(text[0]) && char.IsUpper(text[1])) return true;
            return false;
        }
    }
}
