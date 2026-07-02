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

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using Verifier = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    MathCursor.Analyzers.MC0006_LatexSpliceAntiPattern>;

namespace MathCursor.Analyzers.Tests;

/// <summary>
/// Tests pour MC0006 : splice de LaTeX rendu (anti-pattern racine).
/// </summary>
public sealed class MC0006_LatexSpliceAntiPatternTests
{
    // ─────────────────────────────────────────────────────────────────────
    //  Cas positifs (déclenche)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Triggers_on_topLatex_triple_substring_splice()
    {
        // Exact pattern racine du bug 11-05 (ZoneResolver.cs:205).
        const string source = @"
namespace Foo
{
    public class Bar
    {
        public string Baz(string topLatex, int start, int end, string altLatex)
        {
            return [|topLatex.Substring(0, start)|] + altLatex + [|topLatex.Substring(end)|];
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Triggers_on_renderedLatex_substring_concat()
    {
        const string source = @"
namespace Foo
{
    public class Bar
    {
        public string Baz(string renderedLatex, int x)
        {
            return ""prefix"" + [|renderedLatex.Substring(0, x)|];
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Triggers_on_topRenderedLatex_substring()
    {
        const string source = @"
namespace Foo
{
    public class Bar
    {
        public string Baz(string topRenderedLatex, int x)
        {
            return [|topRenderedLatex.Substring(x)|] + ""suffix"";
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Cas négatifs (silencieux)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Does_not_trigger_when_substring_on_unrelated_variable()
    {
        const string source = @"
namespace Foo
{
    public class Bar
    {
        public string Baz(string userInput, int start, int end, string replacement)
        {
            return userInput.Substring(0, start) + replacement + userInput.Substring(end);
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Does_not_trigger_when_substring_not_concatenated()
    {
        // .Substring isolé sur topLatex (lecture sans splice) : pas un anti-pattern.
        const string source = @"
namespace Foo
{
    public class Bar
    {
        public string Baz(string topLatex)
        {
            return topLatex.Substring(0, 10);
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Does_not_trigger_on_non_string_substring()
    {
        // Si quelqu'un a une méthode `Substring` sur un type custom nommé
        // `topLatex`, on ne déclenche pas (check SpecialType.System_String).
        const string source = @"
namespace Foo
{
    public class Wrapper
    {
        public string Substring(int x) => """";
    }

    public class Bar
    {
        public string Baz(Wrapper topLatex)
        {
            return topLatex.Substring(0) + ""x"";
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Does_not_trigger_on_simple_string_split_or_other_method()
    {
        const string source = @"
namespace Foo
{
    public class Bar
    {
        public string[] Baz(string topLatex)
        {
            return topLatex.Split(' ');
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }
}
