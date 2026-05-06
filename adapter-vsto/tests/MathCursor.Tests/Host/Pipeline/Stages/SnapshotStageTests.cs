using MathCursor.Host;
using MathCursor.Host.Pipeline;
using MathCursor.Host.Pipeline.Stages;
using Xunit;

namespace MathCursor.Tests.Host.Pipeline.Stages
{
    /// <summary>
    /// Tests Phase 4 — SnapshotStage extrait dans sa classe (logique métier
    /// réelle, plus juste un délégant). Cf. ADR
    /// <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </summary>
    public sealed class SnapshotStageTests
    {
        [Fact(DisplayName = "Apply met à jour LastActionTracker avec ctx.Source/Latex")]
        public void Apply_updates_tracker_with_ctx_source_and_latex()
        {
            var tracker = new LastActionTracker(() => "para-context");
            var stage = new SnapshotStage(tracker);
            var ctx = new CommitContext(
                absStart: 0, absEnd: 5,
                source: "AB+BC",
                latex: "\\vec{AB}+\\vec{BC}");

            stage.Apply(ctx);

            Assert.NotNull(tracker.Current);
            Assert.Equal("AB+BC", tracker.Current.SourceText);
            Assert.Equal("\\vec{AB}+\\vec{BC}", tracker.Current.CommittedLatex);
            Assert.Equal("para-context", tracker.Current.ParagraphContext);
        }

        [Fact(DisplayName = "Apply met à jour Latex sur snapshot existant (pas de nouveau)")]
        public void Apply_updates_latex_on_existing_snapshot()
        {
            // Simule l'ouverture popup (ProposedLatex set) puis le snapshot
            // pre-commit (CommittedLatex set par le stage).
            var tracker = new LastActionTracker(() => "para");
            tracker.RecordPopupOpen("AB+BC", "AB+BC"); // snapshot avec ProposedLatex
            var stage = new SnapshotStage(tracker);
            var ctx = new CommitContext(0, 5, "AB+BC", "\\vec{AB}+\\vec{BC}");

            stage.Apply(ctx);

            // ProposedLatex préservé (set par RecordPopupOpen)
            Assert.Equal("AB+BC", tracker.Current.ProposedLatex);
            // CommittedLatex set par Apply
            Assert.Equal("\\vec{AB}+\\vec{BC}", tracker.Current.CommittedLatex);
        }

        [Fact(DisplayName = "Apply(null) → null (pas de side-effect)")]
        public void Apply_null_returns_null()
        {
            var tracker = new LastActionTracker(() => "");
            var stage = new SnapshotStage(tracker);

            var result = stage.Apply(null);

            Assert.Null(result);
            Assert.Null(tracker.Current);
        }

        [Fact(DisplayName = "Apply ne lève jamais (best-effort) même si tracker context throw")]
        public void Apply_swallows_exceptions_from_tracker_context()
        {
            // Tracker avec un readContext qui throw — le snapshot devrait
            // être best-effort (le commit ne doit pas échouer à cause d'un
            // bug de reporting).
            var tracker = new LastActionTracker(() => throw new System.InvalidOperationException("boom"));
            var stage = new SnapshotStage(tracker);
            var ctx = new CommitContext(0, 5, "AB", "AB");

            // Ne doit pas throw
            var result = stage.Apply(ctx);

            Assert.Same(ctx, result);
        }
    }
}
