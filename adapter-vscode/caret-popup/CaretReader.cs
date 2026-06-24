using System;
using System.Runtime.InteropServices;
using Accessibility;

namespace MathCursor.CaretPopup
{
    // Position ÉCRAN (pixels physiques) du caret de la fenêtre au premier plan
    // (VSCode), via MSAA OBJID_CARET. Même technique que la popup Word.
    internal static class CaretReader
    {
        private const uint OBJID_CARET = 0xFFFFFFF8;
        private const int CHILDID_SELF = 0;

        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(
            IntPtr hwnd, uint id, ref Guid iid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);

        internal static bool TryGet(IntPtr hwnd, out double x, out double y, out double h)
        {
            x = y = h = 0;
            try
            {
                Guid iid = typeof(IAccessible).GUID;
                if (AccessibleObjectFromWindow(hwnd, OBJID_CARET, ref iid, out object acc) != 0 || acc == null)
                    return false;
                var ia = (IAccessible)acc;
                ia.accLocation(out int px, out int py, out int pw, out int ph, CHILDID_SELF);
                x = px; y = py; h = ph > 0 ? ph : 18;
                return px != 0 || py != 0 || pw != 0 || ph != 0;
            }
            catch { return false; }
        }
    }
}
