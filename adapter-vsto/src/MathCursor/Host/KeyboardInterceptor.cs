using System;
using System.Runtime.InteropServices;

namespace MathCursor.Host
{
    /// <summary>
    /// Intercepte la touche Tab via un hook WH_KEYBOARD thread-local
    /// (celui du thread UI Word). Au contraire d'un hook _LL global, celui-ci
    /// ne tourne QUE dans le processus Word → zéro impact sur d'autres apps.
    ///
    /// Le handler OnTabPressed renvoie true pour "consommer" le Tab (conversion
    /// effectuée) ou false pour le laisser passer (pas de math détectée →
    /// comportement Tab normal : tab char / nav table / indent liste).
    /// </summary>
    public sealed class KeyboardInterceptor : IDisposable
    {
        private const int WH_KEYBOARD = 2;
        private const int HC_ACTION = 0;
        private const int VK_TAB = 0x09;
        private const int VK_SHIFT = 0x10;

        /// <summary>Appelé sur Tab down (sans Shift). Retourne true = consommer, false = laisser passer.</summary>
        public Func<bool> OnTabPressed { get; set; }

        private IntPtr _hookHandle;
        private KeyboardHookProc _proc; // référence GC-stable

        private delegate IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam);

        public void Install()
        {
            if (_hookHandle != IntPtr.Zero) return; // déjà installé
            _proc = HookCallback;
            var threadId = GetCurrentThreadId();
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD, _proc, IntPtr.Zero, threadId);
            if (_hookHandle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    "SetWindowsHookEx a échoué, code: " + err);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == HC_ACTION)
            {
                int vkCode = wParam.ToInt32();
                long lparam = lParam.ToInt64();
                // Bit 31 du lParam : 0 = key pressed (down), 1 = released
                bool keyDown = (lparam & 0x80000000L) == 0;
                bool shiftDown = (GetKeyState(VK_SHIFT) & 0x8000) != 0;

                if (vkCode == VK_TAB && keyDown && !shiftDown)
                {
                    var handler = OnTabPressed;
                    if (handler != null)
                    {
                        try
                        {
                            if (handler())
                            {
                                return new IntPtr(1); // consommé, Word ne voit pas le Tab
                            }
                        }
                        catch
                        {
                            // Jamais remonter d'exception depuis le hook : Windows décroche
                        }
                    }
                }
            }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
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
    }
}
