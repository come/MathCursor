using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Xunit;
using Verifier = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    MathCursor.Analyzers.MC0001_RegexOnStructured>;

namespace MathCursor.Analyzers.Tests;

/// <summary>
/// Tests pour la règle MC0001 : Regex sur contenu structuré.
/// Couvre les 3 heuristiques (using System.Xml, nom de fichier suspect,
/// littéral avec `&lt;`/`xmlns`) + cas négatifs (regex sur du texte plat).
/// </summary>
public sealed class MC0001_RegexOnStructuredTests
{
    private const string SourcePrefix = @"
using System.Text.RegularExpressions;
";

    // ─────────────────────────────────────────────────────────────────────
    //  Cas positifs (la règle doit déclencher)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Triggers_when_file_imports_System_Xml_Linq()
    {
        const string source = @"
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Foo
{
    public class Bar
    {
        public void Baz(string xml)
        {
            var match = [|new Regex(""pattern"")|];
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Triggers_when_file_imports_System_Xml()
    {
        const string source = @"
using System.Text.RegularExpressions;
using System.Xml;

namespace Foo
{
    public class Bar
    {
        public void Baz()
        {
            var match = [|new Regex(""pattern"")|];
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Triggers_when_string_literal_contains_xml_open_tag()
    {
        const string source = @"
using System.Text.RegularExpressions;

namespace Foo
{
    public class Bar
    {
        public void Baz(string content)
        {
            var match = [|new Regex(@""<m:fraction>.*?</m:fraction>"")|];
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Does_not_trigger_when_xmlns_in_separate_variable()
    {
        // Le littéral `xmlns` est dans une variable, pas dans le sous-arbre
        // du `new Regex(...)`. L'heuristique 3 inspecte uniquement les
        // littéraux descendants du node Regex → pas de fire.
        const string source = @"
using System.Text.RegularExpressions;

namespace Foo
{
    public class Bar
    {
        public void Baz()
        {
            var pattern = ""xmlns:m"";
            var match = new Regex(pattern);
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Triggers_when_xmlns_directly_in_pattern_literal()
    {
        const string source = @"
using System.Text.RegularExpressions;

namespace Foo
{
    public class Bar
    {
        public void Baz()
        {
            var match = [|new Regex(""xmlns:m="")|];
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Triggers_on_filename_OMath_suffix()
    {
        // Source SANS using System.Xml et SANS littéral xml — seul le nom
        // de fichier `OMathSerializer.cs` doit déclencher la règle.
        const string source = @"
using System.Text.RegularExpressions;

namespace Foo
{
    public class Bar
    {
        public void Baz(string content)
        {
            var match = [|new Regex(""pattern"")|];
        }
    }
}";
        var test = new CSharpAnalyzerTest<MC0001_RegexOnStructured, XUnitVerifier>
        {
            TestState =
            {
                Sources = { ("OMathSerializer.cs", source) },
            },
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task Triggers_on_Regex_Match_static_call()
    {
        const string source = @"
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Foo
{
    public class Bar
    {
        public void Baz(string xml)
        {
            var m = [|Regex.Match(xml, ""pattern"")|];
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Triggers_on_Regex_Replace_static_call()
    {
        const string source = @"
using System.Text.RegularExpressions;
using System.Xml;

namespace Foo
{
    public class Bar
    {
        public void Baz(string xml)
        {
            var r = [|Regex.Replace(xml, ""<a>"", """")|];
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Cas négatifs (la règle ne doit PAS déclencher)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Does_not_trigger_on_plain_text_regex()
    {
        const string source = @"
using System.Text.RegularExpressions;

namespace Foo
{
    public class Bar
    {
        public void Baz(string text)
        {
            var match = new Regex(@""\d{3}-\d{4}"");
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Does_not_trigger_on_email_pattern()
    {
        const string source = @"
using System.Text.RegularExpressions;

namespace Foo
{
    public class Bar
    {
        public void Baz(string email)
        {
            var match = new Regex(@""[a-z]+@[a-z]+\.[a-z]+"");
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Does_not_trigger_on_System_Xml_usage_without_Regex()
    {
        // Sanity : la simple présence de System.Xml ne doit pas déclencher
        // sans appel Regex.
        const string source = @"
using System.Xml.Linq;

namespace Foo
{
    public class Bar
    {
        public void Baz(string xml)
        {
            var doc = XDocument.Parse(xml);
        }
    }
}";
        await Verifier.VerifyAnalyzerAsync(source);
    }
}
