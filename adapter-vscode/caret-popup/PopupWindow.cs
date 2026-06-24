using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MathCursor.UI;          // MixedLatexRenderer — rendu réutilisé de l'adapter Word
using WpfMath.Controls;

namespace MathCursor.CaretPopup
{
    // Popup persistante au caret, modèle « comme Word » :
    //  - ne prend JAMAIS le focus (WS_EX_NOACTIVATE) ; le caret reste dans VSCode ;
    //  - vit entre les SHOW (le process ne meurt pas) ; suit le caret à chaque SHOW ;
    //  - hook clavier global actif UNIQUEMENT quand visible (↑↓ nav, Entrée/Tab
    //    commit, Échap dismiss ; le reste passe à l'éditeur → refresh live) ;
    //  - nav-mode opt-in : pas de surbrillance avant la 1re flèche.
    internal sealed class PopupWindow : Window
    {
        public event Action<int> Commit;   // index choisi (Entrée/Tab/clic)
        public event Action Dismiss;        // Échap

        private readonly StackPanel _rows;
        private string[] _candidates = Array.Empty<string>();
        private int _sel;
        private bool _nav;
        private bool _dark = true;

        private IntPtr _hwnd;
        private IntPtr _ownerFg;             // VSCode (garde le focus)
        private IntPtr _hook;
        private HookProc _hookProc;
        private DispatcherTimer _fgTimer;
        private bool _hasPos;

        public PopupWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Opacity = 0;                     // créée cachée

