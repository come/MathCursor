using System.Collections.Generic;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Résultat du filter : alts à afficher dans la popup + mapping
    /// display index → realAltIdx (de la liste full <c>Spot.Alternatives</c>).
    /// <c>AltIdxMap[displayIdx]</c> = <c>SpanOverride.AltIdxRevert</c> (-1)
    /// pour l'item "revert vers default".
    /// </summary>
    public sealed class FilteredAlts
    {
        public IReadOnlyList<AmbiguityAlternative> Built { get; }
        public IReadOnlyList<int> AltIdxMap { get; }
        /// <summary>L'altIdx considéré comme actif (= déjà appliqué dans
        /// le topLatex). <c>-1</c> si aucun.</summary>
        public int ActiveAltIdx { get; }

        public FilteredAlts(IReadOnlyList<AmbiguityAlternative> built,
            IReadOnlyList<int> altIdxMap, int activeAltIdx)
        {
            Built = built;
            AltIdxMap = altIdxMap;
            ActiveAltIdx = activeAltIdx;
        }
    }

    /// <summary>
    /// Filter pur (sans WPF) qui construit la liste d'alts à afficher dans
    /// la popup d'ambiguïté. Règles :
    /// <list type="number">
    /// <item>Cherche l'altIdx actif via <c>match.AppliedAltIdx</c> (match
    /// dont bornes == spot bounds). C'est la source de vérité posée par
    /// le <see cref="ZoneResolver"/>.</item>
    /// <item>Si un alt actif → ajoute un item "revert" (AltIdxRevert) en
    /// tête, qui affiche le <c>defaultLatex</c>.</item>
    /// <item>Itère <c>alternatives</c> et exclut celui à l'index actif.
    /// Construit <c>altIdxMap</c> qui mappe chaque position UI vers
    /// l'index réel dans <c>alternatives</c>.</item>
    /// </list>
    ///
    /// <para>Extrait de <c>SuggestionPopupWindow.Show</c> 2026-05-21 pour
    /// rendre la logique testable sans WPF (cf. bugs filter rapportés sur
    /// rule <c>tight-chain-extension</c> et <c>two-uppercase</c>).</para>
    /// </summary>
    public static class PopupAltFilter
    {
        /// <summary>
        /// Construit la liste filtrée. <paramref name="defaultLatex"/> est
        /// le texte du item revert (typiquement la steno brute / l'expression
        /// par défaut sans décoration).
        /// </summary>
        public static FilteredAlts Filter(
            int spotStart, int spotEnd,
            IReadOnlyList<AmbiguityAlternative> alternatives,
            IReadOnlyList<AmbiguityMatch> allMatches,
            string defaultLatex)
        {
            if (alternatives == null || alternatives.Count == 0)
            {
                return new FilteredAlts(
                    System.Array.Empty<AmbiguityAlternative>(),
                    System.Array.Empty<int>(),
                    activeAltIdx: -1);
            }

            // AppliedAltIdx du match au même span que le Spot (= choix user).
            int activeAltIdx = -1;
            if (allMatches != null)
            {
                for (int i = 0; i < allMatches.Count; i++)
                {
                    var m = allMatches[i];
                    if (m?.Spot == null) continue;
                    if (m.Start == spotStart && m.End == spotEnd && m.AppliedAltIdx >= 0)
                    {
                        activeAltIdx = m.AppliedAltIdx;
                        break;
                    }
                }
            }

            bool hasActive = activeAltIdx >= 0 && activeAltIdx < alternatives.Count;

            var built = new List<AmbiguityAlternative>(alternatives.Count + 1);
            var altIdxMap = new List<int>(alternatives.Count + 1);

            // Revert en tête si une alt active via pref user (permet go-back).
            // Pas de revert pour les alts juste qui « matchent default
            // rendering » — c'est de la sémantique d'affichage, pas un choix
            // user (rien à revert).
            if (hasActive)
            {
                built.Add(new AmbiguityAlternative(defaultLatex ?? string.Empty));
                altIdxMap.Add(SpanOverride.AltIdxRevert);
            }

            // Itère les alts, exclut :
            //   - l'active (= choix user, déjà affichée en final)
            //   - les alts dont Latex == defaultLatex (= rendu par défaut de
            //     l'engine, déjà affiché en final ⇒ doublon visuel sinon).
            for (int i = 0; i < alternatives.Count; i++)
            {
                if (i == activeAltIdx) continue;
                if (!string.IsNullOrEmpty(defaultLatex)
                    && string.Equals(alternatives[i].Latex, defaultLatex, System.StringComparison.Ordinal))
                    continue;
                built.Add(alternatives[i]);
                altIdxMap.Add(i);
            }

            return new FilteredAlts(built, altIdxMap, activeAltIdx);
        }
    }
}
