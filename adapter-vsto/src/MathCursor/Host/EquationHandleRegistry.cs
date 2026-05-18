using System;
using System.Collections.Generic;
using MathCursor.Core.Resolution;

namespace MathCursor.Host
{
    /// <summary>
    /// Registre in-memory des sidecars indexés par handleId. Mapping
    /// handleId → <see cref="ResolutionSidecar"/> + génération d'IDs.
    ///
    /// <para>Phase B (2026-05-18) : les bookmarks Word + le store
    /// CustomXMLPart ont été supprimés. La source / latex / hash vivent
    /// dans le <c>cc.Tag</c> JSON de l'OMath (cf. <c>MCMeta</c>). Cette
    /// registry ne gère plus que les sidecars (pins/votes de désambig)
    /// en mémoire pour la session.</para>
    /// </summary>
    internal sealed class EquationHandleRegistry
    {
        private readonly Dictionary<string, ResolutionSidecar> _sidecarsByHandle
            = new Dictionary<string, ResolutionSidecar>();
        private readonly Func<ResolutionSidecar> _popupSidecar;

        /// <param name="popupSidecar">Délégué : <c>() → popup.CurrentSidecar</c>. Lu
        /// par <see cref="Stash"/> en fallback quand l'override est Empty.</param>
        public EquationHandleRegistry(Func<ResolutionSidecar> popupSidecar)
        {
            _popupSidecar = popupSidecar ?? (() => ResolutionSidecar.Empty);
        }

        /// <summary>Génère un nouvel handle id unique pour un commit OMath.
        /// Mémorisé dans <c>cc.Tag</c> MCMeta.HandleId pour persistance.</summary>
        public string NewHandleId() => "eq_" + Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>Lit le sidecar mémorisé pour un handle. Renvoie
        /// <see cref="ResolutionSidecar.Empty"/> si le handle est inconnu
        /// (cas session redémarrée — sidecars non-persistés).</summary>
        public ResolutionSidecar GetSidecar(string handleId)
        {
            if (string.IsNullOrEmpty(handleId)) return ResolutionSidecar.Empty;
            return _sidecarsByHandle.TryGetValue(handleId, out var sc)
                ? sc
                : ResolutionSidecar.Empty;
        }

        /// <summary>Mémorise le sidecar pour un handle. Si <paramref name="overrideSidecar"/>
        /// est non-null et non-Empty, l'utilise. Sinon, fallback popup.CurrentSidecar.
        /// Si le résultat final est Empty, supprime l'entrée (pas de pollution mémoire).</summary>
        public void Stash(string handleId, ResolutionSidecar overrideSidecar = null)
        {
            if (string.IsNullOrEmpty(handleId)) return;
            var sc = (overrideSidecar != null && !overrideSidecar.IsEmpty)
                ? overrideSidecar
                : _popupSidecar();
            if (sc == null || sc.IsEmpty) { _sidecarsByHandle.Remove(handleId); return; }
            _sidecarsByHandle[handleId] = sc;
        }

        /// <summary>Mémorise un sidecar restauré (ex. TryEnterEditMode si le
        /// sidecar avait été stocké séparément). Pas de fallback popup, pas de
        /// remove-if-empty : assignement direct.</summary>
        public void Restore(string handleId, ResolutionSidecar sidecar)
        {
            if (string.IsNullOrEmpty(handleId) || sidecar == null || sidecar.IsEmpty) return;
            _sidecarsByHandle[handleId] = sidecar;
        }

        /// <summary>Supprime un handle absorbé au merge : retire le sidecar
        /// mémoire. Le CC + son Tag ont déjà été supprimés par
        /// <c>sel.Delete</c> dans <c>InsertOMathAt</c>.</summary>
        public void Forget(string handleId)
        {
            if (string.IsNullOrEmpty(handleId)) return;
            _sidecarsByHandle.Remove(handleId);
        }
    }
}
