using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MathCursor.Analyzers;

/// <summary>
/// MC0001 — Regex utilisée sur du contenu structuré (XML/OMath/MathML).
///
/// <para>Détection :</para>
/// <list type="bullet">
/// <item><c>new Regex(...)</c> (ObjectCreationExpressionSyntax)</item>
/// <item><c>Regex.Match/Replace/Split/IsMatch/...</c> (InvocationExpressionSyntax sur le type statique)</item>
/// </list>
///
/// <para>Heuristique de contexte structuré (au moins une des conditions) :</para>
/// <list type="number">
/// <item>Le fichier contient <c>using System.Xml.Linq</c> ou <c>using System.Xml</c></item>
/// <item>Le nom de fichier matche <c>*OMath*</c>, <c>*Serializer*</c>, <c>*Parser*</c>, <c>*Renderer*</c>, <c>*Splicer*</c></item>
/// <item>L'argument string contient un littéral <c>&lt;</c> ou <c>xmlns</c></item>
/// </list>
///
/// <para>Sévérité par défaut : <c>Info</c>. Promotion en <c>Warning</c> puis
/// <c>Error</c> via <c>.editorconfig</c> au fil du nettoyage. Suppression
/// admise uniquement avec ADR explicite.</para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MC0001_RegexOnStructured : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MC0001";

    private static readonly LocalizableString Title =
        "Regex sur contenu structuré (XML/OMath/MathML)";

    private static readonly LocalizableString MessageFormat =
        "Regex sur contenu structuré — utiliser XDocument/XElement ou un parseur dédié";

    private static readonly LocalizableString Description =
        "Regex sur XML/OMath/MathML est fragile (échappement, namespaces, " +
        "fermetures imbriquées). Préférer System.Xml.Linq, un parseur dédié, " +
        "ou un visiteur AST. Suppression admise uniquement avec ADR documenté " +
        "via [SuppressMessage(Justification=\"ADR-...\")].";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: Title,
        messageFormat: MessageFormat,
        category: "MathCursor.Architecture",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/mathcursor/docs/blob/main/docs/dev/architecture/mc-rules.md#mc0001");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var node = (ObjectCreationExpressionSyntax)context.Node;
        var typeSymbol = context.SemanticModel.GetSymbolInfo(node.Type).Symbol;
        if (!IsRegexType(typeSymbol)) return;
        if (LooksLikeStructuredContext(node, context))
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        // Cible : appels statiques sur Regex (Regex.Match, Regex.Replace, etc.).
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        var receiverSymbol = context.SemanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
        if (!IsRegexType(receiverSymbol)) return;

        if (LooksLikeStructuredContext(invocation, context))
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
    }

    private static bool IsRegexType(ISymbol? symbol)
        => symbol?.ToDisplayString() == "System.Text.RegularExpressions.Regex";

    private static bool LooksLikeStructuredContext(SyntaxNode node, SyntaxNodeAnalysisContext context)
    {
        // 1) Heuristique nom de fichier
        var filePath = node.SyntaxTree.FilePath;
        if (!string.IsNullOrEmpty(filePath))
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (FileNameSuggestsStructured(fileName)) return true;
        }

        // 2) Heuristique using directives
        var root = node.SyntaxTree.GetRoot(context.CancellationToken);
        var usings = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(u => u.Name?.ToString() ?? "");
        if (usings.Any(u => u.StartsWith("System.Xml"))) return true;

        // 3) Heuristique littéral d'argument (contient `<` ou `xmlns`)
        foreach (var literal in node.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (literal.Token.IsKind(SyntaxKind.StringLiteralToken)
                && LiteralLooksLikeXml(literal.Token.ValueText))
                return true;
        }

        return false;
    }

    private static bool FileNameSuggestsStructured(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        return ContainsCi(fileName, "OMath")
            || ContainsCi(fileName, "Serializer")
            || ContainsCi(fileName, "Parser")
            || ContainsCi(fileName, "Renderer")
            || ContainsCi(fileName, "Splicer");
    }

    private static bool LiteralLooksLikeXml(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        // Si la regex elle-même cible du XML, on a typiquement `<` ou `xmlns`
        // dans le pattern (ex: `@"<m:fraction>.*?</m:fraction>"`).
        return s.IndexOf('<') >= 0 || ContainsCi(s, "xmlns");
    }

    private static bool ContainsCi(string haystack, string needle)
        => haystack.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
}
