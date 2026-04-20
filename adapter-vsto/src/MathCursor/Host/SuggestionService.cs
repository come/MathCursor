using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using MathCursor.Core.Symbols;
using MathCursor.Detection;
using MathCursor.UI;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Surveille en continu le paragraphe courant (timer 200 ms) et affiche
    /// une popup WPF avec les zones math détectées par le modèle NER.
    ///
    /// Pivot pivot ML : la popup affiche maintenant ce que le modèle NER détecte
    /// (zones math + confiance), pas le résultat du pipeline heuristique.
    /// </summary>
    public sealed class SuggestionService : IDisposable
    {
        private const int PollIntervalMs = 200;

        private readonly Word.Application _app;
        private readonly WordContextReader _contextReader;
        private readonly MathNerDetector _ner;

        private SuggestionPopupWindow _popup;
        private DispatcherTimer _pollTimer;
        private string _lastParagraph = "";
        private int _lastCaretPos = -1;
        private bool _installed;
        // Inférence asynchrone : on évite de bloquer le thread UI
        private bool _inferenceInFlight;

        public SuggestionService(Word.Application app, MathNerDetector ner)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _ner = ner ?? throw new ArgumentNullException(nameof(ner));
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
            _pollTimer.Tick += (_, __) => CheckAndUpdate();
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

        private void OnSelectionChange(Word.Selection sel) => CheckAndUpdate();

        private void OnWindowDeactivate(Word.Document doc, Word.Window wnd)
        {
            HidePopup();
            try { _pollTimer?.Stop(); } catch { }
        }

        private void OnWindowActivate(Word.Document doc, Word.Window wnd)
        {
            try { _pollTimer?.Start(); } catch { }
        }

        private void CheckAndUpdate()
        {
            if (_inferenceInFlight) return;
            try
            {
                if (_app.Documents.Count == 0)
                {
                    HidePopup();
                    return;
                }

                string paragraphText;
                int caretInParagraph, caretPos;
                try
                {
                    var (text, offset) = _contextReader.ReadCurrentParagraph();
                    paragraphText = text;
                    caretInParagraph = offset; // déjà relatif au paragraphe
                    caretPos = _app.Selection.Start;
                }
                catch
                {
                    return;
                }

                // Skip si rien n'a changé depuis le dernier check
                if (paragraphText == _lastParagraph && caretPos == _lastCaretPos) return;
                _lastParagraph = paragraphText;
                _lastCaretPos = caretPos;

                if (string.IsNullOrWhiteSpace(paragraphText))
                {
                    HidePopup();
                    return;
                }

                LogDiag($"tick len={paragraphText.Length} caret={caretInParagraph} text=\"{Preview(paragraphText)}\"");

                // Inférence sur thread pool (~30-80 ms hors warm-up)
                _inferenceInFlight = true;
                Task.Run(() =>
                {
                    IReadOnlyList<DetectedZone> zones;
                    try { zones = _ner.Detect(paragraphText); }
                    catch (Exception ex) { LogDiag("ner_error: " + ex.Message); zones = Array.Empty<DetectedZone>(); }

                    LogDiag($"zones={zones.Count} -> {string.Join(" | ", zones.Select(z => z.ToString()))}");

                    // Retour sur le thread UI pour mettre à jour la popup
                    _pollTimer?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { ApplyZones(zones, caretInParagraph); }
                        finally { _inferenceInFlight = false; }
                    }));
                });
            }
            catch
            {
                _inferenceInFlight = false;
            }
        }

        private void ApplyZones(IReadOnlyList<DetectedZone> zones, int caretInParagraph)
        {
            if (zones == null || zones.Count == 0)
            {
                HidePopup();
                return;
            }

            // On n'affiche que la zone la plus proche du curseur, et uniquement
            // si le curseur est DANS la zone ou directement collé à un bord
            // (touche la frontière). Sinon → rien.
            var target = PickNearestZone(zones, caretInParagraph, out int dist);
            LogDiag($"pick caret={caretInParagraph} target={(target == null ? "null" : target.ToString())} dist={dist}");
            if (target == null || dist > 0)
            {
                HidePopup();
                return;
            }

            var choices = new List<SymbolChoice>
            {
                new SymbolChoice
                {
                    Display = target.Text,
                    Replacement = target.Text,
                    Label = $"{target.Confidence:P0}",
                }
            };

            int rawLen = target.Text?.Length ?? 0;
            ShowPopup(choices, rawLen);
        }

        private static DetectedZone PickNearestZone(IReadOnlyList<DetectedZone> zones, int caret, out int bestDist)
        {
            DetectedZone best = null;
            bestDist = int.MaxValue;
            foreach (var z in zones)
            {
                int dist;
                if (caret >= z.Start && caret <= z.End) dist = 0;       // curseur dedans ou collé au bord
                else if (caret < z.Start) dist = z.Start - caret;       // zone après le curseur
                else dist = caret - z.End;                              // zone avant le curseur
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = z;
                }
            }
            return best;
        }

        // ============================================================
        // Positionnement popup (inchangé du heuristique)
        // ============================================================

        private const double AvgCharWidthDip = 7.0;
        private const double PopupWidthDip = 280.0;

        private void ShowPopup(IReadOnlyList<SymbolChoice> choices, int rawZoneLength)
        {
            if (_popup == null) _popup = new SuggestionPopupWindow();
            var pos = GetCaretScreenPosition();
            double zoneWidth = Math.Max(0, rawZoneLength) * AvgCharWidthDip;
            double offset = Math.Min(zoneWidth, PopupWidthDip);
            double popupX = pos.x - offset;
            if (popupX < 0) popupX = 0;
            _popup.ShowSuggestions(choices, popupX, pos.y);
        }

        private (double x, double y) GetCaretScreenPosition()
        {
            try
            {
                if (!GetCaretPos(out POINT pt))
                {
                    return (200, 200);
                }
                IntPtr hwnd = GetFocus();
                if (hwnd != IntPtr.Zero) ClientToScreen(hwnd, ref pt);
                double scale = GetDpiScale();
                return (pt.X / scale, pt.Y / scale + 22);
            }
            catch
            {
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
            catch { return 1.0; }
        }

        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }

        private static void LogDiag(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} ner {message}{Environment.NewLine}");
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
