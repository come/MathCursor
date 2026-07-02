// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
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
using System.Runtime.InteropServices;

namespace MathCursor.Host.Caret
{
    /// <summary>
    /// Lit la position écran du caret Word via Win32 <c>GetGUIThreadInfo</c>
    /// (= la fenêtre qui possède le caret + son rect en coordonnées client).
    /// Convertit en coordonnées DIP (compte du DPI scale) pour placer la
    /// popup WPF au bon endroit.
    ///
    /// <para>Pourquoi pas GetFocus() : dès qu'un OMath existe dans le doc,
    /// Word multiplie les sous-fenêtres (éditeur math, pane texte) et les
    /// deux HWND peuvent diverger, ce qui décalait la popup.</para>
    ///
    /// <para>P2.17 du refactor archi. Toute la dépendance Win32 isolée ici.</para>
    /// </summary>
    internal static class CaretScreenPositionReader
    {
        /// <summary>
        /// Rect du caret GDI en DIP : (x, top, bottom). Contrairement à la
        /// boîte de ligne Word (GetPoint, qui inclut interligne + espace de
        /// paragraphe), le caret a exactement la hauteur du TEXTE → son bas
        /// est l'ancre « collée sous la ligne ». False si pas de caret
        /// (sélection non réduite, fenêtre sans focus...).
        /// </summary>
        public static bool TryReadRect(out double x, out double top, out double bottom)
        {
            x = top = bottom = 0;
            try
            {
                var gti = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf(typeof(GUITHREADINFO)) };
                if (!GetGUIThreadInfo(0, ref gti) || gti.hwndCaret == IntPtr.Zero) return false;
                var pt = new POINT { X = gti.rcCaret.Left, Y = gti.rcCaret.Top };
                var pb = new POINT { X = gti.rcCaret.Left, Y = gti.rcCaret.Bottom };
                ClientToScreen(gti.hwndCaret, ref pt);
                ClientToScreen(gti.hwndCaret, ref pb);
                double scale = GetDpiScale();
                x = pt.X / scale;
                top = pt.Y / scale;
                bottom = pb.Y / scale;
                return bottom > top;
            }
            catch { return false; }
        }

        /// <summary>
        /// Retourne (x, y) en coordonnées DIP (= scaled by DPI). Si la lecture
        /// échoue, retourne (200, 200) — fallback raisonnable au coin du doc.
        /// </summary>
        public static (double x, double y) Read()
        {
            try
            {
                var gti = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf(typeof(GUITHREADINFO)) };
                if (!GetGUIThreadInfo(0, ref gti) || gti.hwndCaret == IntPtr.Zero)
                {
                    return (200, 200);
                }
                var pt = new POINT { X = gti.rcCaret.Left, Y = gti.rcCaret.Bottom };
                ClientToScreen(gti.hwndCaret, ref pt);
                double scale = GetDpiScale();
                return (pt.X / scale, pt.Y / scale + 4);
            }
            catch
            {
                return (200, 200);
            }
        }

        /// <summary>Facteur DPI écran→DIP (96 = 1.0). Exposé pour les callers
        /// qui convertissent d'autres coordonnées pixels (ex. Word GetPoint).</summary>
        internal static double GetDpiScale()
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

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public uint cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    }
}
