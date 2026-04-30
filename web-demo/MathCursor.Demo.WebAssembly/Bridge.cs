using System;
using System.Collections.Generic;
using System.Linq;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using Microsoft.JSInterop;

namespace MathCursor.Demo.WebAssembly;

/// <summary>
/// Pont JS↔.NET pour la démo web. Expose le LatticeEngine au JS via
/// JSInvokable.
///
/// <see cref="ConvertRich"/> retourne le top-1 + les alternatives d'ambiguïté
/// (la plus à droite), reconstruites en formule complète. Couvre :
/// <list type="bullet">
/// <item>AB / ABC (segment subst : vec, paren, crochet, widehat, triangle)</item>
/// <item>x2 / x3 (Sup implicite : exposant ↔ indice)</item>
/// <item>V/E isolés (mutation source : forall / exists / racine)</item>
/// <item>R/N/Z/Q/C isolés (mutation : ensemble \mathbb vs lettre)</item>
/// </list>
/// </summary>
public static class Bridge
{
    private static readonly Lazy<LatticeEngine> _engine = new(() => LatticeEngine.LoadEmbedded("fr"));

    /// <summary>DTO sérialisé vers JS via System.Text.Json.</summary>
    public sealed class ConvertResult
    {
        public string Top { get; set; } = "";
        public string[] Alternatives { get; set; } = Array.Empty<string>();
        public string? Rule { get; set; }
    }

    [JSInvokable]
    public static ConvertResult ConvertRich(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new ConvertResult();

        try
        {
            var result = _engine.Value.ConvertWithAmbiguity(input);
            var dto = new ConvertResult { Top = result.TopLatex };

            if (result.Spot == null || !result.SpotStart.HasValue || !result.SpotEnd.HasValue)
                return dto;

            dto.Rule = result.Spot.RuleId;
            int start = result.SpotStart.Value;
            int end = result.SpotEnd.Value;
            string top = result.TopLatex;

            // Les règles AB/ABC/x_n produisent des alternatives au niveau du
            // SEGMENT (pas du LaTeX complet). On reconstruit la formule entière
            // en substituant le segment dans top[start..end].
            // Les règles V/E/canonical-set produisent déjà des LaTeX complets
            // (rendus post-mutation source) — pas de substitution nécessaire.
            bool isSegmentRule =
                result.Spot.RuleId == AlternativeGenerator.RuleTwoUppercase ||
                result.Spot.RuleId == AlternativeGenerator.RuleThreeUppercase ||
                result.Spot.RuleId == AlternativeGenerator.RuleLetterSupNumber;

            var alts = new List<string>();
            foreach (var alt in result.Spot.Alternatives)
            {
                string fullAlt;
                if (isSegmentRule)
                {
                    if (start >= 0 && end <= top.Length && start < end)
                        fullAlt = top.Substring(0, start) + alt.Latex + top.Substring(end);
                    else
                        fullAlt = alt.Latex;
                }
                else
                {
                    fullAlt = alt.Latex; // déjà formule complète post-mutation
                }
                if (!string.IsNullOrEmpty(fullAlt) && fullAlt != top)
                    alts.Add(fullAlt);
            }
            dto.Alternatives = alts.ToArray();
            return dto;
        }
        catch (Exception ex)
        {
            return new ConvertResult { Top = "% erreur moteur : " + ex.Message };
        }
    }

    /// <summary>API legacy (top-K Dijkstra) — gardée pour compat si jamais.</summary>
    [JSInvokable]
    public static string[] Convert(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return Array.Empty<string>();
        try
        {
            var suggestions = _engine.Value.Convert(input);
            return suggestions.Select(s => s.Latex).ToArray();
        }
        catch (Exception ex)
        {
            return new[] { "% erreur moteur : " + ex.Message };
        }
    }
}
