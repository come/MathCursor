using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Probe bug 2026-05-23 user-reported : `[0,1[` (intervalle half-open
    /// français, fermé à gauche, ouvert à droite) rend `[0,1[]]` (deux `]`
    /// parasites en fin).
    ///
    /// <para>Cas générique : intervalles avec close-delim non-canonique du
    /// open-delim. Conv FR : `[a,b]` fermé, `]a,b[` ouvert, `[a,b[` /
    /// `]a,b]` half-open.</para>
    /// </summary>
    public class IntervalHalfOpenBugProbeTests
    {
        private readonly ITestOutputHelper _output;

        public IntervalHalfOpenBugProbeTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void HalfOpenRight_closed_left_open_right()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("[0,1[");
            _output.WriteLine($"top='{r.TopLatex}'");
            // Attendu : un `[0,1[` propre (= 1 close `[`, pas `[]]`).
            Assert.Equal("[0,1[", r.TopLatex.Trim());
        }

        // `]a,b]` et `]a,b[` (= leading `]`) : non couverts par le fix
        // 2026-05-23 sur `[0,1[`. Le `]` leading est tokenisé en CloseDelim
        // → skip silencieux par le top-level loop de MathEngine. Fix
        // générique = retokenize contextuel des brackets — chantier séparé.
        [Fact(Skip = "Leading `]` not yet handled — see follow-up issue")]
        public void HalfOpenLeft_open_left_closed_right()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("]0,1]");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Equal("]0,1]", r.TopLatex.Trim());
        }

        [Fact(Skip = "Leading `]` not yet handled — see follow-up issue")]
        public void FullyOpen_french_notation()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("]0,1[");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Equal("]0,1[", r.TopLatex.Trim());
        }

        [Fact]
        public void FullyClosed_standard()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("[0,1]");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Equal("[0,1]", r.TopLatex.Trim());
        }
    }
}
