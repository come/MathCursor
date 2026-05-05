using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests pour <see cref="ListModeStateMachine"/> — mode liste invisible
    /// activé après un cross-merge multi-ligne réussi (cf. ADR
    /// 2026-05-05-Feat-multiline-list-mode).
    /// <para>
    /// Logique pure, pas de dépendance Word/VSTO. Couvre les 10 cas listés
    /// dans le brief (état initial, activation par marker, exit par ligne
    /// vide/whitespace, validate-as-is sur marker explicite, switch caret,
    /// null safety).
    /// </para>
    /// </summary>
    public sealed class ListModeStateMachineTests
    {
        // ─────────────────────────────────────────────────────────────────
        //  1. État initial : aucune action en cours → Passthrough
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "État initial : OnEnterPressed retourne Passthrough")]
        public void InitialState_AnyInput_ReturnsPassthrough()
        {
            var sm = new ListModeStateMachine();

            Assert.Equal(EnterAction.Passthrough, sm.OnEnterPressed("X=1"));
            Assert.Equal(EnterAction.Passthrough, sm.OnEnterPressed(""));
            Assert.Null(sm.ActiveMarker);
        }

        // ─────────────────────────────────────────────────────────────────
        //  2. Activation après cross-merge : ligne avec contenu sans marker
        //     → préfixage silencieux par marker actif
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Active(<=>) + ligne contenu sans marker → PrefixWithActiveMarker")]
        public void Active_LineWithContentNoMarker_ReturnsPrefixWithActiveMarker()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("<=>");

            Assert.Equal(EnterAction.PrefixWithActiveMarker, sm.OnEnterPressed("X=1"));
            Assert.Equal("<=>", sm.ActiveMarker);
        }

        // ─────────────────────────────────────────────────────────────────
        //  3. Active + ligne vide → ExitListMode
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Active + ligne vide → ExitListMode")]
        public void Active_EmptyLine_ReturnsExitListMode()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("<=>");

            Assert.Equal(EnterAction.ExitListMode, sm.OnEnterPressed(""));
        }

        // ─────────────────────────────────────────────────────────────────
        //  4. Active + ligne whitespace-only → ExitListMode
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Active + ligne whitespace-only → ExitListMode")]
        public void Active_WhitespaceOnly_ReturnsExitListMode()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("<=>");

            Assert.Equal(EnterAction.ExitListMode, sm.OnEnterPressed("   "));
            Assert.Equal(EnterAction.ExitListMode, sm.OnEnterPressed("\t  "));
        }

        // ─────────────────────────────────────────────────────────────────
        //  5. Active(<=>) + ligne commence par "<=>" explicite → ValidateAsIs
        //     (pas de double-préfixe)
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Active(<=>) + ligne commence par <=> → ValidateAsIs")]
        public void Active_LineStartsWithSameMarker_ReturnsValidateAsIs()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("<=>");

            Assert.Equal(EnterAction.ValidateAsIs, sm.OnEnterPressed("<=> X=1"));
        }

        // ─────────────────────────────────────────────────────────────────
        //  6. Active(<=>) + ligne commence par marker DIFFÉRENT (=>) →
        //     ValidateAsIs (on respecte le marker tapé par l'user)
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Active(<=>) + ligne commence par => (autre marker) → ValidateAsIs")]
        public void Active_LineStartsWithDifferentMarker_ReturnsValidateAsIs()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("<=>");

            Assert.Equal(EnterAction.ValidateAsIs, sm.OnEnterPressed("=> X=1"));
        }

        // ─────────────────────────────────────────────────────────────────
        //  7. OnSelectionMoved → désactive le mode (caret hors zone)
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Active → OnSelectionMoved → état Inactive")]
        public void Active_OnSelectionMoved_BecomesInactive()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("<=>");
            Assert.Equal("<=>", sm.ActiveMarker);

            sm.OnSelectionMoved();

            Assert.Null(sm.ActiveMarker);
            Assert.Equal(EnterAction.Passthrough, sm.OnEnterPressed("X=1"));
        }

        // ─────────────────────────────────────────────────────────────────
        //  8. Marker = (chaîne d'égalités) : ligne "4" → préfixée en "= 4"
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Active(=) + ligne \"4\" → PrefixWithActiveMarker")]
        public void ActiveEquals_PlainNumber_ReturnsPrefixWithActiveMarker()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("=");

            Assert.Equal(EnterAction.PrefixWithActiveMarker, sm.OnEnterPressed("4"));
        }

        // ─────────────────────────────────────────────────────────────────
        //  9. Marker Unicode : ligne "⇔ X=1" → ValidateAsIs (variante reconnue)
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Active(<=>) + ligne ⇔ X=1 (Unicode) → ValidateAsIs")]
        public void Active_UnicodeMarkerOnLine_ReturnsValidateAsIs()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("<=>");

            Assert.Equal(EnterAction.ValidateAsIs, sm.OnEnterPressed("⇔ X=1"));
        }

        // ─────────────────────────────────────────────────────────────────
        //  10. Null safety : OnEnterPressed(null) ne crash pas, retourne
        //      ExitListMode (= équivalent ligne vide)
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Active + OnEnterPressed(null) → ExitListMode (null safety)")]
        public void Active_NullInput_ReturnsExitListMode()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("<=>");

            Assert.Equal(EnterAction.ExitListMode, sm.OnEnterPressed(null));
        }

        // ─────────────────────────────────────────────────────────────────
        //  Bonus : Reset() désactive aussi le mode
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Active → Reset() → état Inactive")]
        public void Active_Reset_BecomesInactive()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("=>");
            Assert.Equal("=>", sm.ActiveMarker);

            sm.Reset();

            Assert.Null(sm.ActiveMarker);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Bonus : OnCrossMergeSucceeded(null) ou vide → no-op (ne casse pas)
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "OnCrossMergeSucceeded(null/vide) → reste Inactive")]
        public void OnCrossMergeSucceeded_NullOrEmpty_RemainsInactive()
        {
            var sm = new ListModeStateMachine();

            sm.OnCrossMergeSucceeded(null);
            Assert.Null(sm.ActiveMarker);

            sm.OnCrossMergeSucceeded("");
            Assert.Null(sm.ActiveMarker);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Bonus : marker `=` solo NE doit PAS matcher si suivi d'un autre `=`
        //  (ex. ligne "== X=1" : les `==` ne sont pas un marker align connu,
        //  donc on doit préfixer normalement, pas valider-as-is)
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "StartsWithKnownMarker : `=` solo ne matche pas si suivi de `=`")]
        public void StartsWithKnownMarker_EqualsFollowedByEquals_ReturnsFalse()
        {
            // "==" ne fait pas partie des markers connus → false
            Assert.False(ListModeStateMachine.StartsWithKnownMarker("== X=1"));

            // "= " (suivi d'espace) matche bien
            Assert.True(ListModeStateMachine.StartsWithKnownMarker("= X=1"));
        }

        // ─────────────────────────────────────────────────────────────────
        //  Bonus : marker multi-char (<==>) : matché en priorité avant <=>
        //  (tri par longueur décroissante dans KnownMarkers)
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "StartsWithKnownMarker : <==> matche (multi-char)")]
        public void StartsWithKnownMarker_MultiCharMarker_ReturnsTrue()
        {
            Assert.True(ListModeStateMachine.StartsWithKnownMarker("<==> X=1"));
            Assert.True(ListModeStateMachine.StartsWithKnownMarker("==> X=1"));
            Assert.True(ListModeStateMachine.StartsWithKnownMarker("<== X=1"));
        }

        // ─────────────────────────────────────────────────────────────────
        //  Mode visible (ADR retracted→visible) : ligne contenant UNIQUEMENT
        //  le marker actif (rien tapé après) → ExitListMode. C'est la sortie
        //  propre quand l'user veut quitter le list-mode après l'auto-injection.
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Active(<=>) + ligne \"<=>\" seul → ExitListMode")]
        public void Active_LineOnlyContainsActiveMarker_ReturnsExitListMode()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("<=>");

            Assert.Equal(EnterAction.ExitListMode, sm.OnEnterPressed("<=>"));
            Assert.Equal(EnterAction.ExitListMode, sm.OnEnterPressed("<=> "));
            Assert.Equal(EnterAction.ExitListMode, sm.OnEnterPressed("  <=>  "));
        }

        [Fact(DisplayName = "Active(=) + ligne \"=\" seul → ExitListMode")]
        public void Active_EqualsOnly_ReturnsExitListMode()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("=");

            Assert.Equal(EnterAction.ExitListMode, sm.OnEnterPressed("="));
            Assert.Equal(EnterAction.ExitListMode, sm.OnEnterPressed("=  "));
        }

        [Fact(DisplayName = "Active(<=>) + ligne \"<=> X=1\" → ValidateAsIs (contenu réel après marker)")]
        public void Active_MarkerWithContent_ReturnsValidateAsIs()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("<=>");

            Assert.Equal(EnterAction.ValidateAsIs, sm.OnEnterPressed("<=> X=1"));
        }

        // ─────────────────────────────────────────────────────────────────
        //  Phase 2 cases : marker `{` avec règle STRICTE « { + espace ».
        //  Sinon `{1,2}` (set en extension) ou `{x=1` (sans espace) seraient
        //  faux-positivés — l'user qui Backspace le `{ ` injecté pour taper
        //  un set ne doit pas voir ValidateAsIs déclencher conversion.
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Active({) + ligne \"{ x=1\" → ValidateAsIs (système valide)")]
        public void ActiveCases_LineWithSpaceAndContent_ReturnsValidateAsIs()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("{");

            Assert.Equal(EnterAction.ValidateAsIs, sm.OnEnterPressed("{ x=1"));
        }

        [Fact(DisplayName = "Active({) + ligne \"{ \" seul → ExitListMode (sortie cohérente avec align)")]
        public void ActiveCases_MarkerOnly_ReturnsExitListMode()
        {
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("{");

            // "{ " trimmed = "{", == ActiveMarker.Trim() → ExitListMode
            Assert.Equal(EnterAction.ExitListMode, sm.OnEnterPressed("{ "));
            Assert.Equal(EnterAction.ExitListMode, sm.OnEnterPressed("{"));
        }

        [Fact(DisplayName = "Active({) + ligne \"{1,2}\" (set sans espace) → PrefixWithActiveMarker (pas de match)")]
        public void ActiveCases_SetWithoutSpace_ReturnsPrefixWithActiveMarker()
        {
            // C'est le cas du Backspace partiel : l'user a effacé l'espace du
            // `{ ` injecté et tape un set en extension. Le `{` solo NE DOIT
            // PAS matcher comme marker (sinon on tenterait un ValidateAsIs
            // sur un set, faux comportement).
            // Au lieu, on tombe dans PrefixWithActiveMarker qui sera traité
            // comme exit silencieux côté SuggestionService.
            var sm = new ListModeStateMachine();
            sm.OnCrossMergeSucceeded("{");

            Assert.Equal(EnterAction.PrefixWithActiveMarker, sm.OnEnterPressed("{1,2}"));
            Assert.Equal(EnterAction.PrefixWithActiveMarker, sm.OnEnterPressed("{x=1"));
        }

        [Fact(DisplayName = "StartsWithKnownMarker : `{` ne matche que si suivi d'un espace")]
        public void StartsWithKnownMarker_BraceRequiresSpace()
        {
            // Suivi d'espace : match
            Assert.True(ListModeStateMachine.StartsWithKnownMarker("{ x=1"));
            Assert.True(ListModeStateMachine.StartsWithKnownMarker("{ "));

            // Sans espace : pas de match
            Assert.False(ListModeStateMachine.StartsWithKnownMarker("{1,2}"));
            Assert.False(ListModeStateMachine.StartsWithKnownMarker("{x=1"));
            Assert.False(ListModeStateMachine.StartsWithKnownMarker("{}"));
        }
    }
}
