using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace MathCursor.UI.Debug
{
    /// <summary>
    /// Wrapper WinForms autour de <see cref="ContextInspectorPane"/> (WPF).
    /// Nécessaire car <c>CustomTaskPanes.Add</c> demande un
    /// <see cref="UserControl"/> WinForms — on embed le WPF via
    /// <see cref="ElementHost"/>.
    /// </summary>
    public sealed class ContextInspectorPaneHost : UserControl
    {
        public ContextInspectorPane WpfPane { get; }

        public ContextInspectorPaneHost()
        {
            WpfPane = new ContextInspectorPane();
            var host = new ElementHost
            {
                Dock = DockStyle.Fill,
                Child = WpfPane,
            };
            Controls.Add(host);
        }
    }
}
