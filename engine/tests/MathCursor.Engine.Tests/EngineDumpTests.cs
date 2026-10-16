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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace MathCursor.Engine.Tests;

// Sortie brute du moteur C# sur un corpus, pour tools/engine-diff (parité C#/Rust
// au-delà des fixtures). Inactif sans MC_ENGINE_DUMP_IN : ne pèse pas sur le gate.
// Une ligne par entrée : décision \t note(0/1) \t latex␂coût␁latex␂coût…
public sealed class EngineDumpTests
{
    [Fact]
    public void DumpCorpus()
    {
        string? inPath = Environment.GetEnvironmentVariable("MC_ENGINE_DUMP_IN");
        if (inPath == null) return;
        var modes = (Environment.GetEnvironmentVariable("MC_ENGINE_DUMP_MODES") ?? "fr").Split(',');
        var inputs = File.ReadAllLines(inPath, Encoding.UTF8);
        foreach (var mode in modes)
        {
            bool physChem = mode.EndsWith("+physchem", StringComparison.Ordinal);
            var culture = (mode.StartsWith("us", StringComparison.Ordinal) ? EngineCulture.Us : EngineCulture.Fr).WithPhysChem(physChem);
            var sb = new StringBuilder();
            foreach (var s in inputs)
            {
                try
                {
                    var r = ForestEngine.Analyze(s, culture);
                    sb.Append(r.Decision).Append('\t').Append(r.HasNote ? '1' : '0').Append('\t')
                      .Append(string.Join("\u0001", r.Ranked.Select(c => c.Latex + "\u0002" + c.Cost.ToString("R", CultureInfo.InvariantCulture))));
                }
                catch { sb.Append("erreur\t0\t"); }
                sb.Append('\n');
            }
            File.WriteAllText(inPath + "." + mode.Replace('+', '_') + ".cs.out", sb.ToString(), new UTF8Encoding(false));
        }
    }
}
