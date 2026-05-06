using System;
using MathCursor.HostContract;

namespace MathCursor.Host.Session
{
    /// <summary>
    /// Encapsule l'état mutable du cycle de vie « user a une popup ouverte
    /// sur une zone math en cours de résolution ». Cf. ADR
    /// <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// <para>
    /// Remplace les ~10 champs privés mutables dispersés dans
    /// <c>SuggestionService</c> (<c>_lastZoneAbsStart</c>,
    /// <c>_iterativeSpanStart</c>, <c>_revertedMultiLineZoneStart</c>,
    /// <c>_listModeAnchorPara</c>, <c>_editHandle</c>, etc.). Source de
    /// vérité unique avec transitions FSM validées.
    /// </para>
    /// <para>
    /// Les transitions invalides depuis l'état courant lèvent
    /// <see cref="InvalidOperationException"/> — vs. l'ancien modèle où
    /// on appelait des resets épars en espérant n'avoir rien oublié
    /// (cause racine bug 06-05).
    /// </para>
    /// </summary>
    internal sealed class EquationSession
    {
        public SessionState State { get; private set; } = SessionState.Idle;

        public int ZoneAbsStart { get; private set; } = -1;
        public int ZoneAbsEnd { get; private set; } = -1;
        public string Source { get; private set; } = string.Empty;

        public IterativeExpansion Expansion { get; private set; } = IterativeExpansion.None;

        public RevertedMultiLineZone RevertedZone { get; private set; }
        public ListModeAnchor ListMode { get; private set; }
        public EquationHandle EditingHandle { get; private set; }

        // ─── Transitions ────────────────────────────────────────────

        /// <summary>Valide depuis Idle ou Open (re-frappe sur nouvelle zone).</summary>
        public void OpenOnZone(int absStart, int absEnd, string source)
        {
            EnsureState("OpenOnZone", SessionState.Idle, SessionState.Open);
            ZoneAbsStart = absStart;
            ZoneAbsEnd = absEnd;
            Source = source ?? string.Empty;
            State = SessionState.Open;
        }

        /// <summary>Valide depuis Idle uniquement (clic sur OMath existant).</summary>
        public void EnterEditing(EquationHandle handle, int omathStart, int omathEnd, string source)
        {
            EnsureState("EnterEditing", SessionState.Idle);
            EditingHandle = handle ?? throw new ArgumentNullException(nameof(handle));
            ZoneAbsStart = omathStart;
            ZoneAbsEnd = omathEnd;
            Source = source ?? string.Empty;
            State = SessionState.Editing;
        }

        /// <summary>Valide depuis Open ou Editing.</summary>
        public void StartCommitting()
        {
            EnsureState("StartCommitting", SessionState.Open, SessionState.Editing);
            State = SessionState.Committing;
        }

        /// <summary>Succès commit : revient à Idle, reset de tout l'état.</summary>
        public void Close()
        {
            EnsureState("Close", SessionState.Committing);
            ResetInternal();
        }

        /// <summary>Esc, sortie zone, échec insert : reset depuis n'importe
        /// quel état (idempotent — appelable même depuis Idle).</summary>
        public void Reset()
        {
            ResetInternal();
        }

        // ─── Setters d'état contextuel (autorisés sans transition) ──

        /// <summary>Met à jour l'expansion itérative (Ctrl+Espace répété).
        /// Pas une transition d'état FSM — juste un raffinement de la zone
        /// courante en mode Open.</summary>
        public void SetExpansion(IterativeExpansion expansion)
        {
            Expansion = expansion ?? IterativeExpansion.None;
        }

        /// <summary>Mémorise une zone reverted multi-ligne (utilisée par
        /// <see cref="MathCursor.Host.Merging.RevertedMultiLineMerger"/>
        /// au prochain commit). Effacée au reset.</summary>
        public void SetRevertedZone(RevertedMultiLineZone zone)
        {
            RevertedZone = zone;
        }

        /// <summary>Mémorise l'ancre list-mode après un cross-merge réussi.
        /// Effacée quand le caret quitte le paragraphe d'ancre.</summary>
        public void SetListMode(ListModeAnchor anchor)
        {
            ListMode = anchor;
        }

        public void ClearListMode()
        {
            ListMode = null;
        }

        public void ClearRevertedZone()
        {
            RevertedZone = null;
        }

        // ─── Internals ──────────────────────────────────────────────

        private void ResetInternal()
        {
            State = SessionState.Idle;
            ZoneAbsStart = -1;
            ZoneAbsEnd = -1;
            Source = string.Empty;
            Expansion = IterativeExpansion.None;
            RevertedZone = null;
            ListMode = null;
            EditingHandle = null;
        }

        private void EnsureState(string operation, params SessionState[] allowed)
        {
            for (int i = 0; i < allowed.Length; i++)
            {
                if (State == allowed[i]) return;
            }
            throw new InvalidOperationException(
                $"EquationSession.{operation}() invalide depuis l'état {State}. " +
                $"Attendu : {string.Join(" ou ", allowed)}.");
        }
    }
}
