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

using MathCursor.Host.SourceMap;
using Xunit;

namespace MathCursor.Tests.SourceMap
{
    /// <summary>
    /// Le canonicaliseur est LA réponse au « drift hash WARN-only » de
    /// word-api-helpers §5 : deux dumps WordOpenXML différant par du bruit
    /// Word (rsid, w:rPr, re-split de runs, proofErr, oMathPara/jc) doivent
    /// produire le MÊME canonique ; deux structures mathématiques distinctes
    /// (sSup vs sSub, délimiteurs différents) des canoniques DIFFÉRENTS.
    /// Les dumps in-vivo capturés par la sonde P2 du POC viendront enrichir
    /// ces cas — ceux-ci reproduisent les variantes documentées.
    /// </summary>
    public class OmmlCanonicalizerTests
    {
        private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private const string M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

        private static string Wrap(string body) =>
            "<w:document xmlns:w=\"" + W + "\" xmlns:m=\"" + M + "\"><w:body><w:p>"
            + body + "</w:p></w:body></w:document>";

        // x² en m:sSup, forme propre.
        private static string SSupClean() => Wrap(
            "<m:oMath><m:sSup><m:e><m:r><m:t>x</m:t></m:r></m:e>"
            + "<m:sup><m:r><m:t>2</m:t></m:r></m:sup></m:sSup></m:oMath>");

        // Même x² pollué : oMathPara+jc, rsid, w:rPr, m:rPr, run splitté, ctrlPr.
        private static string SSupNoisy() => Wrap(
            "<m:oMathPara><m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr>"
            + "<m:oMath><m:sSup>"
            + "<m:sSupPr><m:ctrlPr><w:rPr><w:rFonts w:ascii=\"Cambria Math\"/><w:i/></w:rPr></m:ctrlPr></m:sSupPr>"
            + "<m:e><m:r w:rsidR=\"00AB12CD\"><m:rPr><m:sty m:val=\"i\"/></m:rPr>"
            + "<w:rPr><w:rFonts w:ascii=\"Cambria Math\"/></w:rPr><m:t>x</m:t></m:r></m:e>"
            + "<m:sup><m:r><m:t>2</m:t></m:r></m:sup></m:sSup></m:oMath></m:oMathPara>");

        [Fact]
        public void BruitWord_MemeCanonique()
        {
            var clean = OmmlCanonicalizer.Canonicalize(SSupClean());
            var noisy = OmmlCanonicalizer.Canonicalize(SSupNoisy());
            Assert.NotNull(clean);
            Assert.Equal(clean, noisy);
        }

        [Fact]
        public void RunsSplittes_MemeCanonique()
        {
            // « ab » en un run vs deux runs adjacents (re-split save/reopen).
            var one = Wrap("<m:oMath><m:r><m:t>ab</m:t></m:r></m:oMath>");
            var two = Wrap("<m:oMath><m:r><m:t>a</m:t></m:r><m:r><m:t>b</m:t></m:r></m:oMath>");
            Assert.Equal(OmmlCanonicalizer.Canonicalize(one), OmmlCanonicalizer.Canonicalize(two));
        }

        [Fact]
        public void RunMultiT_EgalRunsFusionnes()
        {
            // Un m:r portant 2 m:t ≡ deux runs fusionnés.
            var multi = Wrap("<m:oMath><m:r><m:t>a</m:t><m:t>b</m:t></m:r></m:oMath>");
            var one = Wrap("<m:oMath><m:r><m:t>ab</m:t></m:r></m:oMath>");
            Assert.Equal(OmmlCanonicalizer.Canonicalize(one), OmmlCanonicalizer.Canonicalize(multi));
        }

        [Fact]
        public void SSup_vs_SSub_CanoniquesDistincts()
        {
            // x² vs x₂ : MÊME texte linéaire (collision K1 assumée), K2 doit départager.
            var sub = Wrap("<m:oMath><m:sSub><m:e><m:r><m:t>x</m:t></m:r></m:e>"
                + "<m:sub><m:r><m:t>2</m:t></m:r></m:sub></m:sSub></m:oMath>");
            Assert.NotEqual(OmmlCanonicalizer.Canonicalize(SSupClean()),
                            OmmlCanonicalizer.Canonicalize(sub));
        }

        [Fact]
        public void Delimiteurs_SemantiquePreservee()
        {
            // [x] vs (x) : la différence vit dans m:dPr/m:begChr (m:val) —
            // les *Pr sémantiques doivent SURVIVRE au filtrage.
            string Delim(string beg, string end) => Wrap(
                "<m:oMath><m:d><m:dPr><m:begChr m:val=\"" + beg + "\"/>"
                + "<m:endChr m:val=\"" + end + "\"/><m:ctrlPr/></m:dPr>"
                + "<m:e><m:r><m:t>x</m:t></m:r></m:e></m:d></m:oMath>");
            Assert.NotEqual(OmmlCanonicalizer.Canonicalize(Delim("[", "]")),
                            OmmlCanonicalizer.Canonicalize(Delim("(", ")")));
        }

        [Fact]
        public void Fraction_vs_Lineaire_CanoniquesDistincts()
        {
            var frac = Wrap("<m:oMath><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num>"
                + "<m:den><m:r><m:t>2</m:t></m:r></m:den></m:f></m:oMath>");
            var lin = Wrap("<m:oMath><m:r><m:t>1/2</m:t></m:r></m:oMath>");
            Assert.NotEqual(OmmlCanonicalizer.Canonicalize(frac),
                            OmmlCanonicalizer.Canonicalize(lin));
        }

        [Fact]
        public void EntreesInvalides_Null()
        {
            Assert.Null(OmmlCanonicalizer.Canonicalize(null));
            Assert.Null(OmmlCanonicalizer.Canonicalize(""));
            Assert.Null(OmmlCanonicalizer.Canonicalize("pas du xml <"));
            Assert.Null(OmmlCanonicalizer.Canonicalize(Wrap("<w:r><w:t>pas de math</w:t></w:r>")));
        }

        [Fact]
        public void Deterministe_DeuxAppels_MemeSortie()
        {
            Assert.Equal(OmmlCanonicalizer.Canonicalize(SSupNoisy()),
                         OmmlCanonicalizer.Canonicalize(SSupNoisy()));
        }
    }
}
