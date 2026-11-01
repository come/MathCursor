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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MathCursor.Engine;
using MathCursor.Engine.Plot;
using MathCursor.UI;

namespace MathCursor.Host.Curves
{
    /// <summary>
    /// Dessine un repère et ses courbes en image (WPF, sans Word). Deux styles :
    /// propre (axes gradués, grille légère) et main levée (traits tremblés, police
    /// manuscrite). Le calcul des points vient du moteur (<see cref="Sampler"/>).
    /// ADR 2026-10-31-Feat-insert-curves.
    /// </summary>
    internal static class CurveRenderer
    {
        /// <summary>Taille logique de l'image (DIP). Rendue en 2× pour rester nette.</summary>
        public const double Width = 500, Height = 340;
        private const double Scale = 2;
        private const double PadL = 34, PadR = 16, PadT = 14, PadB = 26;

        /// <summary>Une courbe à tracer : fonction compilée (null si la saisie a échoué).</summary>
        internal sealed class Item
        {
            public CompiledFunction Function;
            public string Color;
        }

        /// <summary>Lectures traçables d'une ligne (ordre de la popup) et celle retenue ;
        /// <see cref="Error"/> si aucune ne se trace.</summary>
        internal sealed class Row
        {
            public IReadOnlyList<CompiledFunction> Readings = Array.Empty<CompiledFunction>();
            public int Chosen;
            public string Error;
        }

        /// <summary>Compile les fonctions de la spec (même culture que la popup), chacune sur
        /// la lecture choisie (<see cref="CurveSpec.Picks"/>, sinon celle de tête).
        /// Les échecs sont rendus en clair dans <paramref name="errors"/> (index → raison).</summary>
        public static List<Item> Compile(CurveSpec spec, EngineCulture culture, out Dictionary<int, string> errors) =>
            Compile(spec, culture, out errors, out _);

        public static List<Item> Compile(CurveSpec spec, EngineCulture culture, out Dictionary<int, string> errors, out List<Row> lines)
        {
            errors = new Dictionary<int, string>();
            lines = new List<Row>();
            var items = new List<Item>();
            for (int i = 0; i < spec.Functions.Count; i++)
            {
                var line = new Row();
                lines.Add(line);
                var src = spec.Functions[i];
                if (string.IsNullOrWhiteSpace(src)) continue;
                line.Readings = ForestEngine.FunctionReadings(src, culture);
                if (line.Readings.Count == 0)
                {
                    ForestEngine.TryCompileFunction(src, out _, out var err, culture);
                    errors[i] = line.Error = err ?? "pas une fonction";
                    continue;
                }
                var pick = spec.PickOf(i);
                line.Chosen = Math.Max(0, line.Readings.ToList().FindIndex(r => r.Latex == pick));
                items.Add(new Item { Function = line.Readings[line.Chosen], Color = CurveSpec.ColorOf(i) });
            }
            return items;
        }

        /// <summary>Point de l'image (fractions 0..1 de sa largeur et de sa hauteur, depuis le
        /// coin haut gauche) → position dans la zone tracée (fractions 0..1, x vers la droite,
        /// y vers le HAUT), bornée au cadre. Sert au zoom à la molette autour du pointeur.</summary>
        public static (double Fx, double Fy) PlotFraction(double u, double v)
        {
            double fx = (u * Width - PadL) / (Width - PadL - PadR);
            double fy = 1 - (v * Height - PadT) / (Height - PadT - PadB);
            return (Math.Max(0, Math.Min(1, fx)), Math.Max(0, Math.Min(1, fy)));
        }

        /// <summary>Fenêtre effective : celle de la spec, complétée automatiquement.</summary>
        public static PlotWindow WindowFor(CurveSpec spec, IReadOnlyList<Item> items)
        {
            var fs = items.Select(i => i.Function).ToList();
            var (ax, bx) = Sampler.AutoX(fs);
            double x0 = spec.XMin ?? ax, x1 = spec.XMax ?? bx;
            if (!(x1 > x0)) { x0 = ax; x1 = bx; }
            var (ay, by) = Sampler.AutoY(fs, x0, x1);
            double y0 = spec.YMin ?? ay, y1 = spec.YMax ?? by;
            if (!(y1 > y0)) { y0 = ay; y1 = by; }
            return new PlotWindow(x0, x1, y0, y1);
        }

