using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using MathCursor.Core.Pipeline;
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
        private readonly WordContextReader _contextReader;
        private SuggestionPopupWindow _popup;
        private DispatcherTimer _pollTimer;
        private string _lastContext = "";
        private int _lastCaretPos = -1;
        private bool _installed;

        public SuggestionService(Word.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _contextReader = new WordContextReader(_app);
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
        public bool IsNavMode => _popup != null && _popup.IsNavMode;
        public int SelectedIndex => _popup?.SelectedIndex ?? 0;

        public void MoveSelection(int delta) => _popup?.MoveSelection(delta);
        public void EnterNavMode() => _popup?.EnterNavMode();

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
                    // Lecture déléguée à WordContextReader (paragraph-bounded, source unique)
                    ctx = _contextReader.ReadBefore(ContextChars);
                }
                catch
                {
                    return; // Word en état transitoire, on attend le prochain tick
                }

                if (ctx == _lastContext && caretPos == _lastCaretPos) return;
                _lastContext = ctx;
                _lastCaretPos = caretPos;

                // On utilise le pipeline complet pour que la popup reflète
                // exactement ce que Tab produira : "alpha+beta" → "α+β" plutôt
                // que juste "β" (qui était le résultat avec FindSymbol seul).
                var result = ConversionPipeline.Convert(ctx);
                if (result.Success && result.Equation != null)
                {
                    var display = !string.IsNullOrEmpty(result.Equation.UnicodeFallback)
                        ? result.Equation.UnicodeFallback
                        : result.Equation.Source;
                    var label = result.Zone != null ? "expression" : "symbole";
                    var choice = new SymbolChoice
                    {
                        Display = display,
                        Replacement = display,
                        Label = label,
                    };
                    int rawLen = result.Equation.Source?.Length ?? 0;
                    ShowPopup(new[] { choice }, rawLen);
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

        // Largeur moyenne d'un caractère (DIPs) — estimation pour Calibri 11pt à 100%.
        // Pour ajuster selon zoom Word, on pourrait lire ActiveWindow.View.Zoom.Percentage.
        private const double AvgCharWidthDip = 7.0;
        private const double PopupWidthDip = 280.0;

        private void ShowPopup(IReadOnlyList<SymbolChoice> choices, int rawZoneLength)
        {
            if (_popup == null) _popup = new SuggestionPopupWindow();
            var pos = GetCaretScreenPosition();

            // Décale la popup vers la GAUCHE pour qu'elle se positionne sous le
            // texte qu'elle va remplacer (la zone math), plutôt que pile sous le
            // curseur. Largeur estimée = rawZoneLength × char_width. Plafonnée
            // à la largeur popup pour que le curseur reste à minima au-dessus
            // (à la limite : au bord droit de la popup pour les zones très longues).
            double zoneWidth = Math.Max(0, rawZoneLength) * AvgCharWidthDip;
            double offset = Math.Min(zoneWidth, PopupWidthDip);
            double popupX = pos.x - offset;
            if (popupX < 0) popupX = 0; // ne pas sortir à gauche de l'écran

            _popup.ShowSuggestions(choices, popupX, pos.y);
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
                // GetCaretPos retourne des pixels physiques. WPF Window.Left/Top
                // sont en DIPs (1/96"). Sur un écran à 150% DPI, sans cette conversion,
                // la popup atterrit 50% trop loin.
                double scale = GetDpiScale();
                double dipX = pt.X / scale;
                double dipY = pt.Y / scale;
                LogPos($"caret physical=({pt.X},{pt.Y}) scale={scale:F2} dip=({dipX:F0},{dipY:F0})");
                return (dipX, dipY + 22); // 22 DIP sous la ligne
            }
            catch (Exception ex)
            {
                LogPos("ERR " + ex.GetType().Name + " " + ex.Message);
                return (200, 200);
            }
        }

        private static double GetDpiScale()
        {
            try
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    return g.DpiX / 96.0;
                }
            }
            catch
            {
                return 1.0;
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
