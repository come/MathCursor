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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MathCursor.Detection.WordPiece
{
    /// <summary>
    /// Tokenizer WordPiece (BERT/DistilBERT) en C# pur. Charge le vocab.txt
    /// produit par HuggingFace (un token par ligne, id = numéro de ligne) et
    /// applique l'algorithme greedy longest-match avec subwords <c>##</c>.
    ///
    /// Modèle cible : distilbert-base-multilingual-cased :
    /// <list type="bullet">
    /// <item>do_lower_case = false (CASED)</item>
    /// <item>vocab size 119547</item>
    /// <item>special tokens : [PAD]=0, [UNK]=100, [CLS]=101, [SEP]=102, [MASK]=103</item>
    /// </list>
    ///
    /// Aucune dépendance native, aucune dépendance NuGet supplémentaire — on
    /// suit le même parti que le SentencePiece custom historique (cf. dossier
    /// <c>Sp/</c>, supprimé dans la phase distilmult).
    /// </summary>
    public sealed class WordPieceTokenizer
    {
        // IDs des tokens spéciaux LUS DU VOCAB (et non hardcodés). Un vocab
        // réduit/custom ne les met pas forcément aux positions BERT standard :
        // bug 0.11.3 — sur le modèle NER allégé, [CLS]/[SEP] sont aux IDs 2/3
        // (pas 101/102). Les hardcoder envoyait de FAUX [CLS]/[SEP] au modèle
        // (des sous-mots aléatoires) → contexte corrompu → prédiction « O »,
        // surtout sur les entrées COURTES (« x2 », « U_n », « 2x+1 ») dominées
        // par ces bornes. Fallback = positions BERT standard si absent du vocab.
        public readonly int PadId;
        public readonly int UnkId;
        public readonly int ClsId;
        public readonly int SepId;
        public readonly int MaskId;

        private const string SubwordPrefix = "##";
        private const int MaxCharsPerWord = 100; // sécurité

        private readonly Dictionary<string, int> _vocab;

        public WordPieceTokenizer(Dictionary<string, int> vocab)
        {
            _vocab = vocab ?? throw new ArgumentNullException(nameof(vocab));
            PadId  = ResolveSpecial("[PAD]", 0);
            UnkId  = ResolveSpecial("[UNK]", 100);
            ClsId  = ResolveSpecial("[CLS]", 101);
            SepId  = ResolveSpecial("[SEP]", 102);
            MaskId = ResolveSpecial("[MASK]", 103);
        }

        private int ResolveSpecial(string tok, int fallback)
            => _vocab.TryGetValue(tok, out var id) ? id : fallback;

        /// <summary>Charge le vocab.txt (un token par ligne) → Dict.</summary>
        public static WordPieceTokenizer LoadFromVocab(string vocabPath)
        {
            if (!File.Exists(vocabPath))
                throw new FileNotFoundException("vocab.txt manquant : " + vocabPath);
            var dict = new Dictionary<string, int>(StringComparer.Ordinal);
            int id = 0;
            foreach (var line in File.ReadAllLines(vocabPath, Encoding.UTF8))
            {
                // BERT vocab files use exact tokens, including special tokens.
                // Pas de trim — un line vide pourrait théoriquement être un token,
                // mais en pratique les vocabs HF n'en ont pas.
                if (!dict.ContainsKey(line)) dict[line] = id;
                id++;
            }
            return new WordPieceTokenizer(dict);
        }

        /// <summary>Token avec son ID et ses offsets dans le texte original.</summary>
        public sealed class Token
        {
            public int Id;
            public int CharStart;
            public int CharEnd;
        }

        /// <summary>
        /// Tokenize <paramref name="text"/> : ajoute [CLS] en début, [SEP] en
        /// fin (offsets aux limites du texte). Pre-tokenization sur whitespace
        /// + ponctuation, puis greedy WordPiece sur chaque mot.
        /// </summary>
        public IReadOnlyList<Token> Encode(string text)
        {
            var result = new List<Token>();
            if (text == null) text = "";

            // [CLS]
            result.Add(new Token { Id = ClsId, CharStart = 0, CharEnd = 0 });

            // 1) Pre-tokenize : split sur whitespace + ponctuation, garde les
            //    offsets de chaque mot dans le texte original.
            foreach (var word in PreTokenize(text))
            {
                // 2) Greedy WordPiece sur ce mot
                EncodeWord(word.Text, word.Start, result);
            }

            // [SEP]
            result.Add(new Token { Id = SepId, CharStart = text.Length, CharEnd = text.Length });
            return result;
        }

        // ------------------------------------------------------------
        // Pre-tokenization
        // ------------------------------------------------------------

        private struct WordSpan
        {
            public string Text;
            public int Start;
            public int End;
        }

        /// <summary>
        /// Pre-tokenize : split sur whitespace + ponctuation. Chaque caractère
        /// de ponctuation devient son propre "mot" (BERT convention). Garde les
        /// offsets dans le texte original.
        /// </summary>
        private static IEnumerable<WordSpan> PreTokenize(string text)
        {
            var sb = new StringBuilder();
            int wordStart = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        yield return new WordSpan { Text = sb.ToString(), Start = wordStart, End = i };
                        sb.Clear();
                        wordStart = -1;
                    }
                }
                else if (IsPunctuation(c))
                {
                    if (sb.Length > 0)
                    {
                        yield return new WordSpan { Text = sb.ToString(), Start = wordStart, End = i };
                        sb.Clear();
                        wordStart = -1;
                    }
                    // La ponctuation seule est son propre token
                    yield return new WordSpan { Text = c.ToString(), Start = i, End = i + 1 };
                }
                else
                {
                    if (sb.Length == 0) wordStart = i;
                    sb.Append(c);
                }
            }
            if (sb.Length > 0)
                yield return new WordSpan { Text = sb.ToString(), Start = wordStart, End = text.Length };
        }

        /// <summary>
        /// Convention BERT : tout caractère qui n'est ni lettre, ni chiffre, ni
        /// underscore est traité comme ponctuation (et devient son propre token).
        /// </summary>
        private static bool IsPunctuation(char c)
        {
            // ASCII punctuation
            if ((c >= '!' && c <= '/') || (c >= ':' && c <= '@')
                || (c >= '[' && c <= '`') || (c >= '{' && c <= '~'))
                return true;
            // Unicode categories de ponctuation (P*)
            var cat = char.GetUnicodeCategory(c);
            switch (cat)
            {
                case System.Globalization.UnicodeCategory.ConnectorPunctuation:
                case System.Globalization.UnicodeCategory.DashPunctuation:
                case System.Globalization.UnicodeCategory.OpenPunctuation:
                case System.Globalization.UnicodeCategory.ClosePunctuation:
                case System.Globalization.UnicodeCategory.InitialQuotePunctuation:
                case System.Globalization.UnicodeCategory.FinalQuotePunctuation:
                case System.Globalization.UnicodeCategory.OtherPunctuation:
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------
        // WordPiece greedy longest-match
        // ------------------------------------------------------------

        /// <summary>
        /// Pour un mot donné (déjà sans whitespace ni ponctuation autour) :
        /// cherche le plus long préfixe dans le vocab, puis recommence sur le
        /// suffixe avec préfixe <c>##</c>. Si aucun préfixe ne matche, [UNK]
        /// pour le mot entier (convention BERT).
        /// </summary>
        private void EncodeWord(string word, int wordStartInText, List<Token> output)
        {
            if (word.Length > MaxCharsPerWord)
            {
                output.Add(new Token { Id = UnkId, CharStart = wordStartInText, CharEnd = wordStartInText + word.Length });
                return;
            }

            var subTokens = new List<Token>();
            int start = 0;
            while (start < word.Length)
            {
                int end = word.Length;
                string matchedSub = null;
                int matchedId = -1;
                while (start < end)
                {
                    string sub = (start == 0) ? word.Substring(start, end - start)
                                              : SubwordPrefix + word.Substring(start, end - start);
                    if (_vocab.TryGetValue(sub, out int id))
                    {
                        matchedSub = sub;
                        matchedId = id;
                        break;
                    }
                    end--;
                }
                if (matchedSub == null)
                {
                    // Aucun préfixe trouvé pour ce caractère → mot entier en [UNK]
                    output.Add(new Token { Id = UnkId, CharStart = wordStartInText, CharEnd = wordStartInText + word.Length });
                    return;
                }
                int subLen = (start == 0) ? matchedSub.Length : matchedSub.Length - SubwordPrefix.Length;
                subTokens.Add(new Token
                {
                    Id = matchedId,
                    CharStart = wordStartInText + start,
                    CharEnd = wordStartInText + start + subLen,
                });
                start += subLen;
            }
            output.AddRange(subTokens);
        }
    }
}
