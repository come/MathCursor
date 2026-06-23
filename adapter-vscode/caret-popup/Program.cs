using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using Accessibility;

namespace MathCursor.CaretPopup
{
    // SPIKE — helper natif piloté par l'extension VSCode. Lit la position ÉCRAN du
    // caret de la fenêtre au premier plan (VSCode) via MSAA OBJID_CARET, affiche une
    // popup WPF borderless topmost à cet endroit, navigation clavier, et renvoie le
    // LaTeX choisi sur stdout (code 0). Échap/abandon → code 1.
    //
    // Protocole : candidats lus sur stdin (un par ligne) ; choix écrit sur stdout.
    internal static class Program
    {
        private const uint OBJID_CARET = 0xFFFFFFF8;
        private const int CHILDID_SELF = 0;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(
            IntPtr hwnd, uint id, ref Guid iid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);

        [STAThread]
        private static int Main(string[] args)
        {
            // Fenêtre au premier plan (VSCode) AVANT de créer notre fenêtre.
            IntPtr fg = GetForegroundWindow();

            var cands = new List<string>();
            string line;
            while ((line = Console.In.ReadLine()) != null)
                if (line.Length > 0) cands.Add(line);
            if (cands.Count == 0) return 2;

            bool gotCaret = TryGetCaret(fg, out double cx, out double cy, out double ch, out string diag);
            // Diagnostic du spike sur stderr (visible côté extension).
            Console.Error.WriteLine($"[caret-popup] fg=0x{fg.ToInt64():X} caret={(gotCaret ? "OK" : "FAIL")} x={cx} y={cy} h={ch} {diag}");
            if (!gotCaret) { cx = 300; cy = 300; ch = 18; } // repli visible si MSAA échoue

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var win = new PopupWindow(cands, cx, cy, ch, fg);
            app.Run(win);

            if (win.PickedIndex >= 0)
            {
                Console.Out.Write(cands[win.PickedIndex]);
                return 0;
            }
            return 1;
        }

        private static bool TryGetCaret(IntPtr hwnd, out double x, out double y, out double h, out string diag)
        {
            x = y = h = 0; diag = "";
            try
            {
                Guid iid = typeof(IAccessible).GUID;
                int hr = AccessibleObjectFromWindow(hwnd, OBJID_CARET, ref iid, out object acc);
                if (hr != 0 || acc == null) { diag = $"AOFW hr=0x{hr:X} acc={(acc == null ? "null" : "obj")}"; return false; }
                var ia = (IAccessible)acc;
                ia.accLocation(out int px, out int py, out int pw, out int ph, CHILDID_SELF);
                x = px; y = py; h = ph > 0 ? ph : 18;
                bool ok = px != 0 || py != 0 || pw != 0 || ph != 0;
                diag = $"loc=({px},{py},{pw},{ph})";
                return ok;
            }
            catch (Exception ex) { diag = "ex=" + ex.Message; return false; }
        }
    }
}
