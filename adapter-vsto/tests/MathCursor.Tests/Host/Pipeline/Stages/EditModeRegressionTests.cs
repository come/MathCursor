using System.Collections.Generic;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Resolution;
using MathCursor.Host.Merging;
using MathCursor.Host.Pipeline;
using MathCursor.Host.Pipeline.Stages;
using MathCursor.HostContract;
using Xunit;

namespace MathCursor.Tests.Host.Pipeline.Stages
{
    /// <summary>
    /// Régression Phase 3b — canary 4 (edit mode + revert préserve désambig).
    /// Bug user : « si je fais edition puis revenir [sur OMath multi-ligne
    /// avec vec], il repasse en mode standard [vec perdus] ».
    /// </summary>
    public sealed class EditModeRegressionTests
    {
        [Fact(DisplayName = "REGRESSION 3b : edit + revert d'un OMath vec → ResolverStage doit re-Resolve avec sidecar mémorisé")]
        public void EditMode_with_stored_sidecar_should_apply_pins_to_latex()
        {
            // Scénario user :
            //  1. OMath existe : `AB+BC=CD\n=EF+GH` avec vec sur les paires.
            //     Le sidecar mémorisé contient les pins vec.
            //  2. User clic « Revenir à la saisie » → revert. Source = texte
            //     plain. Popup réouverte sans nouveaux choix.
            //  3. User valide → CommitLatexAndOMathCore avec _editHandle set.
            //
            // Ce que la popup a comme latex : juste la source brute (= sans vec).
            // Ce que ctx initial doit avoir : sidecar = stored pins (vec).
            // Ce que ResolverStage doit produire : Latex avec vec appliqués.
            //
            // En Phase 3b actuelle, ctx initial Sidecar=Empty, MergerStage skip
            // (edit), ResolverStage skip (!WasMerged && Sidecar.IsEmpty) →
            // Latex reste celui de la popup = pas de vec.
            //
            // Le test simule l'état attendu du ctx initial APRÈS pre-load du
            // stored sidecar (qui devrait être fait par CommitLatexAndOMathCore).

            var resolverStage = new ResolverStage(new ZoneResolver(new LatticeEngine()));

            // Ctx initial avec stored sidecar pre-loadé (= cas FIX attendu)
            var sidecar = new ResolutionSidecar(
                new[]
                {
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 0),  // AB
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 3, 2, 0),  // BC
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 6, 2, 0),  // CD
                },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());
            var ctx = new CommitContext(
                absStart: 0, absEnd: 8,
                source: "AB+BC=CD",
                latex: "AB+BC=CD", // popup latex = source brute (revert)
                sidecar: sidecar,  // ← stored sidecar pre-loadé
                editingHandle: new EquationHandle("eq-edit"));

            var result = resolverStage.Apply(ctx);

            // Latex doit contenir les vec (re-résolu avec sidecar)
            Assert.Contains("\\vec{AB}", result.Latex);
            Assert.Contains("\\vec{BC}", result.Latex);
            Assert.Contains("\\vec{CD}", result.Latex);
        }

        [Fact(DisplayName = "REGRESSION canary 4 : edit mode multi-ligne avec vec → revert + re-commit → vec préservés (pipeline complet)")]
        public void EditMode_multiline_with_vec_revert_then_recommit_preserves_vec_via_full_pipeline()
        {
            // Reproduit le pipeline complet vu par CommitLatexAndOMathCore en
            // edit mode :
            //  1. ctx initial avec sidecar pre-loadé (= stored sidecar de
            //     l'OMath en édition) + EditingHandle non-null + popup latex
            //     = source brute (cas après revert).
            //  2. MergerStage skip (EditingHandle != null) → ne touche pas ctx.
            //  3. ResolverStage Resolve si Sidecar non-empty (= pre-loadé).
            //  4. Latex final contient les vec.
            //
            // Avant le fix `2f7a62c`, le ctx initial avait Sidecar=Empty (pas
            // de pre-load), MergerStage skip, ResolverStage skip (!WasMerged
            // && Empty), Latex restait = popup = source brute → vec sautent.

            var resolver = new ZoneResolver(new LatticeEngine());
            var pipeline = new CommitPipeline(new ICommitStage[]
            {
                new MergerStage(new MergerPipeline(new IZoneMerger[0]), _ => null),
                new ResolverStage(resolver),
            });

            // Stored sidecar de l'OMath en édition (pins vec sur AB/BC/CD/EF/GH).
            // mergedSource = "AB+BC=CD\n=EF+GH" (multi-ligne align*).
            var storedSidecar = new ResolutionSidecar(
                new[]
                {
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 0),  // AB
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 3, 2, 0),  // BC
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 6, 2, 0),  // CD
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 10, 2, 0), // EF
                    new SpanPin(AlternativeGenerator.RuleTwoUppercase, 13, 2, 0), // GH
                },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            // Ctx initial reproduit ce que CommitLatexAndOMathCore construit
            // en edit mode après le fix `2f7a62c` :
            var ctx = new CommitContext(
                absStart: 0, absEnd: 16,
                source: "AB+BC=CD\n=EF+GH",
                latex: "AB+BC=CD\n=EF+GH", // popup latex = source post-revert
                sidecar: storedSidecar,    // ← pre-loadé via fix
                editingHandle: new EquationHandle("eq-multiline"));

            var result = pipeline.Run(ctx);

            // Le pipeline doit produire un LaTeX align* avec tous les vec
            Assert.Contains("align*", result.Latex);
            Assert.Contains("\\vec{AB}", result.Latex);
            Assert.Contains("\\vec{BC}", result.Latex);
            Assert.Contains("\\vec{CD}", result.Latex);
            Assert.Contains("\\vec{EF}", result.Latex);
            Assert.Contains("\\vec{GH}", result.Latex);
        }
    }
}
