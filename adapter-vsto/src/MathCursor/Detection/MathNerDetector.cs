using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MathCursor.Detection.WordPiece;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MathCursor.Detection
{
    /// <summary>
    /// Détecteur de zones math via modèle NER DistilBERT-multilingual fine-tuné.
    /// 3 labels BIO : O=0, B-MATH=1, I-MATH=2.
    ///
    /// Tokenizer : implémentation pure C# de WordPiece (cf. <c>WordPiece/</c>),
    /// charge directement <c>vocab.txt</c>, aucune dépendance native ni NuGet
    /// supplémentaire. Inférence : ONNX Runtime sur model_quantized.onnx (~129 Mo
    /// vs 265 Mo pour XLM-R, latence ~25 ms vs 50 ms).
    ///
    /// API publique inchangée par rapport à l'historique XLM-R — seuls
    /// l'implémentation interne et le format des fichiers modèle ont évolué.
    /// </summary>
    public sealed class MathNerDetector : IDisposable
    {
        private const int MaxTokens = 128;
        private const double DefaultThreshold = 0.85;
        private const int LabelO = 0;
        private const int LabelBMath = 1;
        private const int LabelIMath = 2;

        private readonly InferenceSession _session;
        private readonly WordPieceTokenizer _tokenizer;
        private readonly double _threshold;
        private bool _disposed;

        public MathNerDetector(string modelDir, double threshold = DefaultThreshold)
        {
            if (!Directory.Exists(modelDir))
                throw new DirectoryNotFoundException("Modèle NER introuvable : " + modelDir);

            var onnxPath = Path.Combine(modelDir, "model_quantized.onnx");
            var vocabPath = Path.Combine(modelDir, "vocab.txt");

            if (!File.Exists(onnxPath))
                throw new FileNotFoundException("model_quantized.onnx manquant dans " + modelDir);
            if (!File.Exists(vocabPath))
                throw new FileNotFoundException("vocab.txt manquant dans " + modelDir);

            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = 2,
            };
            _session = new InferenceSession(onnxPath, sessionOptions);
            _tokenizer = WordPieceTokenizer.LoadFromVocab(vocabPath);
            _threshold = threshold;
        }

        public Task WarmUpAsync()
        {
            return Task.Run(() => Detect("x = 1"));
        }

        public IReadOnlyList<DetectedZone> Detect(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<DetectedZone>();

            // 1. Tokenization (avec offsets)
            IReadOnlyList<WordPieceTokenizer.Token> tokens;
            try { tokens = _tokenizer.Encode(text); }
            catch { return Array.Empty<DetectedZone>(); }
            if (tokens.Count == 0) return Array.Empty<DetectedZone>();

            // 2. Truncation à MaxTokens
            int n = Math.Min(tokens.Count, MaxTokens);
            var inputIds = new long[n];
            var attentionMask = new long[n];
            for (int i = 0; i < n; i++)
            {
                inputIds[i] = tokens[i].Id;
                attentionMask[i] = 1;
            }

            // 3. Inférence ONNX
            var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, n });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { 1, n });

            var inputs = new[]
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            };

            int[] labels;
            double[] confidences;
            using (var results = _session.Run(inputs))
            {
                var logits = results.First().AsTensor<float>();
                labels = new int[n];
                confidences = new double[n];
                for (int i = 0; i < n; i++)
                {
                    var (label, conf) = ArgmaxSoftmax(logits, i);
                    labels[i] = label;
                    confidences[i] = conf;
                }
            }

            // 4. Décodage BIO → spans avec offsets caractères
            return DecodeBio(text, tokens, labels, confidences, n);
        }

        private static (int label, double confidence) ArgmaxSoftmax(Tensor<float> logits, int tokenIdx)
        {
            float l0 = logits[0, tokenIdx, 0];
            float l1 = logits[0, tokenIdx, 1];
            float l2 = logits[0, tokenIdx, 2];
            float max = Math.Max(l0, Math.Max(l1, l2));
            double e0 = Math.Exp(l0 - max);
            double e1 = Math.Exp(l1 - max);
            double e2 = Math.Exp(l2 - max);
            double sum = e0 + e1 + e2;
            double p0 = e0 / sum;
            double p1 = e1 / sum;
            double p2 = e2 / sum;
            if (p0 >= p1 && p0 >= p2) return (LabelO, p0);
            if (p1 >= p2) return (LabelBMath, p1);
            return (LabelIMath, p2);
        }

        private IReadOnlyList<DetectedZone> DecodeBio(
            string text,
            IReadOnlyList<WordPieceTokenizer.Token> tokens,
            int[] labels, double[] confidences, int n)
        {
            var spans = new List<DetectedZone>();
            int? currentStart = null;
            int currentEnd = 0;
            double confSum = 0;
            int confCount = 0;

            for (int i = 0; i < n; i++)
            {
                int id = tokens[i].Id;
                // Skip special tokens — IDs LUS DU VOCAB via le tokenizer (un vocab
                // réduit ne les met pas aux positions BERT standard, cf. bug 0.11.3).
                // [UNK] est laissé passer car peut faire partie d'un span MATH.
                if (id == _tokenizer.PadId
                    || id == _tokenizer.ClsId
                    || id == _tokenizer.SepId
                    || id == _tokenizer.MaskId)
                    continue;

                int label = labels[i];
                int tokStart = tokens[i].CharStart;
                int tokEnd = tokens[i].CharEnd;

                if (label == LabelBMath)
                {
                    if (currentStart.HasValue)
                    {
                        spans.Add(BuildZone(text, currentStart.Value, currentEnd, confSum, confCount));
                    }
                    currentStart = tokStart;
                    currentEnd = tokEnd;
                    confSum = confidences[i];
                    confCount = 1;
                }
                else if (label == LabelIMath && currentStart.HasValue)
                {
                    currentEnd = tokEnd;
                    confSum += confidences[i];
                    confCount++;
                }
                else if (label == LabelO && currentStart.HasValue)
                {
                    spans.Add(BuildZone(text, currentStart.Value, currentEnd, confSum, confCount));
                    currentStart = null;
                    confSum = 0;
                    confCount = 0;
                }
            }
            if (currentStart.HasValue)
            {
                spans.Add(BuildZone(text, currentStart.Value, currentEnd, confSum, confCount));
            }

            return spans.Where(z => z.Confidence >= _threshold).ToList();
        }

        private static DetectedZone BuildZone(string text, int start, int end, double confSum, int count)
        {
            int safeStart = Math.Max(0, Math.Min(start, text.Length));
            int safeEnd = Math.Max(safeStart, Math.Min(end, text.Length));
            return new DetectedZone(
                safeStart, safeEnd,
                text.Substring(safeStart, safeEnd - safeStart),
                count > 0 ? confSum / count : 0.0);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _session?.Dispose(); } catch { }
        }
    }
}
