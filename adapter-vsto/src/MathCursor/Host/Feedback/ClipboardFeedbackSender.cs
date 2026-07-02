// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme de Percin
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
using System.Threading.Tasks;
using System.Windows;

namespace MathCursor.Host.Feedback
{
    /// <summary>
    /// Implémentation de secours : sérialise le rapport en JSON et le met dans
    /// le presse-papier. L'utilisateur le colle dans WhatsApp/email/issue GitHub.
    /// Sert tant que l'endpoint HTTP n'est pas configuré — aucun réseau requis.
    /// </summary>
    internal sealed class ClipboardFeedbackSender : IFeedbackSender
    {
        public string Name => "presse-papier";

        public Task<FeedbackResult> SendAsync(FeedbackReport report)
        {
            try
            {
                string json = FeedbackJson.Serialize(report);
                Clipboard.SetText(json);
                return Task.FromResult(new FeedbackResult
                {
                    Success = true,
                    DisplayMessage = "Rapport copié dans le presse-papier — colle-le dans le groupe WhatsApp ou par email.",
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new FeedbackResult
                {
                    Success = false,
                    DisplayMessage = "Impossible de copier le rapport. Réessaie, ou envoie-nous un message texte.",
                    ErrorDetail = ex.Message,
                });
            }
        }
    }
}
