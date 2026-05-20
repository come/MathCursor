using System;

namespace MathCursor.Host
{
    /// <summary>
    /// Snapshot de la dernière action utilisateur (popup affichée + commit
    /// éventuel) — utilisé pour pré-remplir la fenêtre "Signaler une erreur"
    /// avec les 3 informations qui contextualisent un bug :
    ///   1. <see cref="SourceText"/>      : ce que l'utilisateur a tapé
    ///   2. <see cref="ProposedLatex"/>   : ce que MathCursor a proposé en popup
    ///   3. <see cref="CommittedLatex"/>  : ce qui a été inséré (si commit)
    ///
    /// On ne maintient qu'UN snapshot (pas une queue) : le bug est quasi
    /// toujours sur la dernière action, et l'utilisateur peut éditer les
    /// champs à la main si besoin de signaler quelque chose de plus ancien.
    ///
    /// Cf. brief 2026-04-30-feedback-form-with-cloudflare-backend.md §3.1.
    /// </summary>
    public sealed class LastActionSnapshot
    {
        /// <summary>Quand la dernière mise à jour a eu lieu (UTC).</summary>
        public DateTime At { get; set; }

        /// <summary>Texte source brut que l'utilisateur a tapé (zone détectée).</summary>
        public string SourceText { get; set; }

        /// <summary>LaTeX top-1 que l'add-in a proposé dans la popup.</summary>
        public string ProposedLatex { get; set; }

        /// <summary>LaTeX qui a été passé à InsertOMathAt (commit).
        /// Null si l'utilisateur n'a pas encore committé.</summary>
        public string CommittedLatex { get; set; }

        /// <summary>Texte du paragraphe Word où l'action s'est passée (contexte).</summary>
        public string ParagraphContext { get; set; }
    }
}
