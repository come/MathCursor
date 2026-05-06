using System;
using MathCursor.Host.Session;
using MathCursor.HostContract;
using Xunit;

namespace MathCursor.Tests.Host.Session
{
    /// <summary>
    /// Tests FSM de <see cref="EquationSession"/>. Cf. ADR
    /// <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </summary>
    public sealed class EquationSessionTests
    {
        // ─── État initial ───────────────────────────────────────────

        [Fact(DisplayName = "Session initiale = Idle, tous les états contextuels vides")]
        public void Initial_state_is_idle_with_empty_context()
        {
            var s = new EquationSession();

            Assert.Equal(SessionState.Idle, s.State);
            Assert.Equal(-1, s.ZoneAbsStart);
            Assert.Equal(-1, s.ZoneAbsEnd);
            Assert.Equal(string.Empty, s.Source);
            Assert.False(s.Expansion.IsActive);
            Assert.Null(s.RevertedZone);
            Assert.Null(s.ListMode);
            Assert.Null(s.EditingHandle);
        }

        // ─── Transitions valides ────────────────────────────────────

        [Fact(DisplayName = "Idle → OpenOnZone → Open avec zone renseignée")]
        public void OpenOnZone_from_idle_transitions_to_open()
        {
            var s = new EquationSession();
            s.OpenOnZone(absStart: 5, absEnd: 12, source: "AB+BC");

            Assert.Equal(SessionState.Open, s.State);
            Assert.Equal(5, s.ZoneAbsStart);
            Assert.Equal(12, s.ZoneAbsEnd);
            Assert.Equal("AB+BC", s.Source);
        }

        [Fact(DisplayName = "Open → OpenOnZone → Open (re-frappe nouvelle zone)")]
        public void Reopen_on_new_zone_from_open_is_allowed()
        {
            var s = new EquationSession();
            s.OpenOnZone(0, 5, "first");
            s.OpenOnZone(10, 20, "second");

            Assert.Equal(SessionState.Open, s.State);
            Assert.Equal(10, s.ZoneAbsStart);
            Assert.Equal("second", s.Source);
        }

        [Fact(DisplayName = "Idle → EnterEditing → Editing avec handle mémorisé")]
        public void EnterEditing_from_idle_transitions_to_editing()
        {
            var s = new EquationSession();
            var handle = new EquationHandle("eq-42");
            s.EnterEditing(handle, omathStart: 100, omathEnd: 110, source: "f(x)");

            Assert.Equal(SessionState.Editing, s.State);
            Assert.Same(handle, s.EditingHandle);
            Assert.Equal(100, s.ZoneAbsStart);
            Assert.Equal(110, s.ZoneAbsEnd);
        }

        [Fact(DisplayName = "Open → StartCommitting → Committing")]
        public void StartCommitting_from_open()
        {
            var s = new EquationSession();
            s.OpenOnZone(0, 5, "AB");
            s.StartCommitting();
            Assert.Equal(SessionState.Committing, s.State);
        }

        [Fact(DisplayName = "Editing → StartCommitting → Committing")]
        public void StartCommitting_from_editing()
        {
            var s = new EquationSession();
            s.EnterEditing(new EquationHandle("h"), 0, 5, "AB");
            s.StartCommitting();
            Assert.Equal(SessionState.Committing, s.State);
        }

        [Fact(DisplayName = "Committing → Close → Idle (reset complet)")]
        public void Close_from_committing_resets_to_idle()
        {
            var s = new EquationSession();
            s.OpenOnZone(5, 10, "AB");
            s.SetListMode(new ListModeAnchor("=", 5));
            s.StartCommitting();
            s.Close();

            Assert.Equal(SessionState.Idle, s.State);
            Assert.Equal(-1, s.ZoneAbsStart);
            Assert.Equal(string.Empty, s.Source);
            Assert.Null(s.ListMode);
        }

        [Fact(DisplayName = "Reset depuis n'importe quel état → Idle (idempotent)")]
        public void Reset_is_idempotent_from_any_state()
        {
            var s = new EquationSession();
            s.Reset(); // Idle → Idle, pas d'erreur
            Assert.Equal(SessionState.Idle, s.State);

            s.OpenOnZone(0, 5, "X");
            s.Reset(); // Open → Idle
            Assert.Equal(SessionState.Idle, s.State);

            s.OpenOnZone(0, 5, "X");
            s.StartCommitting();
            s.Reset(); // Committing → Idle (échec insert)
            Assert.Equal(SessionState.Idle, s.State);

            s.EnterEditing(new EquationHandle("h"), 0, 5, "X");
            s.Reset(); // Editing → Idle (revert/abandon)
            Assert.Equal(SessionState.Idle, s.State);
            Assert.Null(s.EditingHandle);
        }

