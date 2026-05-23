namespace MathCursor.Core
{
    /// <summary>
    /// Source alternative de <see cref="ResolvedZone"/> branchée en amont
    /// du pipeline lattice legacy. Permet le drop-in d'un moteur tiers
    /// (P11 — <c>MathCursor.Engine</c>) sans toucher l'adapter VSTO.
    ///
    /// <para>Quand un <see cref="ZoneResolver"/> est construit avec une
    /// instance non-null, il l'essaie en PREMIER pour chaque
    /// <see cref="ZoneResolver.Resolve(string, int?)"/>. Si <see cref="TryResolve"/>
    /// retourne non-null → ce résultat est utilisé. Sinon → fallback legacy.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-22-Feat-engine-poc-isolation</c>.</para>
    /// </summary>
    public interface IResolvedZoneSource
    {
        /// <summary>
        /// Tente de résoudre <paramref name="rawSource"/>. Retourne
        /// <c>null</c> pour signaler "je ne sais pas faire" (= fallback
        /// legacy demandé). Sinon retourne un <see cref="ResolvedZone"/>
        /// complet.
        ///
        /// <para>Implémentations attendues : pure fonction, pas d'effet de
        /// bord. <paramref name="diagTrace"/> peut être renseigné pour
        /// alimenter l'inspecteur (= debug pane VSTO).</para>
        /// </summary>
        ResolvedZone? TryResolve(string rawSource, out string diagTrace);
    }
}
