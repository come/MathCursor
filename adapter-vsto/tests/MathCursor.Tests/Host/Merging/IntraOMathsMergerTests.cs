using MathCursor.Host.Merging;
using Xunit;

namespace MathCursor.Tests.Host.Merging
{
    /// <summary>
    /// Tests purs de la logique non-Word du merger intra-¶ revival.
    /// La détection neighbor et le hash drift nécessitent du Word (cf.
    /// tests d'intégration WordIntegrationFixture).
    /// </summary>
    public sealed class IntraOMathsMergerTests
    {
        // ── IsMergeMarker : declenchement merge gauche ─────────────────

        [Theory(DisplayName = "Markers de continuation → merge")]
        [InlineData("=1")]
        [InlineData("= a + b")]
        [InlineData("=>1")]
        [InlineData("<=>x")]
        [InlineData("{a, b}")]
        public void Source_with_marker_triggers_merge(string source)
        {
            Assert.True(IntraOMathsMerger.IsMergeMarker(source));
        }

        [Theory(DisplayName = "Sources sans marker → pas de merge (deux OMaths voulues)")]
        [InlineData("f(x)")]
        [InlineData("g(y)")]
        [InlineData("a + b")]
        [InlineData("sqrt(2)")]
        [InlineData(" =1")]   // espace avant '=' : pas reconnu (l'utilisateur a tapé un espace, intention de séparation)
        [InlineData("x = y")] // '=' au milieu : pas un marqueur de continuation
        public void Source_without_marker_skips_merge(string source)
        {
            Assert.False(IntraOMathsMerger.IsMergeMarker(source));
        }

        [Theory(DisplayName = "Cas dégénérés → pas de merge")]
        [InlineData(null)]
        [InlineData("")]
        public void Empty_or_null_source_skips_merge(string source)
        {
            Assert.False(IntraOMathsMerger.IsMergeMarker(source));
        }

        // ── Précédence des markers (= vs <=> vs =>) ────────────────────

        [Fact(DisplayName = "<=>x reconnu (pas confondu avec '<')")]
        public void IffMarker_recognized()
        {
            Assert.True(IntraOMathsMerger.IsMergeMarker("<=>x"));
        }

        [Fact(DisplayName = "=>y reconnu (pas confondu avec '=')")]
        public void ImpliesMarker_recognized()
        {
            Assert.True(IntraOMathsMerger.IsMergeMarker("=>y"));
        }
    }
}
