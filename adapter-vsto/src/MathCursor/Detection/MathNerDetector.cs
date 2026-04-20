using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MathCursor.Detection.Sp;

namespace MathCursor.Detection
{
    /// <summary>
    /// Détecteur de zones math via modèle NER (XLM-RoBERTa fine-tuné).
    /// 3 labels BIO : O=0, B-MATH=1, I-MATH=2.
    ///
    /// Tokenizer : implémentation pure C# de SentencePiece Unigram (cf. Sp/),
    /// charge directement sentencepiece.bpe.model, aucune dépendance native.
    /// Inférence : ONNX Runtime sur model_quantized.onnx.
    /// </summary>
    public sealed class MathNerDetector : IDisposable
    {
        private const int MaxTokens = 128;
        private const double DefaultThreshold = 0.85;
        private const int LabelO = 0;
        private const int LabelBMath = 1;
        private const int LabelIMath = 2;

        private readonly InferenceSession _session;
        private readonly SentencePieceTokenizer _tokenizer;
        private readonly double _threshold;
        private bool _disposed;

        public MathNerDetector(string modelDir, double threshold = DefaultThreshold)
        {
            if (!Directory.Exists(modelDir))
                throw new DirectoryNotFoundException("Modèle NER introuvable : " + modelDir);

            var onnxPath = Path.Combine(modelDir, "model_quantized.onnx");
            var spPath = Path.Combine(modelDir, "sentencepiece.bpe.model");

            if (!File.Exists(onnxPath))
                throw new FileNotFoundException("model_quantized.onnx manquant dans " + modelDir);
            if (!File.Exists(spPath))
                throw new FileNotFoundException("sentencepiece.bpe.model manquant dans " + modelDir);

            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = 2,
            };
            _session = new InferenceSession(onnxPath, sessionOptions);

            var spModel = SentencePieceModel.LoadFromFile(spPath);
            _tokenizer = new SentencePieceTokenizer(spModel);

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
            IReadOnlyList<SentencePieceTokenizer.Token> tokens;
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
            IReadOnlyList<SentencePieceTokenizer.Token> tokens,
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
                // Skip special tokens HF (<s>=0, <pad>=1, </s>=2, <unk>=3)
                if (id <= 3) continue;

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
