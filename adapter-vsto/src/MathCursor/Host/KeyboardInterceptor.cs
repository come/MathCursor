using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MathCursor.Host
{
    /// <summary>
    /// Intercepte les touches via un hook WH_KEYBOARD thread-local
    /// (celui du thread UI Word). Hook NON global → zéro impact sur d'autres apps.
    ///
    /// Touches gérées :
    /// - Tab (sans Shift)  : OnTabPressed
    /// - Up                : OnUpPressed
    /// - Down              : OnDownPressed
    /// - Escape            : OnEscapePressed
    /// Chaque handler retourne true pour "consommer" la touche, false pour la
    /// laisser passer.
    /// </summary>
    public sealed class KeyboardInterceptor : IDisposable
    {
        private const int WH_KEYBOARD = 2;
        private const int HC_ACTION = 0;
        private const int VK_TAB = 0x09;
        private const int VK_RETURN = 0x0D;
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_SPACE = 0x20;
        private const int VK_BACK = 0x08;
        private const int VK_DELETE = 0x2E;
        private const int VK_MENU = 0x12;
        private const int VK_LEFT = 0x25;
        private const int VK_UP = 0x26;
        private const int VK_RIGHT = 0x27;
        private const int VK_DOWN = 0x28;

        public Func<bool> OnTabPressed { get; set; }
        public Func<bool> OnEnterPressed { get; set; }
        public Func<bool> OnUpPressed { get; set; }
        public Func<bool> OnDownPressed { get; set; }
        public Func<bool> OnLeftPressed { get; set; }
        public Func<bool> OnRightPressed { get; set; }
        public Func<bool> OnEscapePressed { get; set; }
        public Func<bool> OnCtrlSpacePressed { get; set; }

        /// <summary>
        /// Observation NON-consommante d'une frappe « texte » (lettres,
        /// chiffres, opérateurs OEM, espace, Backspace, Delete — hors
        /// Ctrl/Alt). Sert au debounce de l'auto-détection NER (ADR
        /// 2026-06-10-Feat-ner-auto-detection-debounce) : la touche passe
        /// toujours à Word, on ne fait que réarmer un timer.
        /// </summary>
        public Action OnTextKeyTyped { get; set; }

        private IntPtr _hookHandle;
        private KeyboardHookProc _proc; // référence GC-stable

        private delegate IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam);

        public void Install()
        {
            if (_hookHandle != IntPtr.Zero) return;
            _proc = HookCallback;
            var threadId = GetCurrentThreadId();
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD, _proc, IntPtr.Zero, threadId);
            if (_hookHandle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException("SetWindowsHookEx a échoué, code: " + err);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == HC_ACTION)
            {
                int vkCode = wParam.ToInt32();
                long lparam = lParam.ToInt64();
                bool keyDown = (lparam & 0x80000000L) == 0;

                if (keyDown)
                {
                    bool shiftDown = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
                    bool ctrlDown = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                    Func<bool> handler = null;

                    if (vkCode == VK_SPACE && ctrlDown) handler = OnCtrlSpacePressed;
                    else if (vkCode == VK_TAB && !shiftDown) handler = OnTabPressed;
                    else if (vkCode == VK_RETURN && !shiftDown) handler = OnEnterPressed;
                    else if (vkCode == VK_UP) handler = OnUpPressed;
                    else if (vkCode == VK_DOWN) handler = OnDownPressed;
                    else if (vkCode == VK_LEFT) handler = OnLeftPressed;
                    else if (vkCode == VK_RIGHT) handler = OnRightPressed;
                    else if (vkCode == VK_ESCAPE) handler = OnEscapePressed;

                    if (handler != null)
                    {
                        try
                        {
                            bool consumed = handler();
                            LogHook($"vk={vkCode:X} {(consumed ? "consume" : "passthru")}");
                            if (consumed) return new IntPtr(1);
                        }
                        catch (Exception ex)
                        {
                            LogHook($"vk={vkCode:X} exception: {ex.Message}");
                        }
                    }
                    else if (!ctrlDown && IsTextKey(vkCode))
                    {
                        bool altDown = (GetKeyState(VK_MENU) & 0x8000) != 0;
                        if (!altDown)
                        {
                            try { OnTextKeyTyped?.Invoke(); } catch { }
                        }
                    }
                }
            }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        /// <summary>Touche susceptible de MODIFIER le texte du ¶ courant :
        /// lettres, chiffres (+ pavé num.), opérateurs OEM (+ - * / ^ = ( ) …),
        /// espace, Backspace, Delete.</summary>
        private static bool IsTextKey(int vk)
        {
            if (vk == VK_SPACE || vk == VK_BACK || vk == VK_DELETE) return true;
            if (vk >= 0x30 && vk <= 0x39) return true;   // 0-9
            if (vk >= 0x41 && vk <= 0x5A) return true;   // A-Z
            if (vk >= 0x60 && vk <= 0x6F) return true;   // pavé num. + * + - . /
            if (vk >= 0xBA && vk <= 0xC0) return true;   // OEM ; = , - . / `
            if (vk >= 0xDB && vk <= 0xDF) return true;   // OEM [ \ ] ' #
            if (vk == 0xE2) return true;                 // OEM 102 (< > sur AZERTY)
            return false;
        }

        public void Dispose()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
            _proc = null;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, KeyboardHookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private static void LogHook(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} hook {message}{Environment.NewLine}");
            }
            catch { /* jamais d'exception depuis le logging */ }
        }
    }
}
