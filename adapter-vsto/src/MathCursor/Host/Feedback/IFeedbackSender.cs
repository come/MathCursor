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

using System.Threading.Tasks;

namespace MathCursor.Host.Feedback
{
    /// <summary>
    /// Abstraction de l'envoi d'un rapport. Deux implémentations prévues :
    /// - ClipboardFeedbackSender : met le JSON dans le presse-papier (marche jour 1)
    /// - HttpFeedbackSender : POST vers une API configurable (activé quand l'URL est
    ///   renseignée via config ou variable d'environnement)
    /// Le choix est fait au runtime par FeedbackSenderFactory.
    /// </summary>
    public interface IFeedbackSender
    {
        /// <summary>Nom affiché à l'utilisateur après envoi ("envoyé via ..." / logs).</summary>
        string Name { get; }
        Task<FeedbackResult> SendAsync(FeedbackReport report);
    }
}
