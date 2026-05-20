using System;
using System.Collections.Generic;
using MathCursor.Detection;

namespace MathCursor.Host.Detection
{
    /// <summary>
    /// Helpers purs pour ajuster les <see cref="DetectedZone"/> produites
    /// par le NER avant qu'elles soient passées à l'engine.
    ///
    /// <para>Bounded context "raffinage de zone détectée" (P2.14 du refactor
    /// archi). Toutes les méthodes sont statiques et purement fonctionnelles —
    /// pas de Word, pas de state. Testables hors Word.</para>
    /// </summary>
    internal static class ZoneRefiner
    {
        /// <summary>
        /// Mots-clés FR/EN dont la zone détectée juste à droite doit être
        /// étendue rétroactivement pour les inclure. Ex: "limite" devant
        /// "x→0 f(x)" → la zone capture le mot-clé pour que l'engine produise
        /// <c>\lim</c>.
        /// </summary>
        public static readonly HashSet<string> MathPrefixKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "lim", "limite", "lmt",
            "sqrt", "rac", "racine",
            "int", "integrale", "integ", "integral",
            "sum", "somme",
            "forall", "qq", "qqe",
            "exists", "existe",
            "vec", "vect", "vecteur",
        };

        /// <summary>
        /// Si le caret est juste après la zone avec UNIQUEMENT du whitespace
        /// entre, on pousse l'end de la zone jusqu'au caret (l'user a tapé
        /// un espace pour continuer la formule).
        /// </summary>
        public static DetectedZone TryExtendForwardWhitespace(string paragraph, DetectedZone zone, int caret)
        {
            if (zone == null || string.IsNullOrEmpty(paragraph)) return zone;
            if (caret <= zone.End) return zone;
            int gap = caret - zone.End;
            if (gap > 5) return zone;
            for (int i = zone.End; i < caret && i < paragraph.Length; i++)
                if (!char.IsWhiteSpace(paragraph[i])) return zone;
            int newEnd = Math.Min(caret, paragraph.Length);
            string newText = paragraph.Substring(zone.Start, newEnd - zone.Start);
            return new DetectedZone(zone.Start, newEnd, newText, zone.Confidence);
        }

        /// <summary>
        /// Si le mot juste avant la zone est un <see cref="MathPrefixKeywords"/>,
        /// l'inclut dans la zone (ex: "limite" + "x→0 f(x)" → 1 zone unique).
        /// </summary>
        public static DetectedZone ExtendBackwardWithKeyword(string paragraph, DetectedZone zone)
        {
            if (string.IsNullOrEmpty(paragraph) || zone == null) return zone;

            int i = zone.Start;
            while (i > 0 && char.IsWhiteSpace(paragraph[i - 1])) i--;
            int wordEnd = i;
            while (i > 0 && char.IsLetter(paragraph[i - 1])) i--;
            int wordStart = i;
            if (wordEnd <= wordStart) return zone;

            string prevWord = paragraph.Substring(wordStart, wordEnd - wordStart);
            if (!MathPrefixKeywords.Contains(prevWord)) return zone;

            int newEnd = zone.End;
            int newStart = wordStart;
            if (newStart >= 0 && newEnd <= paragraph.Length && newEnd > newStart)
            {
                string newText = paragraph.Substring(newStart, newEnd - newStart);
                return new DetectedZone(newStart, newEnd, newText, zone.Confidence);
            }
            return zone;
        }

        /// <summary>
        /// Jette les zones NER qui chevauchent une région OMath : ces zones
        /// sont déjà converties, pas besoin de les re-proposer.
        /// </summary>
        public static IReadOnlyList<DetectedZone> FilterOutOMathOverlap(
            IReadOnlyList<DetectedZone> zones, IReadOnlyList<(int start, int end)> regions)
        {
            if (zones == null || zones.Count == 0 || regions == null || regions.Count == 0)
                return zones ?? Array.Empty<DetectedZone>();
            var kept = new List<DetectedZone>(zones.Count);
            foreach (var z in zones)
            {
                bool overlaps = false;
                foreach (var (s, e) in regions)
                {
                    if (z.End > s && z.Start < e) { overlaps = true; break; }
                }
                if (!overlaps) kept.Add(z);
            }
            return kept;
        }

        /// <summary>
        /// Choisit la zone la plus proche du caret. Distance = 0 si dedans ou
        /// collée au bord, sinon nombre de chars entre caret et bord le plus
        /// proche de la zone.
        /// </summary>
        public static DetectedZone PickNearestZone(IReadOnlyList<DetectedZone> zones, int caret, out int bestDist)
        {
            DetectedZone best = null;
            bestDist = int.MaxValue;
            foreach (var z in zones)
            {
                int dist;
                if (caret >= z.Start && caret <= z.End) dist = 0;
                else if (caret < z.Start) dist = z.Start - caret;
                else dist = caret - z.End;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = z;
                }
            }
            return best;
        }
    }
}
