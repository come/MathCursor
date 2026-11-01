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
using System.Linq;
using MathCursor.Engine;
using MathCursor.Engine.Plot;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests;

// Courbes : saisie MathCursor → fonction numérique → polylignes.
// ADR 2026-10-31-Feat-insert-curves.
public sealed class PlotTests
{
    private readonly ITestOutputHelper _out;
    public PlotTests(ITestOutputHelper output) { _out = output; }

    private static CompiledFunction Compile(string src)
    {
        Assert.True(ForestEngine.TryCompileFunction(src, out var f, out var err), $"{src} : {err}");
        return f!;
    }

    [Theory]
    [InlineData("rac x", 4, 2)]
    [InlineData("x/rac x", 9, 3)]
    [InlineData("rac x /x", 4, 0.5)]
    [InlineData("1/x", 4, 0.25)]
    [InlineData("x^2 - 1", 3, 8)]
    [InlineData("x2", 3, 9)]
    [InlineData("2x+1", 3, 7)]
    [InlineData("3x^2 - 2x + 1", 2, 9)]
    [InlineData("x - 2 + 1", 0, -1)]
    [InlineData("x - 1 - 1", 0, -2)]
    [InlineData("x - (2 + 1)", 0, -3)]
    [InlineData("ln x", 1, 0)]
    [InlineData("ln(x)", 1, 0)]
    [InlineData("e^x", 0, 1)]
    [InlineData("exp(x)", 0, 1)]
    [InlineData("xe^x", 1, Math.E)]
    [InlineData("sin x", 0, 0)]
    [InlineData("cos(x)", 0, 1)]
    [InlineData("abs(x - 2)", 0, 2)]
    [InlineData("x(x+1)", 2, 6)]
    [InlineData("(x+1)(x-1)", 3, 8)]
    [InlineData("1/(x+1)", 1, 0.5)]
    [InlineData("x+1 /x-1", 3, 2)]
    [InlineData("f(x) = x^2", 3, 9)]
    [InlineData("f(x) = rac x /x", 4, 0.5)]
    [InlineData("y = 2x", 3, 6)]
    [InlineData("pi*x", 1, Math.PI)]
    [InlineData("0,5x", 4, 2)]
    [InlineData("g(t) = t^2", 2, 4)]
    public void Compiles_and_evaluates(string src, double x, double expected)
    {
        var f = Compile(src);
        _out.WriteLine($"{src} → {f.Latex} (var {f.Variable}, nom {f.Name})");
        Assert.Equal(expected, f.Evaluate(x), 9);
    }

    [Theory]
    [InlineData("rac x", -1)]
    [InlineData("ln x", 0)]
    [InlineData("ln x", -2)]
    [InlineData("1/x", 0)]
    public void Outside_domain_is_NaN(string src, double x) =>
        Assert.True(double.IsNaN(Compile(src).Evaluate(x)));

    [Theory]
    [InlineData("int 0 1 x dx")]
    [InlineData("x + y")]
    [InlineData("x = 2")]
    [InlineData("g(x)")]
    [InlineData("")]
    public void Not_plottable_fails_with_reason(string src)
    {
        Assert.False(ForestEngine.TryCompileFunction(src, out var f, out var err));
        Assert.Null(f);
        _out.WriteLine($"{src} → {err}");
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void Name_and_variable_come_from_the_definition()
    {
        var f = Compile("f(x) = x^2");
        Assert.Equal("f", f.Name);
        Assert.Equal("x", f.Variable);
        Assert.Equal("x^{2}", f.Latex);
    }

    [Fact]
    public void Hyperbola_is_cut_at_its_asymptote()
    {
        var f = Compile("1/x");
        var lines = Sampler.Sample(f, new PlotWindow(-5, 5, -5, 5));
        Assert.Equal(2, lines.Count);
        Assert.All(lines[0], p => Assert.True(p.X < 0));
        Assert.All(lines[1], p => Assert.True(p.X > 0));
    }

    [Fact]
    public void Tangent_is_cut_at_each_asymptote()
    {
        var lines = Sampler.Sample(Compile("tan x"), new PlotWindow(-5, 5, -5, 5));
        Assert.Equal(5, lines.Count);   // 4 asymptotes (±π/2, ±3π/2) dans [−5 ; 5]
    }

    [Fact]
    public void Square_root_starts_at_its_domain_edge()
    {
        var lines = Sampler.Sample(Compile("rac x"), new PlotWindow(-5, 5, -1, 3));
        Assert.Single(lines);
        Assert.True(lines[0][0].X < 0.01, $"début {lines[0][0].X}");
    }

    [Fact]
    public void Continuous_steep_curve_is_not_cut() =>
        Assert.Single(Sampler.Sample(Compile("x^3"), new PlotWindow(-5, 5, -2, 2)));

    [Fact]
    public void Auto_x_is_positive_for_square_root_and_log()
    {
        Assert.Equal((0.0, 10.0), Sampler.AutoX(new[] { Compile("rac x"), Compile("ln x") }));
        Assert.Equal((-5.0, 5.0), Sampler.AutoX(new[] { Compile("rac x"), Compile("x") }));
    }

    [Fact]
    public void Auto_y_ignores_the_asymptote_and_keeps_zero()
    {
        var (lo, hi) = Sampler.AutoY(new[] { Compile("1/x") }, -5, 5);
        Assert.True(hi < 30 && lo > -30, $"[{lo} ; {hi}]");
        var (lo2, _) = Sampler.AutoY(new[] { Compile("rac x") }, 0, 10);
        Assert.Equal(0, lo2);
    }

    [Theory]
    [InlineData(-5, 5, 10, new double[] { -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5 })]
    [InlineData(0, 100, 10, new double[] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 })]
    [InlineData(0, 1, 5, new double[] { 0, 0.2, 0.4, 0.6, 0.8, 1 })]
    public void Ticks_are_round(double min, double max, int n, double[] expected) =>
        Assert.Equal(expected, Sampler.Ticks(min, max, n).Select(t => Math.Round(t, 9)).ToArray());
    [Fact]
    public void Readings_start_with_the_popup_top_reading()
    {
        var rs = ForestEngine.FunctionReadings("rac x /x");
        Assert.NotEmpty(rs);
        Assert.True(ForestEngine.TryCompileFunction("rac x /x", out var top, out _));
        Assert.Equal(top!.Latex, rs[0].Latex);
    }

    [Fact]
    public void Ambiguous_input_gives_several_plottable_readings()
    {
        var rs = ForestEngine.FunctionReadings("1/2x");
        foreach (var r in rs) _out.WriteLine(r.Latex);
        Assert.True(rs.Count >= 2);
        Assert.Equal(rs.Count, rs.Select(r => r.Latex).Distinct().Count());
    }

    [Fact]
    public void Not_a_function_has_no_reading() =>
        Assert.Empty(ForestEngine.FunctionReadings("x = 2"));
}
