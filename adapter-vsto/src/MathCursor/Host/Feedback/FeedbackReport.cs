// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;

namespace MathCursor.Host.Feedback
{
    /// <summary>
    /// Rapport de feedback envoyé depuis le lien "Signaler une erreur" de la popup.
    /// Contexte technique pré-rempli au moment du clic + message utilisateur saisi
    /// dans le dialog. Structure flat volontairement (sérialisable en JSON simple,
    /// pas de dépendance Newtonsoft/etc).
    /// </summary>
    public sealed class FeedbackReport
    {
        public string Version { get; set; } = "";
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string UserId { get; set; } = "";
        public string SessionId { get; set; } = "";

        /// <summary>Texte source brut tapé par l'utilisateur (zone détectée
        /// par le NER ou span d'un trigger manuel). Sérialisé en `source_text`.</summary>
        public string NerText { get; set; } = "";

        /// <summary>LaTeX top-1 que MathCursor a proposé dans la popup.
        /// Sérialisé en `proposed_latex`.</summary>
        public string RecognizedFormula { get; set; } = "";

        /// <summary>LaTeX qui a été passé à InsertOMathAt (commit). Vide si
        /// l'utilisateur n'a pas committé. Sérialisé en `committed_latex`.</summary>
        public string CommittedLatex { get; set; } = "";

        /// <summary>Texte du paragraphe Word courant (contexte). Sérialisé
        /// en `paragraph_context`. Tronqué côté snapshot si très long.</summary>
        public string ParagraphContext { get; set; } = "";

        /// <summary>Message libre de l'utilisateur (ce qu'il voulait faire,
        /// ce qui s'est passé). Sérialisé en `user_comment`.</summary>
        public string UserMessage { get; set; } = "";

        /// <summary>Email facultatif pour recontact.</summary>
        public string UserEmail { get; set; } = "";

        /// <summary>Capture d'écran PNG encodée en base64. Vide si l'user a
        /// décoché. Sérialisé en `screenshot_b64`.</summary>
        public string ScreenshotPngBase64 { get; set; } = "";

        /// <summary>Dernière portion du log local (diagnostic). Vide si l'user
        /// a décoché. Sérialisé en `log_tail`.</summary>
        public string LogTail { get; set; } = "";

        public string WordVersion { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public string DotNetVersion { get; set; } = "";
    }

    /// <summary>Retour d'un envoi de feedback.</summary>
    public sealed class FeedbackResult
    {
        public bool Success { get; set; }
        /// <summary>Message affichable à l'utilisateur (succès OU échec).</summary>
        public string DisplayMessage { get; set; } = "";
        /// <summary>Détail technique (erreur réseau, etc.), pour logs uniquement.</summary>
        public string ErrorDetail { get; set; } = "";
    }
}
