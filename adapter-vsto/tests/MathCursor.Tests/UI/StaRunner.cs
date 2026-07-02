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
using System.Threading;

namespace MathCursor.Tests.UI
{
    /// <summary>
    /// Exécute du code WPF dans un thread STA dédié. xUnit tourne en MTA par
    /// défaut, mais WPF (notamment <c>FormulaControl</c>) refuse de
    /// s'instancier hors STA. Plus simple qu'ajouter <c>xunit.stafact</c>
    /// comme dépendance.
    /// </summary>
    internal static class StaRunner
    {
        public static T Run<T>(Func<T> action)
        {
            T result = default;
            Exception captured = null;
            var thread = new Thread(() =>
            {
                try { result = action(); }
                catch (Exception ex) { captured = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();
            if (captured != null) throw captured;
            return result;
        }

        public static void Run(Action action) => Run<object>(() => { action(); return null; });
    }
}
