namespace MathCursor.Host
{
    /// <summary>
    /// Action à prendre quand l'utilisateur appuie sur Enter en mode liste.
    /// </summary>
    internal enum EnterAction
    {
        /// <summary>List-mode inactif → laisser Enter passer normalement.</summary>
        Passthrough,
        /// <summary>List-mode actif mais ligne vide / whitespace-only → sortir
        /// du list-mode et laisser Enter passer (= nouveau ¶ vide).</summary>
        ExitListMode,
        /// <summary>List-mode actif + ligne avec contenu sans marker → préfixer
        /// le source avec le marker actif puis commit.</summary>
        PrefixWithActiveMarker,
        /// <summary>List-mode actif + ligne commence déjà par un marker connu
        /// (l'user l'a tapé explicitement) → valider tel quel, pas de
        /// double-préfixe.</summary>
        ValidateAsIs,
    }

    /// <summary>
    /// Machine d'état du mode liste invisible multi-ligne (cf. ADR 05-05
    /// Feat-multiline-list-mode). Logique pure, sans dépendance Word — testable
    /// directement.
    /// <para>
    /// Activé après un cross-merge multi-ligne réussi avec un marker donné.
    /// L'utilisateur peut alors taper la suite de la chaîne sans répéter le
    /// marker : le helper indique au caller de pré-préfixer la source au
    /// moment du Enter. Désactivé sur ligne vide, marker explicite (autre
    /// que l'actif), ou caret hors zone.
    /// </para>
    /// </summary>
    internal sealed class ListModeStateMachine
    {
        /// <summary>
        /// Markers connus reconnus en début de ligne. Tri par longueur
        /// décroissante pour matcher `<==>` avant `<=>`, etc. Inclut variantes
        /// Unicode (`⇔`, `⇒`, `⇐`).
        /// <para>
        /// Le marker <c>{</c> (Phase 2 cases, ADR 05-05) suit une règle stricte
        /// dans <see cref="StartsWithKnownMarker"/> : match SEULEMENT si suivi
        /// d'un espace. Sinon <c>{1,2}</c> ou <c>{x=1</c> seraient à tort
        /// validés comme système d'équations.
        /// </para>
        /// </summary>
        private static readonly string[] KnownMarkers =
        {
            "<==>", "<=>", "==>", "=>", "<==", "<=",
            "⇔", "⇒", "⇐", "↔", "⟺", "⟹", "⟸",
            "{",
            "=",
        };

        /// <summary>
        /// Marker actif (`<=>`, `=>`, etc.) ou <c>null</c> si list-mode inactif.
        /// </summary>
        public string ActiveMarker { get; private set; }

        /// <summary>
        /// Active le mode liste avec le marker donné. Appelé après un
        /// cross-merge multi-ligne réussi.
        /// </summary>
        public void OnCrossMergeSucceeded(string markerUsed)
        {
            if (string.IsNullOrEmpty(markerUsed)) return;
            ActiveMarker = markerUsed;
        }

        /// <summary>
        /// Désactive le mode (caret hors zone, clic ailleurs, scroll, etc.).
        /// </summary>
        public void OnSelectionMoved() => ActiveMarker = null;

        /// <summary>
        /// Reset explicite (commit fini, edit-mode, etc.).
        /// </summary>
        public void Reset() => ActiveMarker = null;

        /// <summary>
        /// Décide quelle action prendre quand l'utilisateur appuie sur Enter
        /// avec la ligne <paramref name="currentLineText"/>.
        /// </summary>
        public EnterAction OnEnterPressed(string currentLineText)
        {
            if (ActiveMarker == null) return EnterAction.Passthrough;

            string text = currentLineText ?? string.Empty;
            string trimmed = text.Trim();
            if (trimmed.Length == 0) return EnterAction.ExitListMode;

            // Ligne ne contient QUE le marker actif (rien tapé après) → exit.
            // Cas typique du mode visible (ADR 05-05 visible) : on a auto-injecté
            // "<=> ", l'user fait Enter direct sans rien taper → il veut sortir.
            // Comportement Word bullet list : marker disparaît, ¶ reste vide.
            if (trimmed == ActiveMarker.Trim()) return EnterAction.ExitListMode;

            // Ligne commence déjà par un marker connu → valider tel quel
            if (StartsWithKnownMarker(trimmed)) return EnterAction.ValidateAsIs;

            // Sinon : préfixer avec le marker actif
            return EnterAction.PrefixWithActiveMarker;
        }

        /// <summary>
        /// Vérifie si <paramref name="trimmed"/> (déjà <c>TrimStart</c>'é)
        /// commence par un marker connu suivi d'un séparateur (espace, lettre,
        /// chiffre... = pas un autre signe `=` qui ferait `==`).
        /// </summary>
        internal static bool StartsWithKnownMarker(string trimmed)
        {
            if (string.IsNullOrEmpty(trimmed)) return false;
            foreach (var marker in KnownMarkers)
            {
                if (!trimmed.StartsWith(marker, System.StringComparison.Ordinal)) continue;
                // Pour `=` solo : exiger qu'il ne soit pas suivi d'un autre `=`
                // (sinon on confond avec `==` qui n'est pas un marker align).
                if (marker == "=" && trimmed.Length > 1 && trimmed[1] == '=') continue;
                // Pour `{` : exiger un espace après (sinon `{1,2}` ou `{x=1`
                // — set en extension ou Backspace partiel — matcheraient à tort
                // comme système, déclenchant un faux ValidateAsIs côté list-mode).
                if (marker == "{" && (trimmed.Length < 2 || trimmed[1] != ' ')) continue;
                return true;
            }
            return false;
        }
    }
}
