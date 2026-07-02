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

using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MathCursor.Analyzers;

/// <summary>
/// MC0009 — Verrou anti-suppression silencieuse de diagnostic.
///
/// <para>Une suppression de diagnostic via <c>[SuppressMessage(...)]</c>
/// (notamment des règles MC) DOIT citer un ADR du projet via la propriété
/// <c>Justification</c>. Sinon c'est un contournement non documenté qui
/// dérive l'archi en silence.</para>
///
/// <para>Détection :</para>
/// <list type="bullet">
/// <item><c>AttributeSyntax</c> sur <c>SuppressMessageAttribute</c>
///   (<c>System.Diagnostics.CodeAnalysis</c>).</item>
/// <item>Vérifie que la valeur de <c>Justification</c> contient une
///   référence ADR : token <c>ADR</c> (case-insensitive) OU slug de
///   décision projet <c>YYYY-MM-DD-(Meta|Feat|Fix|Refactor|UX|Release|Test|Limit)</c>.</item>
/// </list>
///
/// <para>Cf. brief <c>MATHCURSOR_HARNESS_BRIEF.md</c> : "Une règle additionnelle
/// MC9999 peut auditer toute SuppressMessage sans Justification qui pointe
/// sur un ADR existant — c'est le verrou contre les suppressions silencieuses."</para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MC0009_SuppressMessageWithoutAdr : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MC0009";

    private static readonly LocalizableString Title =
        "SuppressMessage sans référence ADR";

    private static readonly LocalizableString MessageFormat =
        "Suppression de diagnostic sans Justification référençant un ADR projet";

    private static readonly LocalizableString Description =
        "Toute [SuppressMessage(...)] doit citer un ADR du projet dans " +
        "Justification (ex: Justification = \"ADR-014: ...\" ou " +
        "Justification = \"Cf. 2026-05-13-Fix-...\"). Sans ça, la " +
        "suppression devient une dérive silencieuse non tracée.";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: Title,
        messageFormat: MessageFormat,
        category: "MathCursor.Architecture",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/mathcursor/docs/blob/main/docs/dev/architecture/mc-rules.md#mc0009");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Rule);

    // ADR token (case-insensitive) OR YYYY-MM-DD-Kind slug from the project's
    // ADR naming convention (docs/dev/decisions/).
    private static readonly Regex AdrReferenceRegex = new(
        @"\bADR\b|\b\d{4}-\d{2}-\d{2}-(Meta|Feat|Fix|Refactor|UX|Release|Test|Limit)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.Attribute);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var attr = (AttributeSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(attr).Symbol;
        var typeName = symbol?.ContainingType?.ToDisplayString();
        if (typeName != "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute")
            return;

        // Récupère la valeur littérale de Justification (named arg).
        // Positional : SuppressMessage(category, checkId, Scope=..., Target=...,
        //   MessageId=..., Justification=...) — Justification n'est PAS en
        //   positional usuel, donc on ne couvre que le named arg.
        string? justification = null;
        if (attr.ArgumentList != null)
        {
            foreach (var arg in attr.ArgumentList.Arguments)
            {
                if (arg.NameEquals?.Name.Identifier.ValueText != "Justification") continue;
                if (arg.Expression is LiteralExpressionSyntax lit
                    && lit.Token.IsKind(SyntaxKind.StringLiteralToken))
                {
                    justification = lit.Token.ValueText;
                }
                else
                {
                    // Justification = nameof(...) ou variable : on ne peut pas
                    // résoudre statiquement, on suppose conforme (faux négatif
                    // assumé pour pas pénaliser les patterns dynamiques rares).
                    return;
                }
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(justification)
            || !AdrReferenceRegex.IsMatch(justification!))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, attr.GetLocation()));
        }
    }
}
