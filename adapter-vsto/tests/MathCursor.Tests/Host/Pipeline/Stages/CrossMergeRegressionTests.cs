using System.Collections.Generic;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Resolution;
using MathCursor.Host.Merging;
using MathCursor.Host.Pipeline;
using MathCursor.Host.Pipeline.Stages;
using Xunit;

namespace MathCursor.Tests.Host.Pipeline.Stages
{
    /// <summary>
    /// Régression Phase 3b — canary 3 (cross-merge align* texte-only) :
    /// si MergerStage absorbe une chaîne de paragraphes texte (sans OMath
    /// top), <c>ctx.Source</c> devient la mergedSource avec <c>\n</c> mais
    /// <c>ctx.Sidecar</c> reste Empty (pas de handles absorbés à fusionner).
    /// <para>
    /// ResolverStage skip si Sidecar.IsEmpty → ctx.Latex reste le LaTeX
    /// popup (= juste la dernière ligne) au lieu d'être re-résolu sur la
    /// mergedSource (= align* multi-ligne). Bug user : « la ligne 1 est
    /// bouffée par le merge ».
    /// </para>
    /// </summary>
    public sealed class CrossMergeRegressionTests
    {
        [Fact(DisplayName = "REGRESSION 3b : cross-merge texte-only → ResolverStage skip → ligne 1 perdue")]
        public void ResolverStage_skips_on_empty_sidecar_loses_cross_merge_latex()
        {
            // Reproduit le scenario user canary 3 :
            //  ¶1: "AB+BC=CD" (texte plain, marker align)
            //  ¶2: "= CH+HD" (commit courant)
            // → MergerStage absorbe ¶1, mergedSource = "AB+BC=CD\n= CH+HD",
            //   mais Sidecar = Empty (pas d'OMath top dans ¶1, juste du texte).
            // ResolverStage doit Resolve la mergedSource pour produire un
            // align* multi-ligne. Sans ça, ctx.Latex reste celui du commit
            // courant (= CH+HD seul) → la ligne 1 est perdue à InsertOMathAt.

            var resolverStage = new ResolverStage(new ZoneResolver(new LatticeEngine()));

            // Ctx initial : la popup a calculé un latex pour "= CH+HD" seul
            var initial = new CommitContext(
                absStart: 100, absEnd: 110,
                source: "= CH+HD",
                latex: "=CH+HD"); // latex popup = pas align*

            // Simule MergerStage qui a absorbé ¶1 (texte-only, pas de handle) :
            var afterMerge = initial.WithMergeResult(
                absStart: 50, absEnd: 110,
                mergedSource: "AB+BC=CD\n= CH+HD", // \n présent → cross-merge
                mergedSidecar: ResolutionSidecar.Empty, // ← pas de handle absorbé
                removedHandles: new string[0],
                wasCrossParagraphMerge: true,
                crossMergeMarker: "=");

            // ResolverStage devrait Resolve la mergedSource sur le Latex.
            var afterResolve = resolverStage.Apply(afterMerge);

            // Comportement attendu : Latex contient le rendu align* multi-ligne
            Assert.Contains("align*", afterResolve.Latex);
            // Comportement attendu : les tokens des 2 lignes présents (le
            // pipeline align* sépare par & donc on check tokens individuels).
            Assert.Contains("AB", afterResolve.Latex);
            Assert.Contains("BC", afterResolve.Latex);
            Assert.Contains("CD", afterResolve.Latex);
            Assert.Contains("CH", afterResolve.Latex);
            Assert.Contains("HD", afterResolve.Latex);
        }

        [Fact(DisplayName = "Pas de régression hors merge : sans WasCrossParagraphMerge et sidecar Empty → skip OK")]
        public void ResolverStage_skips_safely_when_no_merge_no_sidecar()
        {
            // Cas légitime du skip : nouveau commit sans merge, sidecar empty.
            // Le ResolverStage doit préserver le Latex popup tel quel.
            var resolverStage = new ResolverStage(new ZoneResolver(new LatticeEngine()));

            var ctx = new CommitContext(
                absStart: 0, absEnd: 5,
                source: "AB+BC",
                latex: "popup-already-substituted-latex",
                sidecar: ResolutionSidecar.Empty,
                wasCrossParagraphMerge: false);

            var result = resolverStage.Apply(ctx);

            // Latex popup préservé
            Assert.Equal("popup-already-substituted-latex", result.Latex);
        }
    }
}
