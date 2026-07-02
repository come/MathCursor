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

namespace MathCursor.Host.Blocks
{
    /// <summary>Résultat de détection : « la ligne commence par un marqueur
    /// de chaîne (ou un ouvreur de système) » — offsets en coordonnées du
    /// texte d'entrée.</summary>
    internal sealed class RelationLineMatch
    {
        /// <summary>Marqueur tel que tapé (« &lt;=&gt; », « = », « { »…).</summary>
        public string MarkerTyped { get; set; } = "";
        /// <summary>LaTeX d'affichage du marqueur (« \Leftrightarrow  »…).</summary>
        public string MarkerLatex { get; set; } = "";
        /// <summary>Connecteur logique (⟺, ⟹ — colonne 1 du bloc) vs
        /// relation (=, ≤ — le signe aligné, colonne 3).</summary>
        public bool IsConnector { get; set; }
        /// <summary>Début du marqueur (après les blancs de tête).</summary>
        public int MarkerStart { get; set; }
        /// <summary>Début du RESTE (après marqueur + blancs).</summary>
        public int RestStart { get; set; }
        /// <summary>Le reste de la ligne (l'expression à analyser par le
        /// moteur). Peut être vide (ligne en cours de frappe : « = »).</summary>
        public string Rest { get; set; } = "";
    }

    /// <summary>
    /// Détection pure « ligne de chaîne » / « ouvreur de système » (M1-M2 du
    /// chantier multiligne, ADR 2026-06-10-Feat-multiline-chain-eqarr-
    /// architecture). Le moteur ne reçoit QUE le reste (relation ou `{` en
    /// tête = « erreur » côté moteur). Pure compute — compilé aussi par
    /// MathCursor.Tests.
    /// </summary>
    internal static class RelationLineDetector
    {
        /// <summary>Null si la ligne ne commence pas par un marqueur de chaîne.</summary>
        public static RelationLineMatch TryDetect(string lineText)
        {
            if (string.IsNullOrEmpty(lineText)) return null;

            int i = 0;
            while (i < lineText.Length && char.IsWhiteSpace(lineText[i])) i++;
            if (i >= lineText.Length) return null;

            var m = RelationMarkers.TryMatch(lineText, i);
            if (m == null) return null;
            var (typed, latex, isConnector) = m.Value;

            return Build(lineText, typed, latex, isConnector, i);
        }

        /// <summary>Null si la ligne ne commence pas par « { » (ouvreur de
        /// SYSTÈME d'équations — accolade qui regroupera les lignes).</summary>
        public static RelationLineMatch TryDetectSystemOpener(string lineText)
        {
            if (string.IsNullOrEmpty(lineText)) return null;

            int i = 0;
            while (i < lineText.Length && char.IsWhiteSpace(lineText[i])) i++;
            if (i >= lineText.Length || lineText[i] != '{') return null;

            return Build(lineText, "{", "\\{", isConnector: false, markerStart: i);
        }

        /// <summary>Index d'une accolade <c>{</c> NON fermée (la plus externe) dans
        /// <paramref name="text"/>, ou -1. Reconnaît un ouvreur de SYSTÈME n'importe
        /// où (« f(x) = {… », pas seulement en tête). Port de chain.rs::find_unclosed_brace
        /// (modèle matrice, ADR 2026-06-29).</summary>
        public static int FindUnclosedBrace(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            int depth = 0, pos = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '{') { if (depth == 0) pos = i; depth++; }
                else if (c == '}' && depth > 0) { depth--; if (depth == 0) pos = -1; }
            }
            return depth > 0 ? pos : -1;
        }

        /// <summary>Détache une relation FINALE du préfixe (« f(x) = » → lhs « f(x) »,
        /// markerLatex « = »). Null si le préfixe ne finit pas par une relation.
        /// Port de chain.rs::split_trailing_relation.</summary>
        public static (string Lhs, string MarkerLatex)? SplitTrailingRelation(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return null;
            string t = prefix.TrimEnd();
            foreach (var entry in RelationMarkers.Table)
            {
                string typed = entry.Typed;
                if (t.Length < typed.Length) continue;
                if (string.Compare(t, t.Length - typed.Length, typed, 0, typed.Length,
                        System.StringComparison.OrdinalIgnoreCase) != 0) continue;
                string lhs = t.Substring(0, t.Length - typed.Length);
                // marqueur-MOT (approx/environ/env) : frontière de mot avant (sinon
                // « xapprox » matcherait).
                if (char.IsLetter(typed[typed.Length - 1]) && lhs.Length > 0
                    && char.IsLetter(lhs[lhs.Length - 1])) continue;
                return (lhs.TrimEnd(), entry.Latex);
            }
            return null;
        }

        private static RelationLineMatch Build(string lineText, string typed, string latex,
            bool isConnector, int markerStart)
        {
            int rest = markerStart + typed.Length;
            while (rest < lineText.Length && lineText[rest] == ' ') rest++;

            return new RelationLineMatch
            {
                MarkerTyped = typed,
                MarkerLatex = latex,
                IsConnector = isConnector,
                MarkerStart = markerStart,
                RestStart = rest,
                Rest = lineText.Substring(rest).TrimEnd('\r', '\n'),
            };
        }
    }
}
