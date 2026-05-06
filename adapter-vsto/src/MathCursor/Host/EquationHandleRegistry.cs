using System;
using System.Collections.Generic;
using MathCursor.Core.Resolution;

namespace MathCursor.Host
{
    /// <summary>
    /// Registre central des handles d'équation : encapsule le mapping
    /// handleId → sidecar mémoire, la génération d'IDs, et les opérations
    /// bookmark Word (déléguées via injection pour rester testable hors Word).
    /// <para>
    /// Phase 4 ADR <c>2026-05-06-Meta-l4-pipeline-and-session</c> : extrait
    /// du field <c>SuggestionService._sidecarsByHandle</c> + méthodes
    /// associées (<c>StashSidecarForHandle</c>, <c>GetSidecarForHandle</c>,
    /// <c>NewHandleId</c>, <c>CreateBookmarkForRange</c>,
    /// <c>DeleteBookmarkByHandle</c>) pour découpler <see cref="MathCursor.Host.Pipeline.Stages.StoreStage"/>
    /// du god-object.
    /// </para>
    /// </summary>
    internal sealed class EquationHandleRegistry
    {
        private readonly Dictionary<string, ResolutionSidecar> _sidecarsByHandle
            = new Dictionary<string, ResolutionSidecar>();
        private readonly Action<string, int, int> _createBookmark;
        private readonly Action<string> _deleteBookmark;
        private readonly Func<ResolutionSidecar> _popupSidecar;

        /// <param name="createBookmark">Délégué Word : <c>(handleId, absStart, absEnd) → void</c>.
        /// Crée un bookmark <c>mcEq_&lt;handleId&gt;</c> sur le range donné. Best-effort.</param>
        /// <param name="deleteBookmark">Délégué Word : <c>(handleId) → void</c>. Supprime
        /// le bookmark si existant. Best-effort.</param>
        /// <param name="popupSidecar">Délégué : <c>() → popup.CurrentSidecar</c>. Lu
        /// par <see cref="Stash"/> en fallback quand l'override est Empty.</param>
        public EquationHandleRegistry(
            Action<string, int, int> createBookmark,
            Action<string> deleteBookmark,
            Func<ResolutionSidecar> popupSidecar)
        {
            _createBookmark = createBookmark ?? throw new ArgumentNullException(nameof(createBookmark));
            _deleteBookmark = deleteBookmark ?? throw new ArgumentNullException(nameof(deleteBookmark));
            _popupSidecar = popupSidecar ?? (() => ResolutionSidecar.Empty);
        }

        /// <summary>Génère un nouvel handle id unique pour un commit OMath.</summary>
        public string NewHandleId() => "eq_" + Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>Lit le sidecar mémorisé pour un handle. Renvoie
        /// <see cref="ResolutionSidecar.Empty"/> si le handle est inconnu
        /// (cas commit pré-Phase 1.5, ou session redémarrée).</summary>
        public ResolutionSidecar GetSidecar(string handleId)
        {
            if (string.IsNullOrEmpty(handleId)) return ResolutionSidecar.Empty;
            return _sidecarsByHandle.TryGetValue(handleId, out var sc)
                ? sc
                : ResolutionSidecar.Empty;
        }

        /// <summary>Mémorise le sidecar pour un handle. Si <paramref name="overrideSidecar"/>
        /// est non-null et non-Empty, l'utilise. Sinon, fallback popup.CurrentSidecar
        /// (cf. comportement Phase 1.6 cross-merge). Si le résultat final est Empty,
        /// supprime l'entrée (pas de pollution mémoire).</summary>
        public void Stash(string handleId, ResolutionSidecar overrideSidecar = null)
        {
            if (string.IsNullOrEmpty(handleId)) return;
            var sc = (overrideSidecar != null && !overrideSidecar.IsEmpty)
                ? overrideSidecar
                : _popupSidecar();
            if (sc == null || sc.IsEmpty) { _sidecarsByHandle.Remove(handleId); return; }
            _sidecarsByHandle[handleId] = sc;
        }

        /// <summary>Mémorise un sidecar restauré (ex. depuis CustomXMLPart au
        /// reload Word, ou TryEnterEditMode). Pas de fallback popup, pas de
        /// remove-if-empty : assignement direct.</summary>
        public void Restore(string handleId, ResolutionSidecar sidecar)
        {
            if (string.IsNullOrEmpty(handleId) || sidecar == null || sidecar.IsEmpty) return;
            _sidecarsByHandle[handleId] = sidecar;
        }

        /// <summary>Supprime un handle absorbé au merge : retire le sidecar
        /// mémoire ET supprime le bookmark Word. Ne touche pas
        /// <c>IEquationStore</c> (le caller s'en charge).</summary>
        public void Forget(string handleId)
        {
            if (string.IsNullOrEmpty(handleId)) return;
            _sidecarsByHandle.Remove(handleId);
            try { _deleteBookmark(handleId); } catch { /* best-effort */ }
        }

        /// <summary>Crée un bookmark Word pour un nouvel handle. Best-effort
        /// (swallow exceptions côté délégué).</summary>
        public void CreateBookmark(string handleId, int absStart, int absEnd)
        {
            if (string.IsNullOrEmpty(handleId)) return;
            _createBookmark(handleId, absStart, absEnd);
        }
    }
}
