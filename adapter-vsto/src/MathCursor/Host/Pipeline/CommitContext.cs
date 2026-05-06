using System.Collections.Generic;
using MathCursor.Core.Resolution;
using MathCursor.HostContract;

namespace MathCursor.Host.Pipeline
{
    /// <summary>
    /// Contexte propagé entre les stages du <c>CommitPipeline</c>. POCO
    /// immutable — chaque stage produit un nouveau context via
    /// <see cref="With"/> méthodes (record-like), ne mute pas le précédent.
    /// Cf. ADR <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// <para>
    /// Cible Phase 1 : juste la signature. Les stages en Phase 2+
    /// utiliseront ces propriétés pour passer leur output au stage suivant.
    /// </para>
    /// </summary>
    /// <remarks>
    /// On n'utilise pas <c>record</c> C# 9 ici parce que le csproj VSTO
    /// principal a <c>LangVersion 9.0</c> mais le projet contient des
    /// fichiers non-record et on garde la cohérence stylistique. Les
    /// propriétés <c>{ get; }</c> + constructeur + méthodes <c>With*</c>
    /// donnent l'équivalent (immutable + value semantics manuelles).
    /// </remarks>
    internal sealed class CommitContext
    {
        // ─── Coordonnées zone ───────────────────────────────────────

        /// <summary>Position absolue de début de la zone à insérer dans
        /// le doc Word. Modifiée par MergerStage si absorption.</summary>
        public int AbsStart { get; }

        /// <summary>Position absolue de fin (exclusive). Modifiée par
        /// MergerStage si absorption.</summary>
        public int AbsEnd { get; }

        // ─── Source + LaTeX ─────────────────────────────────────────

        /// <summary>Source brute (texte tapé) de la zone à insérer.
        /// Mise à jour par MergerStage en source mergée si absorption.</summary>
        public string Source { get; }

        /// <summary>LaTeX rendu par le pipeline, prêt à être inséré.
        /// Mise à jour par ResolverStage (applique sidecar pin/votes).</summary>
        public string Latex { get; }

        // ─── Sidecar ────────────────────────────────────────────────

        /// <summary>Sidecar fusionné des handles absorbés + popup courante.
        /// Construit par MergerStage, consommé par ResolverStage.</summary>
        public ResolutionSidecar Sidecar { get; }

        // ─── Handles affectés ───────────────────────────────────────

        /// <summary>Handles d'OMaths absorbés au merge (à effacer du store
        /// après insertion réussie).</summary>
        public IReadOnlyList<string> RemovedHandles { get; }

        /// <summary>Handle créé au commit (renseigné par InserterStage).
        /// Null tant que l'insertion n'a pas eu lieu.</summary>
        public EquationHandle NewHandle { get; }

        /// <summary>Handle existant en cours d'édition (mode edit).
        /// Si non-null, l'insertion remplace cet OMath au lieu d'en créer
        /// un nouveau.</summary>
        public EquationHandle EditingHandle { get; }

        // ─── Cross-merge metadata ───────────────────────────────────

        /// <summary>Vrai si le merge a produit une mergedSource cross-paragraphe
        /// (présence de \n). Active le list-mode post-insertion.</summary>
        public bool WasCrossParagraphMerge { get; }

        /// <summary>Marker dominant extrait de la mergedSource cross-merge
        /// (= / &lt;=&gt; / =&gt; / &lt;= / { pour cases). Null si pas
        /// cross-merge ou pas de marker reconnu.</summary>
        public string CrossMergeMarker { get; }

        // ─── Abort flag ─────────────────────────────────────────────

        /// <summary>Vrai si un stage a échoué (ex. InserterStage : rollback
        /// requis). Les stages suivants doivent passer le ctx en
        /// pass-through (pas de side-effect). Le pipeline check ce flag
        /// avant chaque <c>Apply</c>.</summary>
        public bool IsAborted { get; }

        // ─── Constructeur + With ────────────────────────────────────

        public CommitContext(
            int absStart,
            int absEnd,
            string source,
            string latex,
            ResolutionSidecar sidecar = null,
            IReadOnlyList<string> removedHandles = null,
            EquationHandle newHandle = null,
            EquationHandle editingHandle = null,
            bool wasCrossParagraphMerge = false,
            string crossMergeMarker = null,
            bool isAborted = false)
        {
            AbsStart = absStart;
            AbsEnd = absEnd;
            Source = source ?? string.Empty;
            Latex = latex ?? string.Empty;
            Sidecar = sidecar ?? ResolutionSidecar.Empty;
            RemovedHandles = removedHandles ?? System.Array.Empty<string>();
            NewHandle = newHandle;
            EditingHandle = editingHandle;
            WasCrossParagraphMerge = wasCrossParagraphMerge;
            CrossMergeMarker = crossMergeMarker;
            IsAborted = isAborted;
        }

        public CommitContext WithMergeResult(
            int absStart, int absEnd, string mergedSource,
            ResolutionSidecar mergedSidecar,
            IReadOnlyList<string> removedHandles,
            bool wasCrossParagraphMerge,
            string crossMergeMarker)
            => new CommitContext(
                absStart, absEnd, mergedSource, Latex,
                mergedSidecar, removedHandles, NewHandle, EditingHandle,
                wasCrossParagraphMerge, crossMergeMarker, IsAborted);

        public CommitContext WithLatex(string latex)
            => new CommitContext(
                AbsStart, AbsEnd, Source, latex, Sidecar, RemovedHandles,
                NewHandle, EditingHandle, WasCrossParagraphMerge, CrossMergeMarker, IsAborted);

        public CommitContext WithNewHandle(EquationHandle handle)
            => new CommitContext(
                AbsStart, AbsEnd, Source, Latex, Sidecar, RemovedHandles,
                handle, EditingHandle, WasCrossParagraphMerge, CrossMergeMarker, IsAborted);

        /// <summary>Marque le ctx comme avorté. Les stages suivants
        /// pass-through (le pipeline les saute via short-circuit).</summary>
        public CommitContext WithAbort()
            => new CommitContext(
                AbsStart, AbsEnd, Source, Latex, Sidecar, RemovedHandles,
                NewHandle, EditingHandle, WasCrossParagraphMerge, CrossMergeMarker, isAborted: true);

        /// <summary>Update les bornes après insertion réussie (l'OMath inséré
        /// peut avoir légèrement changé les bornes par rapport à la zone source).</summary>
        public CommitContext WithBounds(int absStart, int absEnd)
            => new CommitContext(
                absStart, absEnd, Source, Latex, Sidecar, RemovedHandles,
                NewHandle, EditingHandle, WasCrossParagraphMerge, CrossMergeMarker, IsAborted);
    }
}
