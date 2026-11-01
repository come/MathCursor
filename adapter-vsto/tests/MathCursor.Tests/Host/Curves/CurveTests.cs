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
using System.IO;
using MathCursor.Engine;
using MathCursor.Host.Curves;
using MathCursor.Tests.UI;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Tests.Host.Curves
{
    // Courbes : spec ↔ XML, rendu PNG (ADR 2026-10-31-Feat-insert-curves).
    public class CurveTests
    {
        private readonly ITestOutputHelper _out;
        public CurveTests(ITestOutputHelper output) { _out = output; }

        [Fact]
        public void Spec_round_trips_through_xml()
        {
            var spec = new CurveSpec
            {
                Functions = { "rac x", "x", "1/(x-1)" },
                XMin = 0, XMax = 9.5, YMax = 4,
                Grid = false, Style = CurveStyle.Hand, WidthCm = 8.5,
            };
            var back = CurveSpec.FromXml(spec.ToXml());
            Assert.NotNull(back);
            Assert.Equal(spec.Id, back.Id);
            Assert.Equal(spec.Functions, back.Functions);
            Assert.Equal(0, back.XMin); Assert.Equal(9.5, back.XMax);
            Assert.Null(back.YMin); Assert.Equal(4, back.YMax);
            Assert.False(back.Grid);
            Assert.Equal(CurveStyle.Hand, back.Style);
            Assert.Equal(8.5, back.WidthCm);
        }

        [Fact]
        public void Chosen_reading_is_kept_and_drawn()
        {
            var spec = new CurveSpec { Functions = { "1/2x", "x" }, Picks = { @"\frac{1}{2}x", null } };
            var back = CurveSpec.FromXml(spec.ToXml());
            Assert.Equal(spec.Picks, back.Picks);

            var items = CurveRenderer.Compile(back, MathCursor.Engine.EngineCulture.Fr, out var errors, out var rows);
            Assert.Empty(errors);
            Assert.True(rows[0].Readings.Count >= 2);
            Assert.Equal(@"\frac{1}{2}x", items[0].Function.Latex);
            Assert.Equal(1.0, items[0].Function.Evaluate(2), 9);       // x/2, pas 1/(2x)
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("<pas-une-courbe/>")]
        [InlineData("<curve")]
        public void Foreign_xml_is_not_a_spec(string xml) => Assert.Null(CurveSpec.FromXml(xml));

        [Fact]
        public void Id_is_read_from_the_picture_title()
        {
            Assert.Equal("ab12", CurveSpec.IdFromTitle("MathCursor.Curve:ab12"));
            Assert.Null(CurveSpec.IdFromTitle("Image 3"));
            Assert.Null(CurveSpec.IdFromTitle(null));
        }

        [Theory]
        [InlineData(2.5, "2,5")]
        [InlineData(-3, "−3")]
        [InlineData(0.1 + 0.2, "0,3")]
        public void Tick_labels_are_french(double v, string expected) => Assert.Equal(expected, CurveRenderer.Fmt(v));

        [Fact]
        public void Failing_function_is_reported_not_drawn()
        {
            var spec = new CurveSpec { Functions = { "rac x", "int 0 1 x dx", "" } };
            var items = CurveRenderer.Compile(spec, EngineCulture.Fr, out var errors);
            Assert.Single(items);
            Assert.True(errors.ContainsKey(1));
            Assert.False(errors.ContainsKey(2));   // ligne vide : ignorée, pas une erreur
        }

        [Theory]
        [InlineData(false, "clean")]
        [InlineData(true, "hand")]
        public void Renders_a_png_with_several_curves(bool hand, string tag)
        {
            var spec = new CurveSpec { Functions = { "x", "rac x", "1/x" }, Style = hand ? CurveStyle.Hand : CurveStyle.Clean };
            var png = StaRunner.Run(() =>
            {
                var items = CurveRenderer.Compile(spec, EngineCulture.Fr, out _);
                Assert.Equal(3, items.Count);
                var bmp = CurveRenderer.Render(spec, items);
                Assert.Equal((int)(CurveRenderer.Width * 2), bmp.PixelWidth);
                return CurveRenderer.ToPng(bmp);
            });
            Assert.True(png.Length > 5000, $"PNG trop petit : {png.Length} octets");
            var dir = Path.Combine(Path.GetDirectoryName(typeof(CurveTests).Assembly.Location), ".render-probes", "curves");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"x-sqrt-inv-{tag}.png");
            File.WriteAllBytes(file, png);
            _out.WriteLine(file);
        }
    }
}
