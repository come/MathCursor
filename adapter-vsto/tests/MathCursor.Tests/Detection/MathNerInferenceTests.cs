using System.Collections.Generic;
using System.Linq;
using MathCursor.Detection;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Tests.Detection
{
    /// <summary>
    /// Tests d'inférence NER offline : charge le modèle ONNX local
    /// (models/distilmult-v4) et fait passer un échantillon du corpus, calcule
    /// un F1 span-level et asserte un seuil minimal.
    ///
    /// But : si quelqu'un re-train le modèle ou bumpe une version d'OnnxRuntime
    /// et que la perf chute drastiquement, on s'en aperçoit avant la prod.
    ///
    /// Skip transparent si le modèle n'est pas dispo localement (cas dev qui
    /// n'a pas downloadé le modèle de 129 Mo).
    /// </summary>
    public sealed class MathNerInferenceTests : IClassFixture<NerCorpusFixture>
    {
        private readonly NerCorpusFixture _fix;
        private readonly ITestOutputHelper _log;

        public MathNerInferenceTests(NerCorpusFixture fix, ITestOutputHelper log)
        {
            _fix = fix;
            _log = log;
        }

        // ---- Sanity : modèle se charge et détecte au moins un span ----

        [SkippableFact]
        public void Detector_loads_and_detects_simple_math()
        {
            Skip.IfNot(_fix.Available, _fix.SkipReason);
            var zones = _fix.Detector.Detect("On a f(x) = 2x + 1 et c'est tout.");
            Assert.NotEmpty(zones);
            // Le span doit recouvrir la formule, pas juste un mot.
            Assert.Contains(zones, z => z.End - z.Start >= 5);
        }

        [SkippableFact]
        public void Detector_returns_empty_on_pure_text()
        {
            Skip.IfNot(_fix.Available, _fix.SkipReason);
            // Texte sans formule math → aucune zone détectée (ou zones très
            // courtes filtrées par le seuil de confidence).
            var zones = _fix.Detector.Detect("Le chat dort sur le canapé du salon.");
            Assert.Empty(zones);
        }

        [SkippableFact]
        public void Detector_returns_empty_on_empty_input()
        {
            Skip.IfNot(_fix.Available, _fix.SkipReason);
            Assert.Empty(_fix.Detector.Detect(""));
        }

        // ---- F1 span-level sur regression_v1_gold (filet anti-rechute) ----

        [SkippableFact]
        public void Regression_gold_corpus_f1_above_threshold()
        {
            Skip.IfNot(_fix.Available, _fix.SkipReason);
            var examples = _fix.LoadCorpus("regression_v1_gold.jsonl");
            Assert.NotEmpty(examples);

            var metrics = EvaluateF1(_fix.Detector, examples);
            _log.WriteLine($"regression_v1_gold ({examples.Count} ex) :" +
                $" P={metrics.Precision:F3} R={metrics.Recall:F3} F1={metrics.F1:F3}" +
                $" (TP={metrics.TruePositives} FP={metrics.FalsePositives} FN={metrics.FalseNegatives})");

            // Seuil : 0.90. Baseline mesurée 2026-04-30 sur distilmult-v5
            // (modèle Colab fraîchement déployé) = F1 0.954 (P/R 0.954,
            // TP=62 FP=3 FN=3). Le seuil est calé ~5% sous la baseline
            // pour absorber les fluctuations sans masquer une vraie
            // régression. Bumpé depuis 0.75 (v4) car v5 est nettement
            // meilleur — on ne veut pas accepter un retour à v4 silencieux.
            Assert.True(metrics.F1 >= 0.90,
                $"F1 sur regression_v1_gold = {metrics.F1:F3} (seuil 0.90, baseline 2026-04-30 v5 ≈ 0.95). " +
                $"Régression du modèle ou de l'inférence — investiguer avant release.");
        }

        // ---- F1 sur sample du test set (perf baseline du modèle) ----

        [SkippableFact]
        public void Test_corpus_sample_f1_above_baseline()
        {
            Skip.IfNot(_fix.Available, _fix.SkipReason);
            // Échantillon de 100 exemples du test set — assez pour avoir un
            // F1 stable, pas trop pour rester rapide (~3-4s sur ce hardware).
            var examples = _fix.LoadCorpus("test.jsonl", limit: 100);
            Assert.NotEmpty(examples);

            var metrics = EvaluateF1(_fix.Detector, examples);
            _log.WriteLine($"test.jsonl[:100] :" +
                $" P={metrics.Precision:F3} R={metrics.Recall:F3} F1={metrics.F1:F3}" +
                $" (TP={metrics.TruePositives} FP={metrics.FalsePositives} FN={metrics.FalseNegatives})");

            // Seuil : 0.95. Baseline mesurée 2026-04-30 sur distilmult-v5
            // = F1 1.000 (TP=102 FP=0 FN=0) sur les 100 premiers de test.jsonl.
            // Marge 5% sous la baseline.
            Assert.True(metrics.F1 >= 0.95,
                $"F1 sur test.jsonl[:100] = {metrics.F1:F3} (seuil 0.95, baseline 2026-04-30 v5 ≈ 1.00). " +
                $"Le modèle a une perf dégradée par rapport à la baseline.");
        }

        // ---- Échantillon v6 (features récentes : vecteurs, coordonnées) ----

        [SkippableFact]
        public void Recent_features_corpus_f1_above_threshold()
        {
            Skip.IfNot(_fix.Available, _fix.SkipReason);
            var examples = _fix.LoadCorpus("extension_v6_recent_features.jsonl", limit: 80);
            Assert.NotEmpty(examples);

            var metrics = EvaluateF1(_fix.Detector, examples);
            _log.WriteLine($"extension_v6_recent_features[:80] :" +
                $" P={metrics.Precision:F3} R={metrics.Recall:F3} F1={metrics.F1:F3}");

            // Seuil : 0.90. Baseline distilmult-v5 = F1 1.000 sur 80 premiers
            // de extension_v6 — le modèle a été retrain spécifiquement sur ces
            // features récentes (vecteurs/coords). Si on passe sous 0.90, le
            // re-training a été dégradé sur cette extension.
            Assert.True(metrics.F1 >= 0.90,
                $"F1 sur extension_v6 = {metrics.F1:F3} (seuil 0.90, baseline 2026-04-30 v5 ≈ 1.00). " +
                $"Les features récentes (vecteurs/coords) sont mal détectées.");
        }

        // ---- Échantillon v8 (formes courtes n-aires, iint/iiint, mots-clés nus) ----

        [SkippableFact]
        public void Nary_short_forms_corpus_f1_above_threshold()
        {
            Skip.IfNot(_fix.Available, _fix.SkipReason);
            var examples = _fix.LoadCorpus("extension_v8_nary_short_forms.jsonl");
            Assert.NotEmpty(examples);

            var metrics = EvaluateF1(_fix.Detector, examples);
            _log.WriteLine($"extension_v8 ({examples.Count} ex) :" +
                $" P={metrics.Precision:F3} R={metrics.Recall:F3} F1={metrics.F1:F3}");

            // Seuil : 0.93. Baseline mesurée 2026-06-12 sur distilmult-v6
            // (retrain corpus v8) = F1 0.984 (P 0.992 R 0.977) ; le v5 était
            // à 0.865 (iint/iiint inconnus, mots-clés nus muets). Marge ~5 %
            // sous la baseline, même doctrine que les autres seuils.
            Assert.True(metrics.F1 >= 0.93,
                $"F1 sur extension_v8 = {metrics.F1:F3} (seuil 0.93, baseline 2026-06-12 v6 ≈ 0.98). " +
                $"Les n-aires formes courtes / iint / mots-clés nus sont mal détectés — retour v5 silencieux ?");
        }

        // ---- F1 calculation helpers ----

        private static F1Metrics EvaluateF1(MathNerDetector detector, IReadOnlyList<NerCorpusExample> examples)
        {
            int tp = 0, fp = 0, fn = 0;
            foreach (var ex in examples)
            {
                IReadOnlyList<DetectedZone> predictedZones;
                try { predictedZones = detector.Detect(ex.Text); }
                catch { predictedZones = new List<DetectedZone>(); }

                var predicted = predictedZones.Select(z => (z.Start, z.End)).ToList();
                var gold = ex.Spans.ToList();

                // Match prédictions vs gold avec recouvrement IoU >= 0.5
                // (plus tolérant que strict, plus strict qu'overlap simple).
                // Évite les FP/FN à 1 char près qui sont du bruit de boundary
                // sur les wordpieces.
                var matchedGold = new bool[gold.Count];
                foreach (var p in predicted)
                {
                    int matched = -1;
                    for (int g = 0; g < gold.Count; g++)
                    {
                        if (matchedGold[g]) continue;
                        if (Iou(p, gold[g]) >= 0.5)
                        {
                            matched = g;
                            break;
                        }
                    }
                    if (matched >= 0)
                    {
                        tp++;
                        matchedGold[matched] = true;
                    }
                    else
                    {
                        fp++;
                    }
                }
                for (int g = 0; g < gold.Count; g++)
                    if (!matchedGold[g]) fn++;
            }
            return F1Metrics.Compute(tp, fp, fn);
        }

        private static double Iou((int Start, int End) a, (int Start, int End) b)
        {
            int interStart = a.Start > b.Start ? a.Start : b.Start;
            int interEnd = a.End < b.End ? a.End : b.End;
            int inter = interEnd > interStart ? (interEnd - interStart) : 0;
            int union = (a.End - a.Start) + (b.End - b.Start) - inter;
            if (union <= 0) return 0;
            return (double)inter / union;
        }

        public readonly struct F1Metrics
        {
            public int TruePositives { get; }
            public int FalsePositives { get; }
            public int FalseNegatives { get; }
            public double Precision { get; }
            public double Recall { get; }
            public double F1 { get; }

            private F1Metrics(int tp, int fp, int fn, double p, double r, double f1)
            {
                TruePositives = tp; FalsePositives = fp; FalseNegatives = fn;
                Precision = p; Recall = r; F1 = f1;
            }

            public static F1Metrics Compute(int tp, int fp, int fn)
            {
                double p = (tp + fp) > 0 ? (double)tp / (tp + fp) : 0;
                double r = (tp + fn) > 0 ? (double)tp / (tp + fn) : 0;
                double f1 = (p + r) > 0 ? 2 * p * r / (p + r) : 0;
                return new F1Metrics(tp, fp, fn, p, r, f1);
            }
        }
    }
}