        /// <summary>Image du repère avec ses courbes.</summary>
        public static BitmapSource Render(CurveSpec spec, IReadOnlyList<Item> items)
        {
            var w = WindowFor(spec, items);
            bool hand = spec.Style == CurveStyle.Hand;
            var labels = items.Select(it => (it, Label(it, hand))).ToList();

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, Width, Height));
                var plot = new Rect(PadL, PadT, Width - PadL - PadR, Height - PadT - PadB);
                var map = new Mapper(w, plot);
                var jitter = hand ? new Jitter(7) : null;

                if (spec.Grid && !hand) DrawGrid(dc, map, w);
                DrawAxes(dc, map, w, hand, jitter);

                dc.PushClip(new RectangleGeometry(plot));
                foreach (var it in items)
                {
                    var pen = new Pen(Brush(it.Color), hand ? 2.6 : 2.2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    foreach (var line in Sampler.Sample(it.Function, w))
                    {
                        var pts = line.Select(p => map.ToScreen(p.X, p.Y)).ToList();
                        if (jitter != null) pts = jitter.Apply(pts);
                        dc.DrawGeometry(null, pen, Polyline(pts));
                    }
                }
                dc.Pop();

                PlaceLabels(dc, map, w, plot, labels);
            }

            var bmp = new RenderTargetBitmap((int)(Width * Scale), (int)(Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        public static byte[] ToPng(BitmapSource bmp)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }

        // ── Repère ─────────────────────────────────────────────────────

        private sealed class Mapper
        {
            private readonly PlotWindow _w; private readonly Rect _r;
            public Mapper(PlotWindow w, Rect r) { _w = w; _r = r; }
            public Point ToScreen(double x, double y) => new Point(
                _r.Left + (x - _w.XMin) / (_w.XMax - _w.XMin) * _r.Width,
                _r.Bottom - (y - _w.YMin) / (_w.YMax - _w.YMin) * _r.Height);
        }

        private static void DrawGrid(DrawingContext dc, Mapper map, PlotWindow w)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(228, 228, 228)), 0.8);
            foreach (var t in Sampler.Ticks(w.XMin, w.XMax))
                dc.DrawLine(pen, map.ToScreen(t, w.YMin), map.ToScreen(t, w.YMax));
            foreach (var t in Sampler.Ticks(w.YMin, w.YMax, 8))
                dc.DrawLine(pen, map.ToScreen(w.XMin, t), map.ToScreen(w.XMax, t));
        }

        private static void DrawAxes(DrawingContext dc, Mapper map, PlotWindow w, bool hand, Jitter jitter)
        {
            var ink = new SolidColorBrush(Color.FromRgb(40, 40, 40));
            var pen = new Pen(ink, hand ? 1.8 : 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            // axes en 0 s'il est dans le cadre, sinon au bord
            double ax = w.YMin <= 0 && 0 <= w.YMax ? 0 : w.YMin;
            double ay = w.XMin <= 0 && 0 <= w.XMax ? 0 : w.XMin;
            var xa = map.ToScreen(w.XMin, ax); var xb = map.ToScreen(w.XMax, ax);
            var ya = map.ToScreen(ay, w.YMin); var yb = map.ToScreen(ay, w.YMax);
            xb.X += 8; yb.Y -= 8;
            Line(dc, pen, xa, xb, jitter); Line(dc, pen, ya, yb, jitter);
            Arrow(dc, pen, xb, 0); Arrow(dc, pen, yb, -Math.PI / 2);

            var font = hand ? HandFont : CleanFont;
            Text(dc, "x", font, 12, ink, new Point(xb.X - 8, xb.Y - 19), italic: !hand);   // au-dessus : la dernière graduation est dessous
            Text(dc, "y", font, 12, ink, new Point(yb.X - 13, yb.Y - 4), italic: !hand);

            var tick = new Pen(ink, 1);
            var gray = new SolidColorBrush(Color.FromRgb(90, 90, 90));
            foreach (var t in Sampler.Ticks(w.XMin, w.XMax))
            {
                if (t == ay && ay == 0) continue;
                var p = map.ToScreen(t, ax);
                dc.DrawLine(tick, new Point(p.X, p.Y - 3), new Point(p.X, p.Y + 3));
                Text(dc, Fmt(t), font, 10, gray, new Point(p.X, p.Y + 5), center: true);
            }
            foreach (var t in Sampler.Ticks(w.YMin, w.YMax, 8))
            {
                if (t == ax && ax == 0) continue;
                var p = map.ToScreen(ay, t);
                dc.DrawLine(tick, new Point(p.X - 3, p.Y), new Point(p.X + 3, p.Y));
                Text(dc, Fmt(t), font, 10, gray, new Point(p.X - 5, p.Y - 7), right: true);
            }
            if (ax == 0 && ay == 0)
            {
                var o = map.ToScreen(0, 0);
                Text(dc, "0", font, 10, gray, new Point(o.X - 5, o.Y + 3), right: true);
            }
        }

        private static void Arrow(DrawingContext dc, Pen pen, Point tip, double angle)
        {
            double a = 0.45, l = 7;
            dc.DrawLine(pen, tip, new Point(tip.X - l * Math.Cos(angle - a), tip.Y - l * Math.Sin(angle - a)));
            dc.DrawLine(pen, tip, new Point(tip.X - l * Math.Cos(angle + a), tip.Y - l * Math.Sin(angle + a)));
        }

        private static void Line(DrawingContext dc, Pen pen, Point a, Point b, Jitter jitter)
        {
            if (jitter == null) { dc.DrawLine(pen, a, b); return; }
            dc.DrawGeometry(null, pen, Polyline(jitter.Apply(new List<Point> { a, b })));
        }

        private static StreamGeometry Polyline(IList<Point> pts)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
            }
            g.Freeze();
            return g;
        }

        // ── Noms des courbes ───────────────────────────────────────────

        // Nom rendu à part (WpfMath en style propre, police manuscrite en main levée),
        // posé près de la fin visible de sa courbe, au-dessus ou en dessous, sans
        // chevaucher les autres noms.
        private static BitmapSource Label(Item it, bool hand)
        {
            UIElement el;
            if (hand)
                el = new TextBlock { Text = "y = " + Linear(it.Function.Latex), FontFamily = HandFont, FontSize = 15 };
            else
                el = MixedLatexRenderer.Render("y = " + it.Function.Latex, 15);
            var host = new Border { Child = el, Background = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)), Padding = new Thickness(2, 0, 2, 0) };
            TextElement.SetForeground(host, Brush(it.Color));
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            host.UpdateLayout();
            var sz = host.DesiredSize;
            var bmp = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(sz.Width * Scale)), Math.Max(1, (int)Math.Ceiling(sz.Height * Scale)),
                96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            bmp.Render(host);
            bmp.Freeze();
            return bmp;
        }

        private static void PlaceLabels(DrawingContext dc, Mapper map, PlotWindow w, Rect plot, List<(Item It, BitmapSource Img)> labels)
        {
            var taken = new List<Rect>();
            foreach (var (it, img) in labels)
            {
                // dernier point visible de la courbe (en partant de la droite)
                Point? anchor = null;
                for (int i = 0; i <= 200 && anchor == null; i++)
                {
                    double x = w.XMax - (w.XMax - w.XMin) * (0.06 + 0.9 * i / 200);
                    double y = it.Function.Evaluate(x);
                    if (!double.IsNaN(y) && y >= w.YMin && y <= w.YMax) anchor = map.ToScreen(x, y);
                }
                if (anchor == null) continue;
                double lw = img.Width, lh = img.Height;
                var p = anchor.Value;
                Rect best = Rect.Empty;
                foreach (double dy in new[] { -lh - 6, 6, -2 * lh - 8, lh + 8 })
                {
                    var r = new Rect(Math.Min(p.X - lw * 0.6, plot.Right - lw), p.Y + dy, lw, lh);
                    r.Y = Math.Max(plot.Top, Math.Min(plot.Bottom - lh, r.Y));
                    r.X = Math.Max(plot.Left + 4, r.X);
                    if (!taken.Any(t => t.IntersectsWith(r))) { best = r; break; }
                    if (best.IsEmpty) best = r;
                }
                taken.Add(best);
                dc.DrawImage(img, best);
            }
        }

        // \sqrt{x} → √x, x^{2} → x², pour la police manuscrite (pas de WpfMath).
        private static string Linear(string latex)
        {
            var s = latex.Replace("\\sqrt{", "√(").Replace("\\frac{", "(").Replace("}{", ")/(")
                .Replace("\\left|", "|").Replace("\\right|", "|").Replace("\\times", "×").Replace("\\cdot", "·")
                .Replace("\\ln", "ln").Replace("\\sin", "sin").Replace("\\cos", "cos").Replace("\\tan", "tan")
                .Replace("\\pi", "π").Replace("\\", "");
            s = s.Replace("^{2}", "²").Replace("^{3}", "³").Replace("{", "(").Replace("}", ")");
            // √(x) → √x quand l'argument est une seule lettre ou un nombre
            s = System.Text.RegularExpressions.Regex.Replace(s, @"√\((\w)\)", "√$1");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\((\w)\)/\((\w+)\)", "$1/$2");
            return s;
        }

        // ── Utilitaires ────────────────────────────────────────────────

        private static readonly FontFamily CleanFont = new FontFamily("Cambria Math, Cambria, Segoe UI");
        private static readonly FontFamily HandFont = new FontFamily("Segoe Print, Comic Sans MS");

        private static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        /// <summary>Nombre à la française : virgule décimale, signe moins typographique.</summary>
        internal static string Fmt(double v)
        {
            var s = Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture).Replace('.', ',');
            return s.StartsWith("-") ? "−" + s.Substring(1) : s;
        }

        private static void Text(DrawingContext dc, string s, FontFamily font, double size, Brush brush, Point at,
            bool center = false, bool right = false, bool italic = false)
        {
            var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(font, italic ? FontStyles.Italic : FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                size, brush, Scale);
            double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
            dc.DrawText(ft, new Point(x, at.Y));
        }

        /// <summary>Tremblé « main levée » : la ligne est rééchantillonnée tous les 4 DIP et
        /// décalée perpendiculairement par un bruit lissé (somme de sinus à phases tirées
        /// une fois) : même dessin à chaque rendu.</summary>
        private sealed class Jitter
        {
            private readonly double[] _ph;
            public Jitter(int seed)
            {
                var r = new Random(seed);
                _ph = Enumerable.Range(0, 3).Select(_ => r.NextDouble() * 2 * Math.PI).ToArray();
            }

            public List<Point> Apply(List<Point> pts)
            {
                var res = new List<Point>();
                double s = 0;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var a = pts[i]; var b = pts[i + 1];
                    double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
                    int n = Math.Max(1, (int)(len / 4));
                    double nx = len > 0 ? -dy / len : 0, ny = len > 0 ? dx / len : 0;
                    for (int k = 0; k < n; k++)
                    {
                        double t = (double)k / n, d = Noise(s + t * len);
                        res.Add(new Point(a.X + dx * t + nx * d, a.Y + dy * t + ny * d));
                    }
                    s += len;
                }
                var last = pts[pts.Count - 1];
                res.Add(last);
                return res;
            }

            private double Noise(double s) =>
                0.9 * Math.Sin(s / 23 + _ph[0]) + 0.5 * Math.Sin(s / 9 + _ph[1]) + 0.25 * Math.Sin(s / 4 + _ph[2]);
        }
    }
}
