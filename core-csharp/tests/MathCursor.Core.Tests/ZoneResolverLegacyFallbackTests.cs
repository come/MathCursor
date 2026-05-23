using MathCursor.Core;
using MathCursor.Core.Lattice;
using Xunit;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// P32.1 (2026-05-23) : tests verrouillant la garantie « le legacy
    /// n'est plus appelé en condition normale ». Vérifie que :
    /// <list type="number">
    ///   <item>Quand <see cref="IResolvedZoneSource"/> retourne non-null
    ///     (= Engine v2 a une réponse, même identité), le legacy n'est PAS
    ///     appelé : <c>LegacyFallbackCalls</c> reste à 0.</item>
    ///   <item>Quand l'engine source retourne <c>null</c> (= exception
    ///     fatale), <c>LegacyFallbackCalls</c> s'incrémente.</item>
    ///   <item>Sans engine source branché (= kill-switch),
    ///     <c>LastResolveUsedLegacy</c> est <c>true</c> mais le compteur
    ///     reste à 0 (signal "kill-switch assumé").</item>
    /// </list>
    /// </summary>
    public sealed class ZoneResolverLegacyFallbackTests
    {
        // ---- Fakes ----

        private sealed class AlwaysIdentitySource : IResolvedZoneSource
        {
            public int CallCount { get; private set; }
            public ResolvedZone? TryResolve(string rawSource, out string diagTrace)
            {
                CallCount++;
                diagTrace = "fake: identity\n";
                return new ResolvedZone(
                    rawSource: rawSource ?? string.Empty,
                    mutedSource: rawSource ?? string.Empty,
                    topLatex: rawSource ?? string.Empty,
                    spot: null,
                    spotStart: null,
                    spotEnd: null,
                    allMatches: System.Array.Empty<AmbiguityMatch>(),
                    isIncomplete: false);
            }
        }

        private sealed class AlwaysNullSource : IResolvedZoneSource
        {
            public int CallCount { get; private set; }
            public ResolvedZone? TryResolve(string rawSource, out string diagTrace)
            {
                CallCount++;
                diagTrace = "fake: null (= simulating exception)\n";
                return null;
            }
        }

        private static ZoneResolver MakeResolver(IResolvedZoneSource? src)
            => new ZoneResolver(new LatticeEngine(),
                patternPipeline: null, patternRegistry: null, engineSource: src);

        // ---- Engine v2 répond non-null → legacy non appelé ----

        [Fact]
        public void Engine_v2_returns_nonnull_legacy_not_called()
        {
            var src = new AlwaysIdentitySource();
            var r = MakeResolver(src);

            r.Resolve("a+b");
            r.Resolve("c=d");
            r.Resolve("forall x in R");

            Assert.Equal(3, src.CallCount);
            Assert.Equal(0, r.LegacyFallbackCalls);
            Assert.False(r.LastResolveUsedLegacy);
        }

        // ---- Engine v2 retourne null → compteur incrémente ----

        [Fact]
        public void Engine_v2_returns_null_increments_counter()
        {
            var src = new AlwaysNullSource();
            var r = MakeResolver(src);

            r.Resolve("a+b");

            Assert.Equal(1, src.CallCount);
            Assert.Equal(1, r.LegacyFallbackCalls);
            Assert.True(r.LastResolveUsedLegacy);
        }

        [Fact]
        public void Engine_v2_returns_null_multiple_times_counter_accumulates()
        {
            var src = new AlwaysNullSource();
            var r = MakeResolver(src);

            r.Resolve("a+b");
            r.Resolve("c+d");
            r.Resolve("e+f");

            Assert.Equal(3, r.LegacyFallbackCalls);
        }

        // ---- Pas d'engine source = kill-switch mode ----

        [Fact]
        public void No_engine_source_kill_switch_mode_no_counter_increment()
        {
            var r = MakeResolver(src: null);

            r.Resolve("a+b");
            r.Resolve("c+d");

            // Mode legacy assumé (= kill-switch) : LastResolveUsedLegacy=true
            // mais compteur reste à 0 (= pas un fallback inattendu).
            Assert.True(r.LastResolveUsedLegacy);
            Assert.Equal(0, r.LegacyFallbackCalls);
            Assert.Contains("[LEGACY-ONLY]", r.LastEngineDiagTrace);
        }

        // ---- Mix : alternance v2-ok / v2-null ----

        [Fact]
        public void Mix_engine_v2_alternating_counter_tracks_only_null_returns()
        {
            var src = new MixSource();
            var r = MakeResolver(src);

            r.Resolve("a");  // null
            r.Resolve("b");  // identity
            r.Resolve("c");  // null
            r.Resolve("d");  // identity

            Assert.Equal(2, r.LegacyFallbackCalls);
            // Dernière résolution = identity (= legacy non utilisé)
            Assert.False(r.LastResolveUsedLegacy);
        }

        private sealed class MixSource : IResolvedZoneSource
        {
            private int _count;
            public ResolvedZone? TryResolve(string rawSource, out string diagTrace)
            {
                diagTrace = "fake: mix\n";
                if (_count++ % 2 == 0) return null;
                return new ResolvedZone(
                    rawSource: rawSource ?? string.Empty,
                    mutedSource: rawSource ?? string.Empty,
                    topLatex: rawSource ?? string.Empty,
                    spot: null, spotStart: null, spotEnd: null,
                    allMatches: System.Array.Empty<AmbiguityMatch>(),
                    isIncomplete: false);
            }
        }

        // ---- Trace diag contient le marqueur [LEGACY-FALLBACK] ----

        [Fact]
        public void Engine_v2_null_diag_trace_contains_legacy_fallback_marker()
        {
            var src = new AlwaysNullSource();
            var r = MakeResolver(src);

            r.Resolve("anything");

            Assert.Contains("[LEGACY-FALLBACK]", r.LastEngineDiagTrace);
            Assert.Contains("legacy calls this session: 1", r.LastEngineDiagTrace);
        }
    }
}
