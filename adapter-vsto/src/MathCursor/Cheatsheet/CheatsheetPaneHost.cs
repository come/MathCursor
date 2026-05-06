using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace MathCursor.Cheatsheet
{
    /// <summary>
    /// Host WinForms hébergé par <c>CustomTaskPane</c> Word, qui contient un
    /// <see cref="ElementHost"/> embarquant le WPF <see cref="CheatsheetPane"/>.
    /// VSTO ne supporte que les <c>System.Windows.Forms.UserControl</c> dans
    /// <c>CustomTaskPanes.Add()</c>, d'où ce wrapper.
    /// </summary>
    internal sealed class CheatsheetPaneHost : UserControl
    {
        public CheatsheetPane WpfPane { get; }

        public CheatsheetPaneHost(CheatsheetViewModel vm)
        {
            if (vm == null) throw new ArgumentNullException(nameof(vm));
            WpfPane = new CheatsheetPane(vm);
            var elementHost = new ElementHost
            {
                Dock = DockStyle.Fill,
                Child = WpfPane,
            };
            Controls.Add(elementHost);
        }
    }
}
