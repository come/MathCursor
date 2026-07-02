// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MathCursor.Analyzers;

/// <summary>
/// MC0006 — Splice de LaTeX rendu (anti-pattern racine).
///
/// <para>Le splice <c>topLatex = topLatex.Substring(0, x) + altLatex +
/// topLatex.Substring(y)</c> opère sur le texte DÉJÀ traité (topLatex)
/// au lieu de muter la source. Cette pratique est le pattern racine du
/// bug double-wrap résolu le 11-05 (commit <c>9ab248b</c>) : splicer
/// <c>\left(AB\right)</c> dans un top qui contient déjà <c>\left(AB\right)</c>
/// produit <c>\left(\left(AB\right)\right)</c>.</para>
///
/// <para>Bonne pratique : muter la source brute (<c>SourceMutation</c>) et
/// re-lancer le pipeline (cf. <c>ZoneResolver.ApplyPreferences</c>).</para>
///
/// <para>Détection : <c>InvocationExpressionSyntax</c> sur <c>.Substring(...)</c>
/// dont le receveur a un nom suggérant du LaTeX rendu (<c>topLatex</c>,
/// <c>*top*</c>, <c>*latex*</c>, <c>*rendered*</c>) ET dont le résultat est
/// concaténé (<c>+</c>) à au moins une autre expression (= pattern splice
/// à 3 morceaux).</para>
///
/// <para>Sévérité par défaut : <c>Info</c>. Promotion en <c>Warning</c> après
/// nettoyage / ADRs ciblées sur les sites légitimes.</para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MC0006_LatexSpliceAntiPattern : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MC0006";

    private static readonly LocalizableString Title =
        "Splice LaTeX sur texte rendu (anti-pattern)";

    private static readonly LocalizableString MessageFormat =
        "Splice de '{0}.Substring(...)' concaténé : préférer une mutation source + re-pipeline";

    private static readonly LocalizableString Description =
        "Splicer du LaTeX dans le texte déjà rendu (topLatex.Substring + altLatex + topLatex.Substring) " +
        "est l'anti-pattern racine du bug double-wrap (parens, widehat, vec). " +
        "Préférer la mutation source via SourceMutation + relance du pipeline. " +
        "Cf. bug 11-05 (commit 9ab248b).";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: Title,
        messageFormat: MessageFormat,
        category: "MathCursor.Architecture",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/mathcursor/docs/blob/main/docs/dev/architecture/mc-rules.md#mc0006");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Cible : appel `.Substring(...)` sur un receveur.
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        if (memberAccess.Name.Identifier.ValueText != "Substring") return;

        // Receveur typé string ? (on s'appuie sur le symbol model — si la
        // résolution échoue, on tombe sur la heuristique nom).
        var typeInfo = context.SemanticModel.GetTypeInfo(memberAccess.Expression);
        if (typeInfo.Type?.SpecialType != SpecialType.System_String) return;

        // Nom du receveur suggère LaTeX rendu ?
        string? receiverName = ExtractReceiverName(memberAccess.Expression);
        if (string.IsNullOrEmpty(receiverName)) return;
        if (!LooksLikeRenderedLatex(receiverName!)) return;

        // Le résultat est-il concaténé à au moins une autre expression via `+` ?
        // (= pattern splice triple, pas un Substring isolé qui peut être légitime)
        if (!IsConcatenated(invocation)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), receiverName));
    }

    private static string? ExtractReceiverName(ExpressionSyntax expr)
    {
        return expr switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            // foo.bar.Substring(...) → on prend le membre direct
            MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
            _ => null,
        };
    }

    private static bool LooksLikeRenderedLatex(string name)
    {
        return ContainsCi(name, "topLatex")
            || ContainsCi(name, "rendered")
            || (ContainsCi(name, "top") && ContainsCi(name, "latex"))
            || (name.Length >= 4 && ContainsCi(name, "latex"));
    }

    /// <summary>
    /// Vrai si l'invocation est utilisée comme opérande d'un <c>+</c>
    /// (= concaténation string). On remonte les parents pour gérer les
    /// chaînes associatives <c>a + b + c</c>.
    /// </summary>
    private static bool IsConcatenated(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is BinaryExpressionSyntax bin
                && bin.OperatorToken.IsKind(SyntaxKind.PlusToken))
                return true;
            // Skip parens / casts neutres
            if (parent is ParenthesizedExpressionSyntax || parent is CastExpressionSyntax)
            {
                parent = parent.Parent;
                continue;
            }
            return false;
        }
        return false;
    }

    private static bool ContainsCi(string haystack, string needle)
        => haystack.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
}
