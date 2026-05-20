using System;

namespace MathCursor.Host
{
    /// <summary>
    /// Encapsule le <see cref="LastActionSnapshot"/> singleton + sa logique de
    /// mise à jour (best-effort, swallow exceptions). Utilisé par
    /// <see cref="MathCursor.Host.Pipeline.Stages.SnapshotStage"/> pour
    /// capturer l'état pre-insert pour la fenêtre « Signaler une erreur ».
    /// <para>
    /// Phase 4 ADR <c>2026-05-06-Meta-l4-pipeline-and-session</c> :
    /// extrait du field <c>SuggestionService._lastAction</c> pour découpler
    /// le snapshot stage du god-object.
    /// </para>
    /// </summary>
    internal sealed class LastActionTracker
    {
        private readonly Func<string> _readParagraphContext;
        private LastActionSnapshot _current;

        /// <param name="readParagraphContext">Délégué qui lit le contexte du
        /// paragraphe Word courant (lecture côté Word — pas d'extraction
        /// possible côté pure C#). Passé via injection pour ne pas coupler
        /// le tracker au SuggestionService.</param>
        public LastActionTracker(Func<string> readParagraphContext)
        {
            _readParagraphContext = readParagraphContext ?? (() => string.Empty);
        }

        /// <summary>Snapshot courant (peut être null si aucune action enregistrée).</summary>
        public LastActionSnapshot Current => _current;

        /// <summary>Crée un snapshot initial à l'ouverture de la popup avec
        /// la source tapée + le LaTeX proposé. Remplace toujours l'ancien
        /// (chaque popup show = nouvelle action utilisateur).</summary>
        public void RecordPopupOpen(string sourceText, string proposedLatex)
        {
            try
            {
                _current = new LastActionSnapshot
                {
                    At = DateTime.UtcNow,
                    SourceText = sourceText ?? string.Empty,
                    ProposedLatex = proposedLatex ?? string.Empty,
                    CommittedLatex = null,
                    ParagraphContext = _readParagraphContext(),
                };
            }
            catch { /* best-effort */ }
        }

        /// <summary>Met à jour le snapshot pre-commit avec le LaTeX final.
        /// Si aucun snapshot existe (commit sans popup show préalable, rare),
        /// le crée. Sinon, met à jour CommittedLatex+At.</summary>
        public void Update(string sourceText, string committedLatex)
        {
            try
            {
                if (_current == null)
                {
                    _current = new LastActionSnapshot
                    {
                        At = DateTime.UtcNow,
                        SourceText = sourceText ?? string.Empty,
                        ParagraphContext = _readParagraphContext(),
                    };
                }
                _current.CommittedLatex = committedLatex ?? string.Empty;
                _current.At = DateTime.UtcNow;
            }
            catch { /* best-effort */ }
        }

        /// <summary>Reset le snapshot (= aucune action enregistrée).</summary>
        public void Reset() => _current = null;
    }
}
