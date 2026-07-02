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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace MathCursor.Tests.Detection
{
    /// <summary>
    /// Parité de DÉTECTION C# ↔ Rust : le détecteur `MathNerDetector` (Word, WordPiece
    /// + vocab.txt) et le `mc-ner` Rust (VS Code/LibreOffice, tokenizer.json) DOIVENT
    /// renvoyer les mêmes zones brutes sur le même modèle. C'est le filet qui manquait
    /// à la 0.11.3 : le C# divergeait du Rust sur les cas courts (IDs spéciaux hardcodés)
    /// et rien ne le testait. On compare `MathNerDetector.Detect` à la commande binaire
    /// `DETECTRAW` (= `NerDetector::detect`, brut). Skip si modèle ou binaire absent.
    /// </summary>
    public sealed class NerCSharpRustParityTests : IClassFixture<NerCorpusFixture>
    {
        private readonly NerCorpusFixture _fix;
        public NerCSharpRustParityTests(NerCorpusFixture fix) { _fix = fix; }

        // Set couvrant la classe de bug 0.11.3 (courts nus) + matrices + prose + fonctions.
        private static readonly string[] Cases =
        {
            "x2", "U_n", "2x+1", "x^2", "X_2", "U_2",
            "on pose x2", "soit f(x) = 2x + 1",
            "(1 -2; 3 4)", "(a b; c d)",
            "cos(x) + 1", "sqrt(x+1)", "alpha + beta",
            "le chat dort sur le canape", "research in mathematics",
        };

        [SkippableFact]
        public void Csharp_and_rust_detect_same_zones()
        {
            Skip.IfNot(_fix.Available, _fix.SkipReason);
            var root = FindRepoRoot();
            Skip.If(root == null, "racine repo introuvable");
            var modelDir = Path.Combine(root, "models", "latest");
            var bin = new[]
            {
                Path.Combine(root, "rust", "target", "release", "mc-ner.exe"),
                Path.Combine(root, "rust", "target", "debug", "mc-ner.exe"),
            }.FirstOrDefault(File.Exists);
            Skip.If(bin == null, "binaire mc-ner non buildé (cargo build -p mc-ner) — parité non exécutée");

            using var rust = new RustNer(bin, modelDir);
            var mismatches = new StringBuilder();
            foreach (var input in Cases)
            {
                var cs = _fix.Detector.Detect(input)
                    .Select(z => (z.Start, z.End)).OrderBy(z => z.Start).ThenBy(z => z.End).ToList();
                var rs = rust.DetectRaw(input)
                    .OrderBy(z => z.Item1).ThenBy(z => z.Item2).ToList();
                if (!cs.SequenceEqual(rs))
                    mismatches.AppendLine($"  \"{input}\"  C#={Fmt(cs)}  Rust={Fmt(rs)}");
            }
            Assert.True(mismatches.Length == 0, "Divergence détection C# ↔ Rust :\n" + mismatches);
        }

        private static string Fmt(IEnumerable<(int, int)> zs)
        {
            var l = zs.ToList();
            return l.Count == 0 ? "(aucune)" : "[" + string.Join(" ", l.Select(z => $"{z.Item1},{z.Item2}")) + "]";
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "MathCursor.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>Pilote le binaire mc-ner persistant (stdio).</summary>
        private sealed class RustNer : IDisposable
        {
            private readonly Process _p;
            public RustNer(string bin, string modelDir)
            {
                _p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = bin,
                        Arguments = "\"" + modelDir + "\"",
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                    },
                };
                _p.Start();
                // Attendre READY (le chargement ONNX + warm-up peut prendre quelques s).
                string line;
                while ((line = _p.StandardOutput.ReadLine()) != null && line != "READY")
                    if (line == "FATAL") throw new InvalidOperationException("mc-ner FATAL au démarrage");
            }

            public List<(int, int)> DetectRaw(string text)
            {
                _p.StandardInput.Write("DETECTRAW\t" + text + "\n");
                _p.StandardInput.Flush();
                var line = _p.StandardOutput.ReadLine() ?? "NONE";
                var res = new List<(int, int)>();
                if (line.StartsWith("ZONES\t"))
                    foreach (var pair in line.Substring(6).Split(' '))
                    {
                        var c = pair.Split(',');
                        res.Add((int.Parse(c[0]), int.Parse(c[1])));
                    }
                return res;
            }

            public void Dispose()
            {
                try { _p.StandardInput.Write("QUIT\n"); _p.StandardInput.Flush(); } catch { }
                try { if (!_p.WaitForExit(3000)) _p.Kill(); } catch { }
                _p.Dispose();
            }
        }
    }
}
