using System.Collections.Generic;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Resolution;
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
    }
}
