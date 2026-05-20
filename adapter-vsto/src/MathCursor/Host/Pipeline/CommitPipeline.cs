using System;
using System.Collections.Generic;

namespace MathCursor.Host.Pipeline
{
    /// <summary>
    /// Orchestre la chaîne des <see cref="ICommitStage"/> du commit.
    /// Itère les stages dans l'ordre et propage le <see cref="CommitContext"/>
    /// de stage en stage. Cf. ADR <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// <para>
    /// Cible (à terme) : <c>merger → resolver → renderer → inserter →
    /// store → layout → caret</c>. En Phase 2, seuls les 2 premiers
    /// (Merger et Resolver) sont des stages réels — les autres viendront
    /// progressivement en Phases 3+.
    /// </para>
    /// <para>
    /// Bénéfice : le flow d'exécution du commit se lit en N lignes (la
    /// liste des stages), au lieu d'une méthode de 250 LoC qui imbrique
    /// tout. Ajouter une étape = ajouter un stage dans la liste.
    /// </para>
    /// </summary>
    internal sealed class CommitPipeline
    {
        private readonly IReadOnlyList<ICommitStage> _stages;
        private readonly Action<string> _log;

        public CommitPipeline(IReadOnlyList<ICommitStage> stages, Action<string> log = null)
        {
            _stages = stages ?? throw new ArgumentNullException(nameof(stages));
            _log = log ?? (_ => { });
        }

        /// <summary>
        /// Applique tous les stages dans l'ordre, en propageant le ctx.
        /// Si un stage throw, le pipeline propage l'exception (les stages
        /// gèrent leurs propres cas dégradés via retour identité).
        /// </summary>
        public CommitContext Run(CommitContext initial)
        {
            if (initial == null) throw new ArgumentNullException(nameof(initial));

            var ctx = initial;
            for (int i = 0; i < _stages.Count; i++)
            {
                var stage = _stages[i];
                if (ctx.IsAborted)
                {
                    _log($"stage[{i}]={stage.Name}: SKIPPED (aborted)");
                    continue;
                }
                var before = ctx;
                ctx = stage.Apply(ctx) ?? before;
                _log($"stage[{i}]={stage.Name}: " +
                     $"absStart={ctx.AbsStart} absEnd={ctx.AbsEnd} " +
                     $"removed={ctx.RemovedHandles.Count} " +
                     $"sidecarPins={ctx.Sidecar.SpanPins.Count}" +
                     (ctx.IsAborted ? " [ABORTED]" : ""));
            }
            return ctx;
        }
    }
}
