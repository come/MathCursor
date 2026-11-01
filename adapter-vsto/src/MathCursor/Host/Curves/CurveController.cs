// MathCursor: capturing mathematical intent from linear keyboard input.
// Copyright (C) 2026  Côme de Percin
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
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using MathCursor.Engine;
using MathCursor.Host.Caret;
using MathCursor.Host.Settings;
using MathCursor.Host.SourceMap;
using MathCursor.UI;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Curves
{
    /// <summary>
    /// Dessins de courbes « comme un tableau » : le bouton Courbe insère tout de
    /// suite un repère (avec la fonction sous le caret si elle se trace) ; un
    /// dessin sélectionné ouvre le panneau de réglages à côté de lui, qui suit
    /// l'image au défilement et met l'image à jour à chaque modification.
    /// Le panneau vit sur son propre thread WPF ; moteur et COM restent ici, sur
    /// le thread de Word. ADR 2026-10-31-Feat-insert-curves,
    /// 2026-10-31-UX-curve-floating-panel.
    /// </summary>
    internal sealed class CurveController
    {
        private const int GapPx = 12;

        private readonly Word.Application _app;
        private readonly Func<SourceMapResolver> _resolver;
        private readonly Action<string> _log;
        private readonly Dispatcher _word;            // thread de Word
        private readonly DispatcherTimer _follow;

        private CurvePanel _panel;
        private Word.InlineShape _shape;
        private IntPtr _wordHwnd;
        private IntPtr _panelHwnd;
        private EngineCulture _culture;
        private string _appliedKey;
        private bool _panelShown;
        private double _lastX = double.NaN, _lastY = double.NaN;
        private bool _updating;
        private CurveSpec _current;                   // dernière spec tracée (zoom molette)

        // Molette sur l'image sélectionnée = zoom du repère : hook souris du thread de
        // Word, posé seulement panneau ouvert (comme le minuteur de position).
        private readonly DispatcherTimer _zoomTimer;
        private HookProc _mouseProc;
        private IntPtr _mouseHook;
        private bool _imgKnown;
        private int _imgL, _imgT, _imgW, _imgH;
        private int _zoomDelta;
        private int _zoomX, _zoomY;

        public CurveController(Word.Application app, Func<SourceMapResolver> resolver, Action<string> log)
        {
            _app = app; _resolver = resolver; _log = log ?? (_ => { });
            _word = Dispatcher.CurrentDispatcher;
            // Pas d'événement de défilement dans Word : la position de l'image est
            // relue tant que le panneau est ouvert, et seulement alors (ADR).
            _follow = new DispatcherTimer(DispatcherPriority.Background, _word) { Interval = TimeSpan.FromMilliseconds(120) };
            _follow.Tick += (_, __) => Follow();
            // les crans de molette s'accumulent, l'image est redessinée au plus toutes les 60 ms
            _zoomTimer = new DispatcherTimer(DispatcherPriority.Normal, _word) { Interval = TimeSpan.FromMilliseconds(60) };
            _zoomTimer.Tick += (_, __) => { _zoomTimer.Stop(); ApplyZoom(); };
        }

        // ── Entrées ────────────────────────────────────────────────────

        /// <summary>Bouton du ruban : dessin sélectionné → son panneau ; sinon insère un
        /// repère (avec la fonction sous le caret) et ouvre son panneau.</summary>
        public void OpenFromRibbon()
        {
            var selected = CurveInserter.SelectedCurve(_app);
            if (selected != null) { ShowPanel(selected, focus: true); return; }

            var spec = new CurveSpec { Style = SettingsStore.Current.CurveStyle };
            var f = FunctionNearCaret();
            if (f != null) spec.Functions.Add(f);
            _culture = Culture();
            Word.InlineShape shape;
            _updating = true;
            try
            {
                shape = Render(null, spec);
                CurveStore.Save(_app.ActiveDocument, spec, _log);
                shape.Select();
            }
            catch (Exception ex)
            {
                _log("curve_error: " + ex.Message);
                System.Windows.MessageBox.Show(Strings.CurveInsertFailed + ex.Message, "MathCursor",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            finally { _updating = false; }
            _log($"curve_insert prefilled={f != null}");
            ShowPanel(shape, focus: true);
        }

        /// <summary>Double-clic : vrai si c'était un de nos dessins (le double-clic Word est
        /// alors annulé par l'appelant) ; le panneau prend le focus.</summary>
        public bool TryEditSelected()
        {
            var shape = CurveInserter.SelectedCurve(_app);
            if (shape == null) return false;
            ShowPanel(shape, focus: true);
            return true;
        }

        /// <summary>Événement natif <c>WindowSelectionChange</c> : un dessin sélectionné
        /// ouvre son panneau (sans lui donner le focus : Suppr, Ctrl+C restent à Word),
        /// toute autre sélection le ferme.</summary>
        public void OnSelectionChanged()
        {
            if (_updating) return;
            var shape = CurveInserter.SelectedCurve(_app);
            if (shape == null) ClosePanel();
            else ShowPanel(shape, focus: false);
        }

        // ── Panneau ────────────────────────────────────────────────────

        private void ShowPanel(Word.InlineShape shape, bool focus)
        {
            var id = CurveSpec.IdFromTitle(CurveInserter.SafeTitle(shape));
            if (id == null) return;
            _shape = shape;
            if (_panel == null || _panel.CurveId != id)
            {
                ClosePanel();
                _shape = shape;
                var doc = shape.Range.Document;
                // spec perdue (image copiée d'un autre document) : on repart d'un dessin
                // vide qui remplacera celui-ci à la première modification
                var spec = CurveStore.Load(doc, id, _log) ?? new CurveSpec { Id = id, Style = SettingsStore.Current.CurveStyle };
                _current = Clean(spec);
                _appliedKey = Key(_current);
                _culture = Culture();
                try { _wordHwnd = GetAncestor(new IntPtr(doc.ActiveWindow.Hwnd), GA_ROOT); }
                catch { _wordHwnd = IntPtr.Zero; }
                _panel = StartPanel(spec);
                _panelShown = false;
                _lastX = _lastY = double.NaN;
                _follow.Start();
                InstallWheel();
                Follow();
            }
            if (focus)
            {
                var p = _panel;
                p.Dispatcher.BeginInvoke(new Action(() => { if (!p.IsVisible) p.Show(); p.FocusFirstField(); }));
            }
        }

        // Le panneau tourne sur son thread STA : la saisie y est celle d'une vraie
        // fenêtre WPF, sans passer par la boucle de messages ni le hook de Word.
        // SANS propriétaire Word : un lien propriétaire entre threads fusionne leurs
        // files d'entrée, et Word reprenait le focus à chaque clic dans le panneau
        // (constaté le 31/10). Il est donc « au premier plan » et Follow le cache
        // dès que ni Word ni lui ne sont actifs.
        private CurvePanel StartPanel(CurveSpec spec)
        {
            CurvePanel panel = null;
            IntPtr hwnd = IntPtr.Zero;
            using (var ready = new ManualResetEventSlim())
            {
                var t = new Thread(() =>
                {
                    try
                    {
                        panel = new CurvePanel(spec) { Topmost = true };
                        hwnd = new WindowInteropHelper(panel).EnsureHandle();
                        panel.Edited += s => _word.BeginInvoke(new Action(() => OnEdited(panel, s)));
                        panel.Dismissed += () => _word.BeginInvoke(new Action(() => BackToDocument(panel)));
                    }
                    finally { ready.Set(); }
                    Dispatcher.Run();
                });
                t.SetApartmentState(ApartmentState.STA);
                t.IsBackground = true;
                t.Name = "MathCursor.CurvePanel";
                t.Start();
                ready.Wait();
            }
            _panelHwnd = hwnd;
            return panel;
        }

        public void ClosePanel()
        {
            _follow.Stop();
            _zoomTimer.Stop();
            RemoveWheel();
            _imgKnown = false;
            var p = _panel;
            _panel = null;
            _shape = null;
            _panelShown = false;
            KeyboardInterceptor.Suspended = false;
            if (p == null) return;
            try
            {
                p.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { p.Close(); } catch { }
                    p.Dispatcher.InvokeShutdown();
                }));
            }
            catch { }
        }

        // Échap ou ✕ : on ferme et le curseur repart sous l'image, dans Word.
        private void BackToDocument(CurvePanel panel)
        {
            if (panel != _panel) return;
            var shape = _shape;
            ClosePanel();
            try
            {
                if (_wordHwnd != IntPtr.Zero) SetForegroundWindow(_wordHwnd);
                if (shape != null) CurveInserter.PlaceCaretAfter(shape.Range.Document, _app.Selection, shape);
            }
            catch (Exception ex) { _log("curve: retour au document KO: " + ex.Message); }
        }

        // ── Mise à jour de l'image ─────────────────────────────────────

        private void OnEdited(CurvePanel panel, CurveSpec spec)
        {
            if (panel != _panel || _shape == null) return;
            CurveRenderer.Compile(spec, _culture, out var errors, out var lines);
            var readings = lines
                .Select(l => (l.Readings.Select(r => r.Latex).ToArray(), l.Chosen, l.Error))
                .ToList();
            panel.Dispatcher.BeginInvoke(new Action(() => panel.ShowReadings(readings)));

            if (errors.Count > 0) return;                   // l'image garde son dernier état bon
            Apply(Clean(spec));
        }

        private void Apply(CurveSpec clean)
        {
            var key = Key(clean);
            if (key == _appliedKey || _shape == null) return;

            _updating = true;
            try
            {
                var doc = _shape.Range.Document;
                var shape = Render(_shape, clean);
                CurveStore.Save(doc, clean, _log);
                shape.Select();
                _shape = shape;
                _appliedKey = key;
                _current = clean;
                _lastX = _lastY = double.NaN;
                _log($"curve_update n={clean.Functions.Count}");
            }
            catch (Exception ex) { _log("curve_update_error: " + ex.Message); }
            finally { _updating = false; }
        }

        private Word.InlineShape Render(Word.InlineShape existing, CurveSpec spec)
        {
            var items = CurveRenderer.Compile(spec, _culture, out _);
            var png = CurveRenderer.ToPng(CurveRenderer.Render(spec, items));
            var w = CurveRenderer.WindowFor(spec, items);
            return CurveInserter.Place(_app, existing, spec, png, Describe(items, w));
        }

        // Lignes vides retirées : couleurs = ordre des courbes tracées.
        private static CurveSpec Clean(CurveSpec spec)
        {
            var c = spec.Clone();
            c.Functions = new List<string>();
            c.Picks = new List<string>();
            for (int i = 0; i < spec.Functions.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(spec.Functions[i])) continue;
                c.Functions.Add(spec.Functions[i].Trim());
                c.Picks.Add(spec.PickOf(i));
            }
            return c;
        }

        // Ce qui change le dessin (la largeur, elle, vient de l'image).
        private static string Key(CurveSpec spec)
        {
            var c = spec.Clone();
            c.WidthCm = 0;
            return c.ToXml();
        }

        // ── Position : à droite de l'image, à gauche faute de place ────

        private void Follow()
        {
            var p = _panel;
            if (p == null || _shape == null) { _follow.Stop(); return; }
            try
            {
                var front = GetAncestor(GetForegroundWindow(), GA_ROOTOWNER);
                if (_wordHwnd == IntPtr.Zero || IsIconic(_wordHwnd) || (front != _wordHwnd && front != _panelHwnd))
                { HidePanel(p); return; }                   // Word au second plan

                var win = _shape.Range.Document.ActiveWindow;
                if (GetAncestor(new IntPtr(win.Hwnd), GA_ROOT) != _wordHwnd) { HidePanel(p); return; }
                win.GetPoint(out int l, out int t, out int w, out int h, _shape.Range);
                var doc = DocumentArea(_wordHwnd);
                if (h <= 0 || t + h < doc.Top || t > doc.Bottom || l + w < doc.Left || l > doc.Right)
                { HidePanel(p); return; }                   // image hors de la page visible
                _imgL = l; _imgT = t; _imgW = w; _imgH = h; _imgKnown = true;

                double scale = CaretScreenPositionReader.GetDpiScale();
                double panelW = CurvePanel.PanelWidth * scale;
                double panelH = Math.Max(p.HeightDip, 200) * scale;
                double x = l + w + GapPx;
                if (x + panelW > doc.Right) x = l - GapPx - panelW;       // pas la place à droite
                if (x < doc.Left) x = doc.Right - panelW - GapPx;         // ni à gauche : par-dessus
                double y = Math.Max(t, doc.Top + 4);
                if (y + panelH > doc.Bottom - 4) y = Math.Max(doc.Top + 4, doc.Bottom - 4 - panelH);

                double xd = x / scale, yd = y / scale;
                if (_panelShown && Math.Abs(xd - _lastX) < 0.5 && Math.Abs(yd - _lastY) < 0.5) return;
                _lastX = xd; _lastY = yd; _panelShown = true;
                p.Dispatcher.BeginInvoke(new Action(() =>
                {
                    p.Left = xd; p.Top = yd;
                    if (!p.IsVisible) p.Show();
                }));
            }
            catch { HidePanel(p); }                         // image supprimée, fenêtre fermée…
        }

        private void HidePanel(CurvePanel p)
        {
            _imgKnown = false;
            if (!_panelShown) return;
            _panelShown = false;
            _lastX = _lastY = double.NaN;
            p.Dispatcher.BeginInvoke(new Action(() => { if (p.IsVisible) p.Hide(); }));
        }

        // Zone du document (fenêtre « _WwG » de Word), à défaut toute la fenêtre.
        private static RECT DocumentArea(IntPtr word)
        {
            RECT r;
            var pane = IntPtr.Zero;
            EnumChildWindows(word, (h, _) =>
            {
                var cls = new System.Text.StringBuilder(32);
                GetClassName(h, cls, cls.Capacity);
                if (cls.ToString() == "_WwG" && IsWindowVisible(h)) { pane = h; return false; }
                return true;
            }, IntPtr.Zero);
            GetWindowRect(pane != IntPtr.Zero ? pane : word, out r);
            return r;
        }

        // ── Zoom du repère à la molette ────────────────────────────────

        private void InstallWheel()
        {
            if (_mouseHook != IntPtr.Zero) return;
            _mouseProc = MouseProc;
            _mouseHook = SetWindowsHookEx(WH_MOUSE, _mouseProc, IntPtr.Zero, GetCurrentThreadId());
        }

        private void RemoveWheel()
        {
            if (_mouseHook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        // Molette au-dessus de l'image sélectionnée (hors Ctrl, qui reste le zoom de Word) :
        // consommée (la page ne défile pas) et cumulée pour ApplyZoom.
        private IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0 && wParam.ToInt32() == WM_MOUSEWHEEL && _imgKnown && _panel != null
                    && (GetKeyState(VK_CONTROL) & 0x8000) == 0)
                {
                    var m = (MOUSEHOOKSTRUCTEX)Marshal.PtrToStructure(lParam, typeof(MOUSEHOOKSTRUCTEX));
                    if (m.X >= _imgL && m.X < _imgL + _imgW && m.Y >= _imgT && m.Y < _imgT + _imgH)
                    {
                        _zoomDelta += (short)((m.MouseData >> 16) & 0xFFFF);
                        _zoomX = m.X; _zoomY = m.Y;
                        if (!_zoomTimer.IsEnabled) _zoomTimer.Start();
                        return new IntPtr(1);
                    }
                }
            }
            catch { }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        // Cran vers l'avant = on se rapproche (×0,8 par cran), autour du point visé.
        private void ApplyZoom()
        {
            int delta = _zoomDelta;
            _zoomDelta = 0;
            var p = _panel;
            if (delta == 0 || p == null || _current == null || _imgW <= 0 || _imgH <= 0) return;
            try
            {
                var items = CurveRenderer.Compile(_current, _culture, out _);
                var w = CurveRenderer.WindowFor(_current, items);
                double k = Math.Pow(0.8, delta / 120.0);
                var (fx, fy) = CurveRenderer.PlotFraction((_zoomX - _imgL) / (double)_imgW, (_zoomY - _imgT) / (double)_imgH);
                double ax = w.XMin + fx * (w.XMax - w.XMin), ay = w.YMin + fy * (w.YMax - w.YMin);
                double x0 = ax - (ax - w.XMin) * k, x1 = ax + (w.XMax - ax) * k;
                double y0 = ay - (ay - w.YMin) * k, y1 = ay + (w.YMax - ay) * k;
                if (!(x1 - x0 > 1e-6) || !(y1 - y0 > 1e-6) || x1 - x0 > 1e6 || y1 - y0 > 1e6) return;
                double sx = x1 - x0, sy = y1 - y0;
                x0 = Round(x0, sx); x1 = Round(x1, sx);
                y0 = Round(y0, sy); y1 = Round(y1, sy);

                var spec = _current.Clone();
                spec.XMin = x0; spec.XMax = x1; spec.YMin = y0; spec.YMax = y1;
                p.Dispatcher.BeginInvoke(new Action(() => p.SetAxes(x0, x1, y0, y1)));
                Apply(spec);
            }
            catch (Exception ex) { _log("curve_zoom_error: " + ex.Message); }
        }

        // Bornes lisibles dans les champs : 3 chiffres significatifs à l'échelle de l'intervalle.
        private static double Round(double v, double span)
        {
            double step = Math.Pow(10, Math.Floor(Math.Log10(span)) - 2);
            return Math.Round(Math.Round(v / step) * step, 10);
        }

        // ── Divers ─────────────────────────────────────────────────────

        private EngineCulture Culture() =>
            SettingsStore.Current.ToEngineCulture().WithPhysChem(DocumentMode.IsPhysChemActive(_app));

        private static string Describe(IReadOnlyList<CurveRenderer.Item> items, Engine.Plot.PlotWindow w) =>
            items.Count == 0
                ? Strings.CurveWindowTitle
                : Strings.CurveDescribe + string.Join(Strings.CurveAnd, items.Select(i => "y = " + i.Function.Latex))
                  + " [" + CurveRenderer.Fmt(w.XMin) + " ; " + CurveRenderer.Fmt(w.XMax) + "]";

        // Source tapée de l'équation la plus proche qui se trace : sous le caret,
        // juste derrière, sinon la dernière du paragraphe.
        private string FunctionNearCaret()
        {
            try
            {
                var doc = _app.ActiveDocument;
                var sel = _app.Selection;
                var resolver = _resolver();
                if (doc == null || sel == null || resolver == null) return null;
                var candidates = new List<Word.OMath>();
                foreach (Word.OMath om in sel.OMaths) candidates.Add(om);
                var (behind, _) = resolver.ResolveBehindCaret(doc, sel);
                if (behind != null) candidates.Add(behind);
                var para = sel.Paragraphs[1].Range;
                for (int i = para.OMaths.Count; i >= 1; i--) candidates.Add(para.OMaths[i]);

                foreach (var om in candidates)
                {
                    var src = resolver.ResolveAt(doc, om)?.Steno;
                    if (!string.IsNullOrWhiteSpace(src) && ForestEngine.TryCompileFunction(src, out _, out _, SettingsStore.Current.ToEngineCulture()))
                        return src.Trim();
                }
            }
            catch (Exception ex) { _log("curve: équation sous le caret KO: " + ex.Message); }
            return null;
        }

        // ── Win32 ──────────────────────────────────────────────────────

        private const uint GA_ROOT = 2, GA_ROOTOWNER = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        private const int WH_MOUSE = 7, WM_MOUSEWHEEL = 0x020A, VK_CONTROL = 0x11;

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEHOOKSTRUCTEX
        {
            public int X, Y;
            public IntPtr Hwnd;
            public uint HitTestCode;
            public IntPtr ExtraInfo;
            public uint MouseData;
        }

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
    }
}
