using System;
using MathCursor.Core.Resolution;
using MathCursor.HostContract;

namespace MathCursor.Host.Pipeline.Stages
{
    /// <summary>
    /// Stage : persiste la source brute + sidecar JSON dans le
    /// <c>CustomXMLPart</c> du document via <see cref="IEquationStore"/>.
    /// En mode édition : update entrée existante. En nouveau commit : crée
    /// le handle, le bookmark, mémorise le sidecar, et store.
    /// <para>
    /// Phase 4b (ADR 06-05 L4) : logique extraite du SuggestionService.
    /// Le stage prend <see cref="IEquationStore"/> + <see cref="EquationHandleRegistry"/>
    /// au constructeur — toutes les ops touchent Word indirectement via le
    /// registry (qui délègue les bookmarks à des Func injectés).
    /// </para>
    /// </summary>
    internal sealed class StoreStage : ICommitStage
    {
        private readonly IEquationStore _store;
        private readonly EquationHandleRegistry _registry;
        private readonly Action<string> _log;

        public StoreStage(
            IEquationStore store,
            EquationHandleRegistry registry,
            Action<string> log = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _log = log ?? (_ => { });
        }

        public string Name => "Store";

        public CommitContext Apply(CommitContext ctx)
        {
            if (ctx == null) return null;
            var editing = ctx.EditingHandle;

            if (editing != null)
            {
                _log($"edit commit handle={editing.Id} latex=\"{ctx.Latex}\"");
                _registry.Stash(editing.Id);
                var json = SidecarSerializer.Serialize(_registry.GetSidecar(editing.Id));
                try
                {
                    _store.StoreAsync(editing, ctx.Source, new EquationMetadata
                    {
                        SourceLanguage = "fr",
                        CreatedAt = DateTimeOffset.UtcNow,
                        SidecarJson = string.IsNullOrEmpty(json) ? null : json,
                    }).GetAwaiter().GetResult();
                }
                catch (Exception ex) { _log("edit_store_save_error: " + ex.Message); }
                return ctx;
            }

            var handle = new EquationHandle(_registry.NewHandleId());
            _registry.CreateBookmark(handle.Id, ctx.AbsStart, ctx.AbsEnd);
            _registry.Stash(handle.Id, ctx.Sidecar);
            var sidecarJson = SidecarSerializer.Serialize(_registry.GetSidecar(handle.Id));
            try
            {
                _store.StoreAsync(handle, ctx.Source, new EquationMetadata
                {
                    SourceLanguage = "fr",
                    CreatedAt = DateTimeOffset.UtcNow,
                    SidecarJson = string.IsNullOrEmpty(sidecarJson) ? null : sidecarJson,
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex) { _log("store_save_error: " + ex.Message); }
            _log($"insert commit handle={handle.Id} range=[{ctx.AbsStart},{ctx.AbsEnd}] latex=\"{ctx.Latex}\" source=\"{ctx.Source}\" sidecarBytes={(sidecarJson?.Length ?? 0)}");
            return ctx.WithNewHandle(handle);
        }
    }
}
