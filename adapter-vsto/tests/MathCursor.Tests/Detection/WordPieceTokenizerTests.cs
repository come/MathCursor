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

using System.Collections.Generic;
using System.Linq;
using MathCursor.Detection.WordPiece;
using Xunit;

namespace MathCursor.Tests.Detection
{
    /// <summary>
    /// Régression bug 0.11.3 : les IDs des tokens spéciaux ([CLS]/[SEP]/…) DOIVENT
    /// être lus du vocab, pas hardcodés aux positions BERT standard (100-103). Un
    /// vocab réduit/custom (le modèle NER allégé) les met ailleurs (2/3) ; les
    /// hardcoder envoyait de faux [CLS]/[SEP] au modèle → contexte corrompu →
    /// « x2 », « U_n » non détectés en auto-détection Word. Test PUR (sans modèle).
    /// </summary>
    public sealed class WordPieceTokenizerTests
    {
        // Vocab style "réduit" : tokens spéciaux aux positions 0-4 (PAS 100-103).
        private static WordPieceTokenizer ReducedVocabTokenizer()
        {
            var vocab = new Dictionary<string, int>
            {
                ["[PAD]"] = 0, ["[UNK]"] = 1, ["[CLS]"] = 2, ["[SEP]"] = 3, ["[MASK]"] = 4,
                ["x"] = 5, ["##2"] = 6,
            };
            return new WordPieceTokenizer(vocab);
        }

        [Fact]
        public void Special_ids_are_read_from_vocab_not_hardcoded()
        {
            var tk = ReducedVocabTokenizer();
            Assert.Equal(0, tk.PadId);
            Assert.Equal(1, tk.UnkId);
            Assert.Equal(2, tk.ClsId);   // 2, pas 101
            Assert.Equal(3, tk.SepId);   // 3, pas 102
            Assert.Equal(4, tk.MaskId);
        }

        [Fact]
        public void Encode_wraps_with_vocab_resolved_cls_sep()
        {
            var tk = ReducedVocabTokenizer();
            var toks = tk.Encode("x2").ToList();
            Assert.Equal(2, toks.First().Id);        // [CLS] = 2
            Assert.Equal(3, toks.Last().Id);         // [SEP] = 3
            Assert.Contains(toks, t => t.Id == 5);   // "x"
            Assert.Contains(toks, t => t.Id == 6);   // "##2"
        }

        [Fact]
        public void Falls_back_to_bert_positions_when_special_absent()
        {
            // Vocab sans tokens spéciaux → fallback aux positions BERT standard.
            var tk = new WordPieceTokenizer(new Dictionary<string, int> { ["x"] = 5 });
            Assert.Equal(101, tk.ClsId);
            Assert.Equal(102, tk.SepId);
        }
    }
}
