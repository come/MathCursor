using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests Phase 4c — <see cref="ListModeController"/>.
    /// Cf. ADR <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </summary>
    public sealed class ListModeControllerTests
    {
        [Fact(DisplayName = "Initial state : pas de marker, pas d'ancre")]
        public void Initial_state_inactive()
        {
            var c = new ListModeController();
            Assert.Null(c.ActiveMarker);
            Assert.Equal(-1, c.AnchorParaStart);
        }

        [Fact(DisplayName = "OnCrossMergeSucceeded active la state machine, ancre reste -1 jusqu'à SetAnchor")]
        public void Cross_merge_activates_marker_but_not_anchor_yet()
        {
            var c = new ListModeController();
            c.OnCrossMergeSucceeded("=");
            Assert.Equal("=", c.ActiveMarker);
            Assert.Equal(-1, c.AnchorParaStart); // SetAnchor pas encore appelé
        }

        [Fact(DisplayName = "SetAnchor mémorise la position du paragraphe d'ancrage")]
        public void SetAnchor_memorizes_paragraph()
        {
            var c = new ListModeController();
            c.OnCrossMergeSucceeded("=");
            c.SetAnchor(42);
            Assert.Equal(42, c.AnchorParaStart);
        }

        [Fact(DisplayName = "ClearAnchor reset l'ancre mais pas la state machine")]
        public void ClearAnchor_only_resets_anchor()
        {
            var c = new ListModeController();
            c.OnCrossMergeSucceeded("<=>");
            c.SetAnchor(10);
            c.ClearAnchor();
            Assert.Equal("<=>", c.ActiveMarker); // marker actif
            Assert.Equal(-1, c.AnchorParaStart); // ancre clearé
        }

        [Fact(DisplayName = "Reset reset state machine ET ancre")]
        public void Reset_clears_both()
        {
            var c = new ListModeController();
            c.OnCrossMergeSucceeded("=>");
            c.SetAnchor(20);
            c.Reset();
            Assert.Null(c.ActiveMarker);
            Assert.Equal(-1, c.AnchorParaStart);
        }

        [Fact(DisplayName = "OnSelectionMoved reset state machine ET ancre (caret hors anchor)")]
        public void SelectionMoved_clears_both()
        {
            var c = new ListModeController();
            c.OnCrossMergeSucceeded("=");
            c.SetAnchor(15);
            c.OnSelectionMoved();
            Assert.Null(c.ActiveMarker);
            Assert.Equal(-1, c.AnchorParaStart);
        }

        [Fact(DisplayName = "ShouldInvalidate : false si list-mode inactif")]
        public void ShouldInvalidate_false_if_no_active_marker()
        {
            var c = new ListModeController();
            // Pas de OnCrossMergeSucceeded
            Assert.False(c.ShouldInvalidate(0));
            Assert.False(c.ShouldInvalidate(100));
        }

        [Fact(DisplayName = "ShouldInvalidate : false si ancre pas encore set")]
        public void ShouldInvalidate_false_if_no_anchor()
        {
            var c = new ListModeController();
            c.OnCrossMergeSucceeded("=");
            // Pas de SetAnchor
            Assert.False(c.ShouldInvalidate(50));
        }

        [Fact(DisplayName = "ShouldInvalidate : false si caret toujours sur l'ancre")]
        public void ShouldInvalidate_false_if_caret_on_anchor()
        {
            var c = new ListModeController();
            c.OnCrossMergeSucceeded("=");
            c.SetAnchor(42);
            Assert.False(c.ShouldInvalidate(42));
        }

        [Fact(DisplayName = "ShouldInvalidate : true si caret sorti du ¶ ancré")]
        public void ShouldInvalidate_true_when_caret_left_anchor()
        {
            var c = new ListModeController();
            c.OnCrossMergeSucceeded("=");
            c.SetAnchor(42);
            Assert.True(c.ShouldInvalidate(50));
            Assert.True(c.ShouldInvalidate(0));
        }
    }
}
