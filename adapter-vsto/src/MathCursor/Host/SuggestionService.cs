using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using MathCursor.Core.Symbols;
using MathCursor.UI;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Surveille en continu le texte avant le caret (timer 200 ms) et
    /// affiche/cache une popup WPF avec les candidats SymbolMatcher.
    ///
    /// Pourquoi un timer plutôt que <c>WindowSelectionChange</c> seul :
    /// l'event Word ne fire pas sur chaque keystroke (seulement sur les
    /// mouvements explicites de curseur). Le polling 200 ms attrape la frappe.
    ///
    /// Aussi câblé : <c>WindowDeactivate</c> → cache la popup quand Word
    /// perd le focus (Alt+Tab) pour qu'elle ne reste pas TopMost à l'écran.
    /// </summary>
    public sealed class SuggestionService : IDisposable
    {
        private const int ContextChars = 50;
        private const int PollIntervalMs = 200;

        private readonly Word.Application _app;
        private SuggestionPopupWindow _popup;
        private DispatcherTimer _pollTimer;
        private string _lastContext = "";
        private int _lastCaretPos = -1;
        private bool _installed;

        public SuggestionService(Word.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        public void Install()
        {
            if (_installed) return;
            _app.WindowSelectionChange += OnSelectionChange;
            _app.WindowDeactivate += OnWindowDeactivate;
            _app.WindowActivate += OnWindowActivate;

            _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(PollIntervalMs),
            };
            _pollTimer.Tick += (_, __) => CheckContextAndUpdate();
            _pollTimer.Start();
            _installed = true;
        }

        public void Dispose()
        {
            try { if (_installed) _app.WindowSelectionChange -= OnSelectionChange; } catch { }
            try { if (_installed) _app.WindowDeactivate -= OnWindowDeactivate; } catch { }
            try { if (_installed) _app.WindowActivate -= OnWindowActivate; } catch { }
            try { _pollTimer?.Stop(); } catch { }
            try { _popup?.Close(); } catch { }
            _popup = null;
            _pollTimer = null;
            _installed = false;
        }

        public bool IsPopupVisible => _popup != null && _popup.IsVisible;
        public int SelectedIndex => _popup?.SelectedIndex ?? 0;

        public void MoveSelection(int delta) => _popup?.MoveSelection(delta);

        public void HidePopup() => _popup?.HidePopup();

        private void OnSelectionChange(Word.Selection sel)
        {
            // Backup : déclenche aussi un check immédiat (sans attendre le tick)
            // pour les mouvements explicites de curseur (clic, flèches).
            CheckContextAndUpdate();
        }

        private void OnWindowDeactivate(Word.Document doc, Word.Window wnd)
        {
            // Word perd le focus → cacher la popup + pauser le polling pour
            // ne pas accéder à un Selection invalide quand Word est hors focus.
            HidePopup();
            try { _pollTimer?.Stop(); } catch { }
        }

        private void OnWindowActivate(Word.Document doc, Word.Window wnd)
        {
            try { _pollTimer?.Start(); } catch { }
        }

        private void CheckContextAndUpdate()
        {
            try
            {
                // Garde : pas de doc actif → rien à faire (timer continue mais
                // skip rapide). Évite d'accéder à Selection en état transitoire.
                if (_app.Documents.Count == 0)
                {
                    HidePopup();
                    return;
                }

                int caretPos;
                string ctx;
                try
                {
                    var sel = _app.Selection;
                    if (sel == null) return;
                    caretPos = sel.Start;
                    ctx = ReadContext(caretPos);
                }
                catch
                {
                    return; // Word en état transitoire, on attend le prochain tick
                }

                if (ctx == _lastContext && caretPos == _lastCaretPos) return;
                _lastContext = ctx;
                _lastCaretPos = caretPos;

                var match = SymbolMatcher.FindSymbol(ctx);
                if (match != null && match.Choices.Count > 0)
                {
                    ShowPopup(match.Choices);
                }
                else
                {
                    HidePopup();
                }
            }
            catch
            {
                // Jamais d'exception depuis le timer
            }
        }

        private string ReadContext(int caretPos)
        {
            var doc = _app.ActiveDocument;
            int start = Math.Max(doc.Content.Start, caretPos - ContextChars);
            if (start >= caretPos) return "";
            return doc.Range(start, caretPos).Text ?? "";
        }

        private void ShowPopup(System.Collections.Generic.IReadOnlyList<SymbolChoice> choices)
        {
            if (_popup == null) _popup = new SuggestionPopupWindow();
            var pos = GetCaretScreenPosition();
            _popup.ShowSuggestions(choices, pos.x, pos.y);
        }

        private (double x, double y) GetCaretScreenPosition()
        {
            try
            {
                // Word maintient un caret système (pour accessibilité / IME).
                // GetCaretPos retourne sa position dans les coords client de la
                // fenêtre qui le possède. ClientToScreen convertit en absolu.
                if (!GetCaretPos(out POINT pt))
                {
                    LogPos("ERR GetCaretPos returned false");
                    return (200, 200);
                }
                IntPtr hwnd = GetFocus();
                if (hwnd != IntPtr.Zero)
                {
                    ClientToScreen(hwnd, ref pt);
                }
                LogPos($"caret screen=({pt.X},{pt.Y}) hwnd=0x{hwnd.ToInt64():X}");
                return (pt.X, pt.Y + 22); // 22px sous la ligne
            }
            catch (Exception ex)
            {
                LogPos("ERR " + ex.GetType().Name + " " + ex.Message);
                return (200, 200);
            }
        }

        private static void LogPos(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} pos {message}{Environment.NewLine}");
            }
            catch { }
        }

        // --- Win32 ---
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCaretPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();
    }
}
