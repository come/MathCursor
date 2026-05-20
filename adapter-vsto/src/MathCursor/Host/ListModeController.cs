namespace MathCursor.Host
{
    /// <summary>
    /// Encapsule l'état list-mode : la state machine
    /// (<see cref="ListModeStateMachine"/>) qui gère les transitions Enter/
    /// SelectionMoved + l'ancre paragraphe (<c>AnchorParaStart</c>) qui
    /// invalide le list-mode dès que le caret quitte le ¶ d'ancrage.
    /// <para>
    /// Phase 4c (ADR 06-05 L4) : extrait des fields dispersés
    /// <c>_listMode</c> + <c>_listModeAnchorParaStart</c> de
    /// <c>SuggestionService</c>. La logique des transitions reste dans
    /// <see cref="ListModeStateMachine"/> ; ce contrôleur ajoute juste
    /// l'ancre + les invariants couplés (clear anchor au Reset/SelectionMoved).
    /// </para>
    /// </summary>
    internal sealed class ListModeController
    {
        private readonly ListModeStateMachine _stateMachine = new ListModeStateMachine();
        private int _anchorParaStart = -1;

        /// <summary>Marker actuellement actif (= / &lt;=&gt; / { ...) ou
        /// null si list-mode désactivé.</summary>
        public string ActiveMarker => _stateMachine.ActiveMarker;

        /// <summary>Position absolue du paragraphe d'ancrage (= début du
        /// ¶ qui contient l'OMath multi-ligne créé par le cross-merge),
        /// ou -1 si aucune ancre active.</summary>
        public int AnchorParaStart => _anchorParaStart;

        /// <summary>Délègue à la state machine. Cf. ADR 05-05
        /// list-mode-visible pour la sémantique de l'action retournée.</summary>
        public EnterAction OnEnterPressed(string line)
            => _stateMachine.OnEnterPressed(line);

        /// <summary>Caret a bougé hors de l'ancre — désactive le list-mode.</summary>
        public void OnSelectionMoved()
        {
            _stateMachine.OnSelectionMoved();
            _anchorParaStart = -1;
        }

        /// <summary>Active le list-mode après un cross-merge réussi (ou
        /// cases single-line). Le caller passera ensuite l'<c>anchorParaStart</c>
        /// via <see cref="SetAnchor"/> une fois calculé via Word.</summary>
        public void OnCrossMergeSucceeded(string marker)
            => _stateMachine.OnCrossMergeSucceeded(marker);

        /// <summary>Mémorise la position de l'ancre paragraphe (= début ¶
        /// où l'OMath multi-ligne a été inséré). Lu par
        /// <see cref="ShouldInvalidate"/> au mouvement de caret.</summary>
        public void SetAnchor(int paragraphStart)
        {
            _anchorParaStart = paragraphStart;
        }

        /// <summary>Efface l'ancre (mais pas la state machine — l'inverse
        /// de <see cref="OnSelectionMoved"/> qui reset les deux).</summary>
        public void ClearAnchor()
        {
            _anchorParaStart = -1;
        }

        /// <summary>Reset complet : state machine + ancre. Appelé au commit
        /// non-cross-merge ou quand l'utilisateur quitte le contexte.</summary>
        public void Reset()
        {
            _stateMachine.Reset();
            _anchorParaStart = -1;
        }

        /// <summary>Vrai si le list-mode est actif ET le caret est sorti
        /// du paragraphe d'ancrage. Le caller doit alors appeler
        /// <see cref="OnSelectionMoved"/>.</summary>
        public bool ShouldInvalidate(int currentParaStart)
        {
            if (_stateMachine.ActiveMarker == null) return false;
            if (_anchorParaStart < 0) return false;
            return currentParaStart != _anchorParaStart;
        }
    }
}
