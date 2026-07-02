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

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using Verifier = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    MathCursor.Analyzers.MC0009_SuppressMessageWithoutAdr>;

namespace MathCursor.Analyzers.Tests;

/// <summary>
/// Tests pour MC0009 : SuppressMessage doit citer un ADR.
/// </summary>
public sealed class MC0009_SuppressMessageWithoutAdrTests
{
    // ─────────────────────────────────────────────────────────────────────
    //  Cas positifs (déclenche)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Triggers_when_no_justification_at_all()
    {
        const string source = @"
using System.Diagnostics.CodeAnalysis;

namespace Foo
{
    public class Bar
    {
        [[|SuppressMessage(""MathCursor.Architecture"", ""MC0001"")|]]
        public void Baz() { }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Triggers_when_justification_empty()
    {
        const string source = @"
using System.Diagnostics.CodeAnalysis;

namespace Foo
{
    public class Bar
    {
        [[|SuppressMessage(""MathCursor.Architecture"", ""MC0001"", Justification = """")|]]
        public void Baz() { }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Triggers_when_justification_does_not_reference_ADR()
    {
        const string source = @"
using System.Diagnostics.CodeAnalysis;

namespace Foo
{
    public class Bar
    {
        [[|SuppressMessage(""MathCursor.Architecture"", ""MC0001"", Justification = ""code review accepted"")|]]
        public void Baz() { }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Triggers_when_justification_has_random_text()
    {
        const string source = @"
using System.Diagnostics.CodeAnalysis;

namespace Foo
{
    public class Bar
    {
        [[|SuppressMessage(""Style"", ""IDE0060"", Justification = ""legacy API, will remove later"")|]]
        public void Baz() { }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Cas négatifs (silencieux)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Does_not_trigger_when_justification_references_ADR_prefix()
    {
        const string source = @"
using System.Diagnostics.CodeAnalysis;

namespace Foo
{
    public class Bar
    {
        [SuppressMessage(""MathCursor.Architecture"", ""MC0001"", Justification = ""ADR-014: regex légitime ici, parsing OMath échoue sur ce cas spécifique"")]
        public void Baz() { }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Does_not_trigger_when_justification_references_project_ADR_slug()
    {
        const string source = @"
using System.Diagnostics.CodeAnalysis;

namespace Foo
{
    public class Bar
    {
        [SuppressMessage(""MathCursor.Architecture"", ""MC0001"", Justification = ""Cf. 2026-05-13-Fix-ghost-doc-invisible"")]
        public void Baz() { }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Does_not_trigger_when_justification_is_long_with_ADR_inside()
    {
        const string source = @"
using System.Diagnostics.CodeAnalysis;

namespace Foo
{
    public class Bar
    {
        [SuppressMessage(""MathCursor.Architecture"", ""MC0001"", Justification = ""Cette regex est nécessaire pour parser un cas dégénéré du XML OMath. Cf ADR 2026-05-13."")]
        public void Baz() { }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Does_not_trigger_when_attribute_is_not_SuppressMessage()
    {
        // Sanity : un autre attribut sans Justification ne doit pas déclencher.
        const string source = @"
using System;

namespace Foo
{
    [AttributeUsage(AttributeTargets.Method)]
    public class CustomAttribute : Attribute { }

    public class Bar
    {
        [Custom]
        public void Baz() { }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Does_not_trigger_when_justification_is_nameof_expression()
    {
        // Justification dynamique (variable / nameof) : on ne peut pas
        // résoudre statiquement → on suppose conforme (faux négatif assumé).
        const string source = @"
using System.Diagnostics.CodeAnalysis;

namespace Foo
{
    public class Bar
    {
        private const string AdrRef = ""ADR-014"";

        [SuppressMessage(""Cat"", ""ID"", Justification = AdrRef)]
        public void Baz() { }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }
}
