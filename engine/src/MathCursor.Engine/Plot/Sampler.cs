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

namespace MathCursor.Engine.Plot;

/// <summary>Point d'une courbe, en coordonnées mathématiques.</summary>
public readonly struct PlotPoint
{
    public double X { get; }
    public double Y { get; }
    public PlotPoint(double x, double y) { X = x; Y = y; }
}

/// <summary>Fenêtre du repère.</summary>
public sealed class PlotWindow
{
    public double XMin { get; }
    public double XMax { get; }
    public double YMin { get; }
    public double YMax { get; }
    public PlotWindow(double xMin, double xMax, double yMin, double yMax)
    { XMin = xMin; XMax = xMax; YMin = yMin; YMax = yMax; }
}

// Échantillonnage PUR (aucun pixel) : polylignes coupées aux trous de domaine et aux
// asymptotes, fenêtre automatique, graduations « rondes ». ADR 2026-10-31-Feat-insert-curves.
public static class Sampler
{
    private const int BaseSamples = 400;
    private const int MaxDepth = 6;

    /// <summary>Polylignes de <paramref name="f"/> dans la fenêtre <paramref name="w"/> : une
    /// nouvelle polyligne à chaque trou du domaine (√x pour x &lt; 0) ou asymptote (1/x en 0).
    /// Les valeurs hors cadre sont bornées à 3 hauteurs du cadre (le rendu découpe).</summary>
    public static IReadOnlyList<IReadOnlyList<PlotPoint>> Sample(CompiledFunction f, PlotWindow w)
    {
        double h = w.YMax - w.YMin, lo = w.YMin - 3 * h, hi = w.YMax + 3 * h;
        var lines = new List<IReadOnlyList<PlotPoint>>();
        var cur = new List<PlotPoint>();
        void Cut() { if (cur.Count > 1) lines.Add(cur); cur = new List<PlotPoint>(); }
        // un point hors domaine (NaN) termine la polyligne en cours
        void Emit(double x, double y)
        {
            if (Ok(y)) cur.Add(new PlotPoint(x, Math.Max(lo, Math.Min(hi, y))));
            else Cut();
        }

        double dx = (w.XMax - w.XMin) / BaseSamples;
        double px = w.XMin, py = f.Evaluate(px);
        Emit(px, py);
        for (int i = 1; i <= BaseSamples; i++)
        {
            double x = w.XMin + i * dx, y = f.Evaluate(x);
            Refine(f, px, py, x, y, w, 0, Emit, Cut);
            Emit(x, y);
            px = x; py = y;
        }
        Cut();
        return lines;
    }

    private static bool Ok(double y) => !double.IsNaN(y) && !double.IsInfinity(y);

    // Subdivise [x0;x1] quand la courbe y bouge beaucoup (pente raide, bord de domaine) ;
    // coupe la ligne quand elle traverse le cadre d'un bord à l'autre sans valeur
    // intermédiaire (asymptote verticale) ou quand le domaine s'interrompt.
    private static void Refine(CompiledFunction f, double x0, double y0, double x1, double y1, PlotWindow w,
        int depth, Action<double, double> emit, Action cut)
    {
        double h = w.YMax - w.YMin;
        bool ok0 = Ok(y0), ok1 = Ok(y1);
        bool steep = ok0 && ok1 && Math.Abs(y1 - y0) > 0.04 * h && (InView(y0, w) || InView(y1, w) || Opposite(y0, y1, w));
        bool edge = ok0 != ok1;
        if (!(steep || edge)) return;
        double xm = (x0 + x1) / 2, ym = f.Evaluate(xm);
        if (depth >= MaxDepth)
        {
            // asymptote : de part et d'autre du cadre, et la valeur au milieu ne fait pas le lien
            if (steep && Opposite(y0, y1, w) && !(Ok(ym) && Between(ym, y0, y1) && Math.Abs(ym - (y0 + y1) / 2) < 0.5 * Math.Abs(y1 - y0)))
                cut();
            return;
        }
        Refine(f, x0, y0, xm, ym, w, depth + 1, emit, cut);
        emit(xm, ym);
        Refine(f, xm, ym, x1, y1, w, depth + 1, emit, cut);
    }

    private static bool InView(double y, PlotWindow w) => y >= w.YMin && y <= w.YMax;
    private static bool Opposite(double a, double b, PlotWindow w) => (a > w.YMax && b < w.YMin) || (a < w.YMin && b > w.YMax);
    private static bool Between(double m, double a, double b) => m >= Math.Min(a, b) && m <= Math.Max(a, b);

    /// <summary>Intervalle en x par défaut : [−5 ; 5], ou [0 ; 10] si aucune fonction n'est
    /// définie à gauche de 0 (√x, ln x).</summary>
    public static (double Min, double Max) AutoX(IEnumerable<CompiledFunction> fs)
    {
        var list = fs.ToList();
        bool left = list.Count == 0 || list.Any(f => Enumerable.Range(1, 50).Any(i => Ok(f.Evaluate(-5.0 * i / 50))));
        return left ? (-5, 5) : (0, 10);
    }

    /// <summary>Intervalle en y qui montre l'essentiel des courbes sur [xMin ; xMax] : quantiles
    /// 2 %-98 % (une asymptote n'écrase pas le reste), marge de 10 %, 0 inclus s'il est proche.</summary>
    public static (double Min, double Max) AutoY(IEnumerable<CompiledFunction> fs, double xMin, double xMax)
    {
        var ys = new List<double>();
        foreach (var f in fs)
            for (int i = 0; i <= BaseSamples; i++)
            {
                double y = f.Evaluate(xMin + (xMax - xMin) * i / BaseSamples);
                if (Ok(y)) ys.Add(y);
            }
        if (ys.Count == 0) return (-5, 5);
        ys.Sort();
        double lo = ys[(int)(0.02 * (ys.Count - 1))], hi = ys[(int)Math.Ceiling(0.98 * (ys.Count - 1))];
        if (hi - lo < 1e-9) { lo -= 1; hi += 1; }
        double span = hi - lo;
        if (lo > 0 && lo < 0.5 * span) lo = 0;
        if (hi < 0 && -hi < 0.5 * span) hi = 0;
        double m = 0.1 * (hi - lo);
        return (lo == 0 ? 0 : lo - m, hi == 0 ? 0 : hi + m);
    }

    /// <summary>Graduations « rondes » (pas 1, 2 ou 5 × 10ⁿ) dans [min ; max], au plus
    /// <paramref name="maxTicks"/>.</summary>
    public static IReadOnlyList<double> Ticks(double min, double max, int maxTicks = 10)
    {
        double span = max - min;
        if (!(span > 0) || maxTicks < 2) return Array.Empty<double>();
        double raw = span / maxTicks, mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double step = new[] { 1, 2, 5, 10 }.Select(k => k * mag).First(s => s >= raw);
        var ticks = new List<double>();
        for (double t = Math.Ceiling(min / step) * step; t <= max + 1e-9 * span; t += step)
            ticks.Add(Math.Abs(t) < 1e-9 * span ? 0 : Math.Round(t / step) * step);
        return ticks;
    }
}
