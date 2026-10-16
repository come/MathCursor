// MathCursor: capturing mathematical intent from linear keyboard input.
// Copyright (C) 2026  Côme de Percin
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

using System.Linq;
using MathCursor.Engine;
using MathCursor.Host.Detection;
using Xunit;

namespace MathCursor.Tests.Host.Detection
{
    /// <summary>
    /// Mise en forme Indice/Exposant de Word → marqueurs moteur (ADR
    /// 2026-10-14-Feat-word-subscript-superscript-format). Le masque s'écrit
    /// comme le POC Word : « . » normal, « _ » indice, « ^ » exposant.
    /// </summary>
    public sealed class ScriptMarkupTests
    {
        private static ScriptKind[] Mask(string m) => m.Select(c =>
            c == '_' ? ScriptKind.Sub : c == '^' ? ScriptKind.Sup : ScriptKind.None).ToArray();

        // Rend les marqueurs lisibles : [_ … _] et [^ … ^].
        private static string Show(string s) => s
            .Replace(ScriptMarkers.SubOpen.ToString(), "[_").Replace(ScriptMarkers.SubClose.ToString(), "_]")
            .Replace(ScriptMarkers.SupOpen.ToString(), "[^").Replace(ScriptMarkers.SupClose.ToString(), "^]");

        [Fact]
        public void Ligne_du_POC_Word()
        {
            // Relevé réel du POC 2026-10-14 : « F2+1=x2 =1 » / « .___..^... ».
            Assert.Equal("F[_2+1_]=x[^2^] =1", Show(ScriptMarkup.Apply("F2+1=x2 =1", Mask(".___..^..."))));
        }

        [Fact]
        public void Indice_puis_exposant_colles()
        {
            Assert.Equal("x[_1_][^2^]", Show(ScriptMarkup.Apply("x12", Mask("._^"))));
        }

        [Fact]
        public void Groupe_en_fin_de_zone_est_ferme()
        {
            Assert.Equal("f[_n+1_]", Show(ScriptMarkup.Apply("fn+1", Mask(".___"))));
        }

        [Fact]
        public void Sans_mise_en_forme_texte_inchange()
        {
            Assert.Equal("x+1", ScriptMarkup.Apply("x+1", Mask("...")));
        }

        [Fact]
        public void Masque_absent_ou_desaligne_texte_inchange()
        {
            Assert.Equal("x2", ScriptMarkup.Apply("x2", null));
            Assert.Equal("x2", ScriptMarkup.Apply("x2", Mask("._^")));
        }

        [Fact]
        public void Bout_en_bout_avec_le_moteur()
        {
            var r = ForestEngine.Analyze(ScriptMarkup.Apply("f2+1", Mask(".___")));
            Assert.Equal("auto", r.Decision);
            Assert.Equal("f_{2+1}", r.Ranked[0].Latex);
        }

        [Fact]
        public void Systeme_garde_la_mise_en_forme()
        {
            // Retour testeur : « { f exposant 2 + 1 » (branche système, ¶ entier).
            string input = ScriptMarkup.Apply("{f2+1 ; x1=0", Mask("..^^^...._.."));
            int bp = MathCursor.Host.Blocks.RelationLineDetector.FindUnclosedBrace(input);
            Assert.Equal(0, bp);
            var rows = input.Substring(bp + 1).Split(';').Select(s => s.Trim()).ToList();
            Assert.Equal("f^{2+1}", ForestEngine.Analyze(rows[0]).Ranked[0].Latex);
            Assert.Equal("x_{1}=0", ForestEngine.Analyze(rows[1]).Ranked[0].Latex);
        }

        // ── Ordinaux mis en exposant par Word (ADR 2026-10-16-Fix-word-ordinal-
        // superscript-ignored) : du texte, pas un exposant mathématique.

        [Fact]
        public void Ordinal_de_Word_est_lu_comme_texte()
        {
            // Relevé réel (logs 2026-10-16) : « 2e- » avec le « e » en exposant.
            Assert.Equal("2He + 2e-", ScriptMarkup.Apply("2He + 2e-", Mask(".......^.")));
            Assert.Equal("1er", ScriptMarkup.Apply("1er", Mask(".^^")));
            Assert.Equal("XIXe 3ème", ScriptMarkup.Apply("XIXe 3ème", Mask(".... .^^^".Replace(' ', '.'))));
        }

        [Fact]
        public void Ordinal_mode_physique_chimie_donne_l_electron()
        {
            string input = ScriptMarkup.Apply("2Fe + 2e-", Mask(".......^."));
            var r = ForestEngine.Analyze(input, EngineCulture.Fr.WithPhysChem(true));
            Assert.Equal("2\\mathrm{Fe}+2\\mathrm{e}^{-}", r.Ranked[0].Latex);
        }

        [Fact]
        public void Exposant_qui_n_est_pas_un_ordinal_reste_un_exposant()
        {
            // pas derrière un chiffre, suffixe inconnu, ou suivi d'une lettre
            Assert.Equal("x[^e^]", Show(ScriptMarkup.Apply("xe", Mask(".^"))));
            Assert.Equal("2[^x^]", Show(ScriptMarkup.Apply("2x", Mask(".^"))));
            Assert.Equal("2[^e^]x", Show(ScriptMarkup.Apply("2ex", Mask(".^."))));
            Assert.Equal("x[^2^]", Show(ScriptMarkup.Apply("x2", Mask(".^"))));
        }
    }
}
