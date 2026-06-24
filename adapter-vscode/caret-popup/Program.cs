using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace MathCursor.CaretPopup
{
    // Helper PERSISTANT piloté par l'extension VSCode. Reste en vie ; reçoit des
    // commandes sur stdin (une par ligne, champs séparés par TAB) et émet des
    // événements sur stdout. Lit la position du caret de VSCode via MSAA à chaque
    // SHOW (popup « façon Word » qui suit le caret et ne vole jamais le focus).
    //
    // stdin  : SHOW <theme> <latex> <latex> …   |   HIDE   |   QUIT
    // stdout : COMMIT <index>                    |   DISMISS
    internal static class Program
    {
        private static PopupWindow _popup;
        private static readonly object _outLock = new object();

        [STAThread]
        private static int Main()
        {
            // Force l'UTF-8 sur les pipes stdin/stdout (sinon code page ANSI →
            // accents corrompus dans le LaTeX échangé avec l'extension).
            var utf8 = new UTF8Encoding(false);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), utf8));

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            _popup = new PopupWindow();
            _popup.Commit += i => Emit("COMMIT\t" + i);
            _popup.Dismiss += () => Emit("DISMISS");

            var reader = new Thread(() => ReadLoop(app)) { IsBackground = true };
            reader.Start();

            app.Run();
            return 0;
        }

        private static void ReadLoop(Application app)
        {
            string line;
            while ((line = Console.In.ReadLine()) != null)
            {
                var parts = line.Split('\t');
                try { app.Dispatcher.Invoke(() => Handle(parts)); }
                catch { /* dispatcher en cours d'arrêt */ }
            }
            // EOF stdin (extension fermée) : ferme la popup (désinstalle le hook
            // clavier) AVANT d'arrêter, sinon hook orphelin = clavier ralenti.
            try { app.Dispatcher.Invoke(new Action(() => { _popup.HidePopup(); app.Shutdown(); })); } catch { }
        }

        private static void Handle(string[] parts)
        {
            switch (parts[0])
            {
                case "SHOW":
                    // SHOW <theme> <reposition> <colDelta> <charWidth> <latex> …
                    bool dark = parts.Length > 1 && (parts[1] == "dark" || parts[1] == "hc");
                    bool reposition = parts.Length > 2 && parts[2] == "1";
                    int colDelta = parts.Length > 3 && int.TryParse(parts[3], out int cd) ? cd : 0;
                    double charWidth = parts.Length > 4 && double.TryParse(parts[4],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double cw) ? cw : 0;
                    var cands = new string[Math.Max(0, parts.Length - 5)];
                    for (int i = 0; i < cands.Length; i++) cands[i] = parts[i + 5];
                    if (cands.Length > 0) _popup.ShowCandidates(cands, dark, reposition, colDelta, charWidth);
                    else _popup.HidePopup();
                    break;
                case "HIDE":
                    _popup.HidePopup();
                    break;
                case "QUIT":
                    _popup.HidePopup();   // désinstalle le hook clavier avant l'arrêt
                    Application.Current.Shutdown();
                    break;
            }
        }

        private static void Emit(string s)
        {
            lock (_outLock) { Console.Out.WriteLine(s); Console.Out.Flush(); }
        }
    }
}
