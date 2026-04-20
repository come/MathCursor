using System;
using System.Collections.Generic;
using System.Text;

namespace MathCursor.Detection.Sp
{
    /// <summary>
    /// Tokenizer SentencePiece Unigram en C# pur.
    /// Algorithme : Viterbi pour trouver la segmentation de score max sur le vocab.
    /// Mappe les SP IDs vers les HuggingFace IDs (XLM-RoBERTa convention) :
    /// - HF id 0 = <s>, 1 = <pad>, 2 = </s>, 3 = <unk>
    /// - HF id N (N >= 4) = SP id (N - 1)
    /// </summary>
    public sealed class SentencePieceTokenizer
    {
        public const int HfBosId = 0;
        public const int HfPadId = 1;
        public const int HfEosId = 2;
        public const int HfUnkId = 3;
        public const int HfFairseqOffset = 1;
        public const char DummyPrefix = '\u2581'; // ▁

        private readonly SentencePieceModel _model;
        private readonly PieceTrie _trie;
        private readonly float _unkPenalty;

        public SentencePieceTokenizer(SentencePieceModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _trie = new PieceTrie();
            float minScore = float.MaxValue;
            for (int i = 0; i < model.Pieces.Count; i++)
            {
                var p = model.Pieces[i];
                _trie.Insert(p.Text, i, p.Score);
                if (p.Score < minScore) minScore = p.Score;
            }
            // Pénalité pour les caractères qui ne matchent aucune pièce :
            // un peu pire que la pire pièce du vocab pour les déprioriser.
            _unkPenalty = minScore - 10.0f;
        }

        /// <summary>Token avec son ID HuggingFace et ses offsets caractères dans le texte ORIGINAL.</summary>
        public sealed class Token
        {
            public int Id;
            public int CharStart;
            public int CharEnd;
        }

        /// <summary>
        /// Tokenize <paramref name="text"/> et renvoie les tokens (avec offsets dans le
        /// texte original). Ajoute <s> en début et </s> en fin (offsets = limites du texte).
        /// </summary>
        public IReadOnlyList<Token> Encode(string text)
        {
            var result = new List<Token>();
            if (string.IsNullOrEmpty(text))
            {
                result.Add(new Token { Id = HfBosId, CharStart = 0, CharEnd = 0 });
                result.Add(new Token { Id = HfEosId, CharStart = 0, CharEnd = 0 });
                return result;
            }

            // 1. Normalisation : add_dummy_prefix + escape_whitespaces
            //    On track le mapping normalisé → original pour récupérer les offsets.
            var (normalized, normToOrig) = Normalize(text);

            // 2. Viterbi sur le texte normalisé
            var pieceSegments = Viterbi(normalized);

            // 3. <s> + pièces + </s> avec offsets ORIGINAUX
            result.Add(new Token { Id = HfBosId, CharStart = 0, CharEnd = 0 });
            foreach (var seg in pieceSegments)
            {
                int hfId = SpToHfId(seg.PieceId);
                int origStart = MapToOrig(normToOrig, seg.NormStart, text.Length);
                int origEnd = MapToOrig(normToOrig, seg.NormStart + seg.NormLength, text.Length);
                result.Add(new Token { Id = hfId, CharStart = origStart, CharEnd = origEnd });
            }
            result.Add(new Token { Id = HfEosId, CharStart = text.Length, CharEnd = text.Length });
            return result;
        }

        // SP id 0 → <unk>=3 (HuggingFace XLMRobertaTokenizer convention)
        // SP id N (N > 0) → HF id (N + 1)
        private static int SpToHfId(int spId)
        {
            if (spId == 0) return HfUnkId;
            return spId + HfFairseqOffset;
        }

        /// <summary>
        /// Normalise text : add dummy prefix ▁ + remplace tous les ' ' par ▁.
        /// Renvoie le texte normalisé et un tableau qui mappe l'index normalisé
        /// vers l'index original (pour reconstruire les offsets).
        /// </summary>
        private (string normalized, int[] normToOrig) Normalize(string text)
        {
            var sb = new StringBuilder();
            var map = new List<int>();

            if (_model.AddDummyPrefix && !_model.TreatWhitespaceAsSuffix)
            {
                sb.Append(DummyPrefix);
                map.Add(0); // le prefix mappe sur la position 0
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (_model.EscapeWhitespaces && c == ' ')
                {
                    sb.Append(DummyPrefix);
                }
                else
                {
                    sb.Append(c);
                }
                map.Add(i);
            }

            if (_model.TreatWhitespaceAsSuffix && _model.AddDummyPrefix)
            {
                sb.Append(DummyPrefix);
                map.Add(text.Length);
            }

            map.Add(text.Length); // sentinelle pour MapToOrig sur normEnd
            return (sb.ToString(), map.ToArray());
        }

        private static int MapToOrig(int[] map, int normIndex, int origLength)
        {
            if (normIndex <= 0) return 0;
            if (normIndex >= map.Length) return origLength;
            return map[normIndex];
        }

        // ============================================================
        // Viterbi : trouve la segmentation qui maximise la somme des scores.
        // ============================================================

        private struct PieceSegment
        {
            public int NormStart;
            public int NormLength;
            public int PieceId;
        }

        private List<PieceSegment> Viterbi(string text)
        {
            int n = text.Length;
            // bestScore[i] = score max pour atteindre la position i
            // backPiece[i] = (start, length, pieceId) du dernier segment
            float[] bestScore = new float[n + 1];
            (int Start, int Length, int PieceId)[] backPiece = new (int, int, int)[n + 1];
            for (int i = 0; i <= n; i++)
            {
                bestScore[i] = float.NegativeInfinity;
                backPiece[i] = (-1, 0, -1);
            }
            bestScore[0] = 0;

            for (int i = 0; i < n; i++)
            {
                if (float.IsNegativeInfinity(bestScore[i])) continue;

                // Énumère toutes les pièces du vocab qui commencent à la position i
                bool foundAny = false;
                foreach (var (len, pid, score) in _trie.MatchAt(text, i))
                {
                    foundAny = true;
                    int j = i + len;
                    float candidate = bestScore[i] + score;
                    if (candidate > bestScore[j])
                    {
                        bestScore[j] = candidate;
                        backPiece[j] = (i, len, pid);
                    }
                }

                // Fallback : aucun match → traiter le caractère comme <unk> (1 char)
                if (!foundAny)
                {
                    int j = i + 1;
                    float candidate = bestScore[i] + _unkPenalty;
                    if (candidate > bestScore[j])
                    {
                        bestScore[j] = candidate;
                        backPiece[j] = (i, 1, _model.SpUnkId);
                    }
                }
            }

            // Reconstruction (de la fin vers le début)
            var segments = new List<PieceSegment>();
            int cur = n;
            while (cur > 0)
            {
                var (start, len, pid) = backPiece[cur];
                if (start < 0)
                {
                    // Pas de chemin valide — émet <unk> sur le reste
                    segments.Add(new PieceSegment { NormStart = 0, NormLength = cur, PieceId = _model.SpUnkId });
                    break;
                }
                segments.Add(new PieceSegment { NormStart = start, NormLength = len, PieceId = pid });
                cur = start;
            }
            segments.Reverse();
            return segments;
        }
    }
}
