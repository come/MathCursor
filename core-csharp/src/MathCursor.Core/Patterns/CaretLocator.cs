using System.Collections.Generic;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Service pur qui, étant donnée une liste de <see cref="AmbiguityMatch"/>
    /// et une position curseur, retourne le match « le plus profond » contenant
    /// le caret (= celui dont le span <c>[Start..End]</c> est le plus petit
    /// parmi ceux qui contiennent la position).
    ///
    /// <para>Utilisé par <see cref="ZoneResolver"/> pour exposer à la popup le
    /// <c>Spot</c> le plus pertinent quand l'utilisateur navigue le curseur
    /// dans une zone qui contient plusieurs ambiguïtés (ex. <c>AB+AC=AD</c> :
    /// caret entre <c>AC</c> → spot = AC, pas rightmost AD).</para>
    ///
    /// <para>Convention <c>caret == End</c> : <b>inclus</b>. Le caret juste
    /// après le dernier caractère d'un match est considéré comme appartenant
    /// au match (focus reste sur ce qu'on vient de finir de taper plutôt que
    /// de sauter au voisin de droite).</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>.
    /// Étape P1 du plan d'organisation Patterns.</para>
    /// </summary>
    public static class CaretLocator
    {
        /// <summary>
        /// Retourne le <see cref="AmbiguityMatch"/> au plus petit span dont
        /// <c>[Start..End]</c> contient <paramref name="caretOffset"/>.
        /// Retourne <c>null</c> si :
        /// <list type="bullet">
        ///   <item><paramref name="matches"/> est null ou vide ;</item>
        ///   <item>aucun match ne contient le caret ;</item>
        ///   <item><paramref name="caretOffset"/> est négatif.</item>
        /// </list>
        /// En cas d'égalité de span minimal (rare), retourne le premier
        /// rencontré dans l'ordre d'énumération de <paramref name="matches"/>
        /// (déterministe : ordre d'émission des scanners).
        /// </summary>
        public static AmbiguityMatch? FindDeepestMatchAtCaret(
            IReadOnlyList<AmbiguityMatch>? matches, int caretOffset)
        {
            if (matches == null || matches.Count == 0) return null;
            if (caretOffset < 0) return null;

            AmbiguityMatch? best = null;
            int bestSpan = int.MaxValue;

            for (int i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                if (m == null) continue;
                if (caretOffset < m.Start || caretOffset > m.End) continue;
                int span = m.End - m.Start;
                if (span < bestSpan)
                {
                    best = m;
                    bestSpan = span;
                }
            }
            return best;
        }
    }
}
