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
