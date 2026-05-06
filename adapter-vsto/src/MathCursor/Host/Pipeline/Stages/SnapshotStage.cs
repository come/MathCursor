using System;

namespace MathCursor.Host.Pipeline.Stages
{
    /// <summary>
    /// Stage : capture un snapshot du commit (source brute + LaTeX final)
    /// pour pré-remplir la fenêtre « Signaler une erreur ». Placé entre
    /// Resolver et Inserter pour capturer le LaTeX post-merge mais
    /// pre-insert : si l'insertion fail, le report a quand même le bon
    /// LaTeX.
    /// <para>
    /// Phase 4 (ADR 06-05 L4) : logique extraite du SuggestionService.
    /// Le stage prend un <see cref="LastActionTracker"/> au constructeur
    /// (encapsule le snapshot singleton + la lecture paragraphe).
    /// </para>
    /// </summary>
    internal sealed class SnapshotStage : ICommitStage
    {
        private readonly LastActionTracker _tracker;

        public SnapshotStage(LastActionTracker tracker)
        {
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        }

        public string Name => "Snapshot";

        public CommitContext Apply(CommitContext ctx)
        {
            if (ctx == null) return null;
            _tracker.Update(ctx.Source, ctx.Latex);
            return ctx;
        }
    }
}
