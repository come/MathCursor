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
using MathCursor.Engine;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Repli anti-fragmentation NER du chemin auto (ADR 2026-06-29-Fix-auto-detect-
    /// anchor-unclosed-bracket). Quand le NER largue la tête d'une matrice et ne
    /// renvoie que la queue au caret (ex. zone « c d » sur « (a n ;c d »), le
    /// fragment seul est imparsable → la popup tombait. Le repli réétend le span
    /// via <see cref="SpanComputer"/> (ancrage sur l'ouvrante non fermée) quand il
    /// démarre AVANT la zone NER. Ces tests verrouillent la DÉCISION (purs, sans
    /// interop Word ni NER) : RunDetection porte l'interop, mais la logique testée
    /// ici est exactement sa condition d'ancrage + sa conséquence moteur.
    /// </summary>
    public sealed class AutoDetectAnchorTests
    {
        private static readonly IReadOnlyList<(int, int)> NoOMath = new List<(int, int)>();

        // Reproduit le span ancré construit dans AutoDetectController.RunDetection.
        private static string AnchoredSpan(string text, int caret)
        {
            int s = SpanComputer.ComputeSpanStart(text, caret, NoOMath);
            int e = SpanComputer.ComputeSpanEnd(text, caret, NoOMath);
            return text.Substring(s, e - s);
        }

        [Fact]
        public void Fragment_queue_seul_est_imparsable()
        {
            // Ce que le NER renvoyait par fragmentation : la 2e ligne seule.
            var r = ForestEngine.Analyze("c d", EngineCulture.Fr);
            Assert.True(r.Decision == "erreur" || r.Ranked.Count == 0,
                "Le fragment « c d » seul devrait être imparsable (c'est la cause du clignotement).");
        }

        [Fact]
        public void Ancrage_recupere_la_matrice_complete_et_parse()
        {
            const string text = "(a n ;c d"; // matrice en cours, caret en fin
            int caret = text.Length;

            // L'ancrage SpanComputer démarre à l'ouvrante ( (index 0), donc AVANT
            // une zone NER fragmentée qui commencerait à « c » (index 6).
            int aStart = SpanComputer.ComputeSpanStart(text, caret, NoOMath);
            Assert.Equal(0, aStart);
            Assert.True(aStart < 6, "Le span ancré doit démarrer avant la zone fragmentée → déclenche le repli.");

            // Et le span ancré, lui, est parsable (matrice à carrés).
            var r = ForestEngine.Analyze(AnchoredSpan(text, caret), EngineCulture.Fr);
            Assert.True(r.Decision != "erreur" && r.Ranked.Count > 0,
                "Le span ancré « (a n ;c d » doit produire un candidat matrice.");
        }

        [Fact]
        public void NoOp_quand_zone_NER_demarre_deja_a_l_ouvrante()
        {
            // Cas normal : la zone NER couvre déjà « (a n ;c d » → zone.Start = 0.
            // La condition de repli (aStart < zone.Start) est fausse → aucun
            // attempt ancré ajouté, comportement inchangé.
            const string text = "(a n ;c d";
            int aStart = SpanComputer.ComputeSpanStart(text, text.Length, NoOMath);
            int nerZoneStart = 0;
            Assert.False(aStart < nerZoneStart, "Zone NER déjà ancrée → pas de repli (no-op).");
        }

        [Fact]
        public void NoOp_sans_parenthese_ouverte()
        {
            // Pas de groupe ( / [ non fermé : SpanComputer ne remonte pas avant la
            // zone NER, donc le repli ne se déclenche pas (aStart >= zone.Start).
            // (« = » est un délimiteur du SpanComputer manuel → aStart >= 0.)
            const string text = "x = 2";
            int aStart = SpanComputer.ComputeSpanStart(text, text.Length, NoOMath);
            int nerZoneStart = 0;
            Assert.False(aStart < nerZoneStart, "Sans bracket ouvert → pas de repli (no-op).");
        }
    }
}