            _rows = new StackPanel { Orientation = Orientation.Vertical };
            Content = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(5),
                Child = _rows,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.35 }
            };
        }

        // ── API pilotée par Program (thread UI) ───────────────────────────────

        // colDelta = nb de caractères entre le DÉBUT de la formule et le caret ;
        // charWidth = largeur d'un caractère (DIP, police monospace de l'éditeur).
        // → on ancre la popup au début de la formule, pas au caret.
        public void ShowCandidates(string[] candidates, bool dark, bool reposition, int colDelta, double charWidth)
        {
            EnsureSource();
            if (_ownerFg == IntPtr.Zero)
            {
                IntPtr fg = CaretReader.GetForegroundWindow();
                if (fg != _hwnd && fg != IntPtr.Zero) _ownerFg = fg;
            }

            bool wasVisible = IsVisible && Opacity > 0;
            // Refresh : on préserve nav + sélection (clampée) ; sinon repart à 0.
            if (!wasVisible || !_nav) { _sel = 0; _nav = false; }

            _candidates = candidates ?? Array.Empty<string>();
            _dark = dark;
            if (_sel > _candidates.Length - 1) _sel = Math.Max(0, _candidates.Length - 1);

            ApplyTheme();
            BuildRows();

            // Position : caret courant (MSAA, pixels physiques → DIP).
            // On ne (re)positionne QUE si la zone a changé (reposition) ou si on
            // n'a pas encore de position → sinon on fige (pas de dérive en frappe).
            if ((reposition || !_hasPos)
                && CaretReader.TryGet(_ownerFg, out double px, out double py, out double ph))
            {
                var src = PresentationSource.FromVisual(this);
                var p = new Point(px, py + ph);
                if (src?.CompositionTarget != null)
                    p = src.CompositionTarget.TransformFromDevice.Transform(p);
                // Recule au début de la formule (caret − colDelta caractères).
                Left = Math.Max(0, p.X - colDelta * charWidth);
                Top = p.Y; _hasPos = true;
            }

            if (!_hasPos) { Left = 200; Top = 200; }

            Opacity = _dark ? 0.97 : 1.0;
            if (!IsVisible) Show();

            InstallHook();
            StartFgTimer();
        }

        public void HidePopup()
        {
            if (Opacity == 0 && !IsVisible) { UninstallHook(); return; }
            Opacity = 0;
            Hide();
            UninstallHook();
            _fgTimer?.Stop();
            _nav = false;
        }

        // ── rendu ─────────────────────────────────────────────────────────────

        private SolidColorBrush _cardBg, _cardBorder, _selBg, _fg, _fgDim;

        private void ApplyTheme()
        {
            _cardBg = Brush(_dark ? "#252526" : "#FBFBFB");
            _cardBorder = Brush(_dark ? "#454545" : "#C8C8C8");
            _selBg = Brush(_dark ? "#094771" : "#D6E8FF");
            _fg = Brush(_dark ? "#D4D4D4" : "#1E1E1E");
            _fgDim = Brush(_dark ? "#7C7C7C" : "#9A9A9A");
            if (Content is Border b) { b.Background = _cardBg; b.BorderBrush = _cardBorder; }
        }

        private void BuildRows()
        {
            _rows.Children.Clear();
            for (int i = 0; i < _candidates.Length; i++)
            {
                var latex = _candidates[i] ?? "";

                var visual = MixedLatexRenderer.Render(latex, 18);
                ApplyForeground(visual, _fg);

                // LaTeX généré, petit et discret SOUS le rendu.
                var code = new TextBlock
                {
                    Text = latex,
                    FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"),
                    FontSize = 10.5,
                    Foreground = _fgDim,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

                var stack = new StackPanel { Orientation = Orientation.Vertical };
                stack.Children.Add(visual);
                stack.Children.Add(code);

                var cell = new Border
                {
                    MinWidth = 150,
                    Padding = new Thickness(14, 9, 14, 9),   // plus aéré
                    Margin = new Thickness(0, 1, 0, 1),
                    CornerRadius = new CornerRadius(4),
                    Background = (_nav && i == _sel) ? _selBg : Brushes.Transparent,
                    Child = stack,
                };
                int idx = i;
                cell.MouseEnter += (_, __) => { _sel = idx; _nav = true; Highlight(); };
                cell.PreviewMouseLeftButtonUp += (_, __) => Commit?.Invoke(idx);
                _rows.Children.Add(cell);
            }
        }

        private void Highlight()
        {
            for (int i = 0; i < _rows.Children.Count; i++)
                if (_rows.Children[i] is Border cell)
                    cell.Background = (_nav && i == _sel) ? _selBg : Brushes.Transparent;
        }

        private void Move(int delta)
        {
            if (_candidates.Length == 0) return;
            // 1re flèche : on ne bouge pas, on RÉVÈLE juste la surbrillance sur la
            // sélection courante (le 1er candidat). Les flèches suivantes déplacent.
            if (!_nav) { _nav = true; Highlight(); return; }
            _sel = Math.Max(0, Math.Min(_candidates.Length - 1, _sel + delta));
            Highlight();
        }

        // ── hook clavier (actif seulement quand visible) ──────────────────────

        private void EnsureSource()
        {
            if (_hwnd != IntPtr.Zero) return;
            // Force la création du HWND sans activer la fenêtre.
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            _hwnd = helper.EnsureHandle();
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        private void InstallHook()
        {
            if (_hook != IntPtr.Zero) return;
            _hookProc = HookCallback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        }

        private void UninstallHook()
        {
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                && Opacity > 0)
            {
                int vk = Marshal.ReadInt32(lParam, 0);
                int flags = Marshal.ReadInt32(lParam, 8);
                bool alt = (flags & LLKHF_ALTDOWN) != 0;
                bool ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                if (!alt && !ctrl)
                {
                    switch (vk)
                    {
                        case VK_UP: Move(-1); return (IntPtr)1;
                        case VK_DOWN: Move(+1); return (IntPtr)1;
                        case VK_RETURN:
                        case VK_TAB:
                            int sel = _sel;
                            HidePopup();
                            Commit?.Invoke(sel);
                            return (IntPtr)1;
                        case VK_ESCAPE:
                            HidePopup();
                            Dismiss?.Invoke();
                            return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // ── fermeture si VSCode n'est plus au premier plan ────────────────────

        private void StartFgTimer()
        {
            if (_fgTimer == null)
            {
                _fgTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _fgTimer.Tick += (_, __) =>
                {
                    IntPtr fg = CaretReader.GetForegroundWindow();
                    if (Opacity > 0 && fg != _ownerFg && fg != _hwnd) HidePopup();
                };
            }
            _fgTimer.Start();
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static void ApplyForeground(UIElement el, Brush fg)
        {
            switch (el)
            {
                case FormulaControl fc: fc.Foreground = fg; break;
                case TextBlock tb: tb.Foreground = fg; break;
                case Border b when b.Child is UIElement bc: ApplyForeground(bc, fg); break;
                case Panel p: foreach (UIElement ch in p.Children) ApplyForeground(ch, fg); break;
                case ContentControl cc when cc.Content is UIElement cv: ApplyForeground(cv, fg); break;
            }
        }

        private static SolidColorBrush Brush(string hex) =>
            (SolidColorBrush)new BrushConverter().ConvertFromString(hex);

        // ── interop ───────────────────────────────────────────────────────────
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        private const int LLKHF_ALTDOWN = 0x20, VK_CONTROL = 0x11;
        private const int VK_RETURN = 0x0D, VK_TAB = 0x09, VK_ESCAPE = 0x1B, VK_UP = 0x26, VK_DOWN = 0x28;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern short GetKeyState(int nVirtKey);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
