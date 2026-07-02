// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
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

namespace MathCursor.Host.Blocks
{
    /// <summary>
    /// Table des MARQUEURS de tête de ligne d'une chaîne de raisonnement
    /// (ADR 2026-06-10-Feat-multiline-chain-eqarr-architecture, P1) : c'est
    /// CE module qui possède les connecteurs logiques (`&lt;=&gt;`, `=&gt;`…)
    /// volontairement retirés du moteur forest, et le LaTeX d'affichage de
    /// chaque marqueur. Pure compute — compilé aussi par MathCursor.Tests.
    ///
    /// Deux GENRES de marqueurs (déterminent la mise en page eqArr) :
    /// <list type="bullet">
    /// <item><b>Connecteur</b> (⟺, ⟹…) : colonne 1 du bloc, le reste de la
    ///   ligne est une équation complète scindée au signe.</item>
    /// <item><b>Relation</b> (=, ≤…) : c'est LE signe aligné — colonne 3,
    ///   préfixé au reste.</item>
    /// </list>
    /// </summary>
    internal static class RelationMarkers
    {
        /// <summary>(forme tapée, LaTeX, connecteur ?). Ordre = longueur
        /// décroissante (plus-long-match : « &lt;=&gt; » avant « &lt;= » avant « = »).</summary>
        public static readonly IReadOnlyList<(string Typed, string Latex, bool IsConnector)> Table =
            new (string, string, bool)[]
            {
                // marqueur-MOT (frontière de mot exigée par TryMatch, sinon
                // « approximation »/« environnement » matcheraient) — demande
                // user 2026-06-12 : « approx 3,14 » en début de ligne, au même
                // titre que > etc. ; « environ »/« env » ajoutés 2026-06-19
                // (ADR Fix-environ-env-line-start-approx-marker). Plus-long
                // d'abord : « environ » avant « env » (la frontière de mot
                // suffirait, mais on garde l'invariant longueur décroissante).
                ("approx",  "\\approx ",     false),
                ("environ", "\\approx ",     false),
                ("env",     "\\approx ",     false),
                ("<=>", "\\Leftrightarrow ", true),
                ("=>",  "\\Rightarrow ",     true),
                ("<=",  "\\leq ",            false),
                (">=",  "\\geq ",            false),
                ("!=",  "\\neq ",            false),
                ("⟺",   "\\Leftrightarrow ", true),
                ("⇔",   "\\Leftrightarrow ", true),
                ("⟹",   "\\Rightarrow ",     true),
                ("⇒",   "\\Rightarrow ",     true),
                ("≤",   "\\leq ",            false),
                ("≥",   "\\geq ",            false),
                ("≠",   "\\neq ",            false),
                ("≈",   "\\approx ",         false),
                ("=",   "=",                 false),
                ("<",   "<",                 false),
                (">",   ">",                 false),
            };

        /// <summary>Plus-long-match d'un marqueur à <paramref name="start"/>.
        /// Null si aucun.</summary>
        public static (string Typed, string Latex, bool IsConnector)? TryMatch(string text, int start)
        {
            if (string.IsNullOrEmpty(text) || start < 0 || start >= text.Length) return null;
            foreach (var entry in Table)
            {
                if (start + entry.Typed.Length > text.Length) continue;
                // Insensible à la casse : Word capitalise automatiquement le
                // début de ligne (« env »→« Env », « approx »→« Approx »).
                // Sans effet sur les marqueurs symboles. ADR 2026-06-19.
                if (string.Compare(text, start, entry.Typed, 0, entry.Typed.Length,
                        System.StringComparison.OrdinalIgnoreCase) != 0)
                    continue;
                // marqueur-MOT : frontière de mot exigée (« approx 3,14 » oui,
                // « approximation » non). Sans objet pour les symboles.
                int end = start + entry.Typed.Length;
                if (char.IsLetter(entry.Typed[entry.Typed.Length - 1])
                    && end < text.Length && char.IsLetter(text[end]))
                    continue;
                return entry;
            }
            return null;
        }
    }
}