        // ─── Transitions invalides → throw ──────────────────────────

        [Fact(DisplayName = "Editing → OpenOnZone interdit (transition invalide)")]
        public void OpenOnZone_from_editing_throws()
        {
            var s = new EquationSession();
            s.EnterEditing(new EquationHandle("h"), 0, 5, "AB");
            var ex = Assert.Throws<InvalidOperationException>(
                () => s.OpenOnZone(10, 20, "X"));
            Assert.Contains("OpenOnZone", ex.Message);
            Assert.Contains("Editing", ex.Message);
        }

        [Fact(DisplayName = "Committing → OpenOnZone interdit")]
        public void OpenOnZone_from_committing_throws()
        {
            var s = new EquationSession();
            s.OpenOnZone(0, 5, "AB");
            s.StartCommitting();
            Assert.Throws<InvalidOperationException>(
                () => s.OpenOnZone(10, 20, "X"));
        }

        [Fact(DisplayName = "Open → EnterEditing interdit (la session doit être Idle pour entrer en édition)")]
        public void EnterEditing_from_open_throws()
        {
            var s = new EquationSession();
            s.OpenOnZone(0, 5, "AB");
            Assert.Throws<InvalidOperationException>(
                () => s.EnterEditing(new EquationHandle("h"), 10, 15, "X"));
        }

        [Fact(DisplayName = "Idle → StartCommitting interdit (rien à committer)")]
        public void StartCommitting_from_idle_throws()
        {
            var s = new EquationSession();
            Assert.Throws<InvalidOperationException>(() => s.StartCommitting());
        }

        [Fact(DisplayName = "Idle → Close interdit (rien à fermer)")]
        public void Close_from_idle_throws()
        {
            var s = new EquationSession();
            Assert.Throws<InvalidOperationException>(() => s.Close());
        }

        [Fact(DisplayName = "Open → Close interdit (manque StartCommitting)")]
        public void Close_from_open_throws()
        {
            var s = new EquationSession();
            s.OpenOnZone(0, 5, "AB");
            Assert.Throws<InvalidOperationException>(() => s.Close());
        }

        // ─── Setters d'état contextuel ──────────────────────────────

        [Fact(DisplayName = "SetExpansion / SetRevertedZone / SetListMode peuvent être appelés sans transition")]
        public void Context_setters_dont_require_specific_state()
        {
            var s = new EquationSession();

            s.SetExpansion(new IterativeExpansion(5, 12, 1));
            Assert.True(s.Expansion.IsActive);
            Assert.Equal(1, s.Expansion.StopIndex);

            s.SetRevertedZone(new RevertedMultiLineZone(0, 100));
            Assert.NotNull(s.RevertedZone);
            Assert.True(s.RevertedZone.ContainsCommit(50));

            s.SetListMode(new ListModeAnchor("=", 5));
            Assert.NotNull(s.ListMode);
            Assert.Equal("=", s.ListMode.Marker);

            s.ClearListMode();
            Assert.Null(s.ListMode);

            s.ClearRevertedZone();
            Assert.Null(s.RevertedZone);
        }

        // ─── Reset efface l'état contextuel ─────────────────────────

        [Fact(DisplayName = "Reset efface tous les états contextuels (expansion, reverted, list-mode, editing)")]
        public void Reset_clears_all_contextual_state()
        {
            var s = new EquationSession();
            s.OpenOnZone(0, 10, "AB+CD");
            s.SetExpansion(new IterativeExpansion(0, 10, 2));
            s.SetRevertedZone(new RevertedMultiLineZone(0, 50));
            s.SetListMode(new ListModeAnchor("=", 0));
            s.Reset();

            Assert.Equal(SessionState.Idle, s.State);
            Assert.False(s.Expansion.IsActive);
            Assert.Null(s.RevertedZone);
            Assert.Null(s.ListMode);
            Assert.Null(s.EditingHandle);
            Assert.Equal(-1, s.ZoneAbsStart);
        }

        // ─── EnterEditing avec handle null → throw ──────────────────

        [Fact(DisplayName = "EnterEditing(null) → ArgumentNullException")]
        public void EnterEditing_with_null_handle_throws()
        {
            var s = new EquationSession();
            Assert.Throws<ArgumentNullException>(
                () => s.EnterEditing(null, 0, 5, "X"));
        }
    }
}
