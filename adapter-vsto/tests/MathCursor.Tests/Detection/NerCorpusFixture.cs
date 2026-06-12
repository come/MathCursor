using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MathCursor.Detection;

namespace MathCursor.Tests.Detection
{
    /// <summary>
    /// Charge le modèle NER une seule fois pour tous les tests d'inférence
    /// (xunit IClassFixture). Si le modèle ou le corpus sont absents, le
    /// fixture expose <see cref="Available"/> = false et les tests sont skip.
    /// Le modèle est local (~129 Mo), pas commit dans le repo, donc tous les
    /// dev n'en ont pas forcément une copie sous main.
    /// </summary>
    public sealed class NerCorpusFixture : IDisposable
    {
        public bool Available { get; }
        public string SkipReason { get; }
        public MathNerDetector Detector { get; }
        public string CorpusDir { get; }

        public NerCorpusFixture()
        {
            var repoRoot = FindRepoRoot();
            if (repoRoot == null)
            {
                Available = false;
                SkipReason = "Racine du repo introuvable depuis " + AppContext.BaseDirectory;
                return;
            }

            var modelDir = Path.Combine(repoRoot, "models", "distilmult-v6");
            CorpusDir = Path.Combine(repoRoot, "data", "ner-corpus");

            if (!Directory.Exists(modelDir))
            {
                Available = false;
                SkipReason = "Modèle NER introuvable : " + modelDir + " — tests d'inférence skip.";
                return;
            }
            if (!Directory.Exists(CorpusDir))
            {
                Available = false;
                SkipReason = "Corpus NER introuvable : " + CorpusDir;
                return;
            }

            try
            {
                Detector = new MathNerDetector(modelDir);
                Available = true;
            }
            catch (Exception ex)
            {
                Available = false;
                // Inclut le détail de l'inner exception : sur P/Invoke loader
                // failure (OnnxRuntime), le top-level dit juste
                // "TypeInitializationException", l'inner contient le DllNotFound.
                var details = new System.Text.StringBuilder();
                details.Append(ex.GetType().Name).Append(" : ").Append(ex.Message);
                Exception cur = ex.InnerException;
                while (cur != null)
                {
                    details.AppendLine().Append("  → ").Append(cur.GetType().Name)
                           .Append(" : ").Append(cur.Message);
                    cur = cur.InnerException;
                }
                SkipReason = "Échec chargement modèle NER :\n" + details
                    + "\nNuGet root: " + Path.Combine(repoRoot, "(check OnnxRuntime native)");
            }
        }

        public void Dispose()
        {
            Detector?.Dispose();
        }

        // Remonte depuis BaseDirectory jusqu'à trouver MathCursor.sln (la
        // racine du repo). null si pas trouvé. Évite de coder un chemin absolu
        // qui dépendrait de la machine.
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "MathCursor.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        // ---- Helper pour les tests ----

        public IReadOnlyList<NerCorpusExample> LoadCorpus(string fileName, int? limit = null)
        {
            var path = Path.Combine(CorpusDir, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Corpus introuvable : " + path);

            var examples = new List<NerCorpusExample>();
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var ex = NerCorpusExample.Parse(line);
                // Fold accents/smart chars comme en prod (WordContextReader →
                // AutocorrectNormalizer avant le NER) : les tests F1 doivent
                // exercer la même distribution que le runtime. 1:1 → spans valides.
                if (ex != null)
                    examples.Add(new NerCorpusExample(
                        MathCursor.Host.AutocorrectNormalizer.Normalize(ex.Text), ex.Spans, ex.Lang));
                if (limit.HasValue && examples.Count >= limit.Value) break;
            }
            return examples;
        }
    }

    /// <summary>
    /// Une ligne du corpus NER (jsonl) parsée. Format minimal :
    /// <c>{"text": "...", "spans": [{"start":N,"end":N,"label":"MATH"}], "lang": "fr"}</c>.
    /// On ne tire pas une dépendance JSON.NET pour 4 champs — parsing manuel
    /// suffit largement pour cette grammaire restreinte.
    /// </summary>
    public sealed class NerCorpusExample
    {
        public string Text { get; }
        public IReadOnlyList<(int Start, int End)> Spans { get; }
        public string Lang { get; }

        public NerCorpusExample(string text, IReadOnlyList<(int, int)> spans, string lang)
        {
            Text = text;
            Spans = spans;
            Lang = lang;
        }

        public static NerCorpusExample Parse(string line)
        {
            // Le corpus est généré par tools/ner-training/build_*.py — format
            // toujours uniforme : "text" puis "spans" (potentiellement vide)
            // puis "lang". On extrait directement avec des recherches par
            // clé "text":, "spans":, "lang":.
            try
            {
                string text = ExtractStringField(line, "\"text\":");
                string lang = ExtractStringField(line, "\"lang\":") ?? "";
                var spans = ExtractSpans(line);
                if (text == null) return null;
                return new NerCorpusExample(text, spans, lang);
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractStringField(string line, string key)
        {
            int idx = line.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;
            int q1 = line.IndexOf('"', idx + key.Length);
            if (q1 < 0) return null;
            // Suit les escapes JSON : \" → ", \\ → \, \n → newline, etc.
            var sb = new StringBuilder();
            int i = q1 + 1;
            while (i < line.Length)
            {
                char c = line[i];
                if (c == '\\' && i + 1 < line.Length)
                {
                    char next = line[i + 1];
                    switch (next)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        default: sb.Append(next); break;
                    }
                    i += 2;
                    continue;
                }
                if (c == '"') return sb.ToString();
                sb.Append(c);
                i++;
            }
            return null;
        }

        private static IReadOnlyList<(int, int)> ExtractSpans(string line)
        {
            var result = new List<(int, int)>();
            int idx = line.IndexOf("\"spans\":", StringComparison.Ordinal);
            if (idx < 0) return result;
            int bracket = line.IndexOf('[', idx);
            if (bracket < 0) return result;
            int closeBracket = line.IndexOf(']', bracket);
            if (closeBracket < 0) return result;
            string spansChunk = line.Substring(bracket, closeBracket - bracket + 1);

            // Parse chaque {"start":N,"end":N,"label":"MATH"} en récupérant
            // start/end. label est ignoré (toujours MATH dans notre corpus).
            int pos = 0;
            while (true)
            {
                int s = spansChunk.IndexOf("\"start\":", pos, StringComparison.Ordinal);
                if (s < 0) break;
                int e = spansChunk.IndexOf("\"end\":", s, StringComparison.Ordinal);
                if (e < 0) break;
                int startVal = ReadInt(spansChunk, s + "\"start\":".Length);
                int endVal = ReadInt(spansChunk, e + "\"end\":".Length);
                result.Add((startVal, endVal));
                pos = e + "\"end\":".Length;
            }
            return result;
        }

        private static int ReadInt(string s, int from)
        {
            // Skip whitespace
            while (from < s.Length && (s[from] == ' ' || s[from] == '\t')) from++;
            int sign = 1;
            if (from < s.Length && s[from] == '-') { sign = -1; from++; }
            int val = 0;
            while (from < s.Length && s[from] >= '0' && s[from] <= '9')
            {
                val = val * 10 + (s[from] - '0');
                from++;
            }
            return sign * val;
        }
    }
}
