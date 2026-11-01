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
using System.Linq;
using System.Xml.Linq;

namespace MathCursor.Host.Curves
{
    /// <summary>Style de tracé : propre (style cours) ou main levée.</summary>
    internal enum CurveStyle { Clean, Hand }

    /// <summary>
    /// Ce qu'il faut pour redessiner une image de courbes : les fonctions telles
    /// que tapées (steno MathCursor), le repère et le style. Stocké en XML dans une
    /// part <c>CustomXMLParts</c> par image (ADR 2026-10-31-Feat-insert-curves).
    /// Pur : aucune dépendance Word ni WPF.
    /// </summary>
    internal sealed class CurveSpec
    {
        public const string Ns = "urn:mathcursor:curve:v1";
        public const string TitlePrefix = "MathCursor.Curve:";

        /// <summary>Palette des courbes, dans l'ordre d'ajout (bleu, rouge, vert, violet, orange).</summary>
        public static readonly string[] Palette = { "#1F4E9C", "#C0392B", "#218258", "#7D3C98", "#D35400" };

        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);
        public List<string> Functions { get; set; } = new List<string>();
        /// <summary>Lecture choisie par ligne (LaTeX de la lecture de la popup), alignée sur
        /// <see cref="Functions"/> ; null ou absente = la lecture de tête.</summary>
        public List<string> Picks { get; set; } = new List<string>();

        /// <summary>Intervalle en x ; null = automatique ([−5 ; 5], ou [0 ; 10] si rien n'est
        /// défini à gauche de 0).</summary>
        public double? XMin { get; set; }
        public double? XMax { get; set; }
        /// <summary>Intervalle en y ; null = automatique (d'après les courbes).</summary>
        public double? YMin { get; set; }
        public double? YMax { get; set; }

        public bool Grid { get; set; } = true;
        public CurveStyle Style { get; set; } = CurveStyle.Clean;
        /// <summary>Largeur de l'image dans le document, en cm.</summary>
        public double WidthCm { get; set; } = 10;

        public static string ColorOf(int index) => Palette[index % Palette.Length];

        public string PickOf(int index) => index < Picks.Count ? Picks[index] : null;

        public CurveSpec Clone()
        {
            var c = (CurveSpec)MemberwiseClone();
            c.Functions = new List<string>(Functions);
            c.Picks = new List<string>(Picks);
            return c;
        }

        public string ToXml()
        {
            XNamespace ns = Ns;
            var root = new XElement(ns + "curve",
                new XAttribute("id", Id),
                new XAttribute("style", Style == CurveStyle.Hand ? "hand" : "clean"),
                new XAttribute("grid", Grid ? "1" : "0"),
                new XAttribute("width", Num(WidthCm)));
            AddOpt(root, "xmin", XMin); AddOpt(root, "xmax", XMax);
            AddOpt(root, "ymin", YMin); AddOpt(root, "ymax", YMax);
            for (int i = 0; i < Functions.Count; i++)
            {
                var f = new XElement(ns + "f", Functions[i]);
                var pick = PickOf(i);
                if (pick != null) f.Add(new XAttribute("pick", pick));
                root.Add(f);
            }
            return root.ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>Relit une spec ; null si le XML n'en est pas une.</summary>
        public static CurveSpec FromXml(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            try
            {
                var root = XElement.Parse(xml);
                XNamespace ns = Ns;
                if (root.Name != ns + "curve") return null;
                return new CurveSpec
                {
                    Id = (string)root.Attribute("id") ?? Guid.NewGuid().ToString("N").Substring(0, 12),
                    Style = (string)root.Attribute("style") == "hand" ? CurveStyle.Hand : CurveStyle.Clean,
                    Grid = (string)root.Attribute("grid") != "0",
                    WidthCm = Opt(root, "width") ?? 10,
                    XMin = Opt(root, "xmin"), XMax = Opt(root, "xmax"),
                    YMin = Opt(root, "ymin"), YMax = Opt(root, "ymax"),
                    Functions = root.Elements(ns + "f").Select(e => e.Value).ToList(),
                    Picks = root.Elements(ns + "f").Select(e => (string)e.Attribute("pick")).ToList(),
                };
            }
            catch (Exception) { return null; }
        }

        /// <summary>Id lu dans le titre d'une image (« MathCursor.Curve:ab12… »), sinon null.</summary>
        public static string IdFromTitle(string title) =>
            title != null && title.StartsWith(TitlePrefix, StringComparison.Ordinal)
                ? title.Substring(TitlePrefix.Length) : null;

        private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static void AddOpt(XElement e, string name, double? v)
        {
            if (v.HasValue) e.Add(new XAttribute(name, Num(v.Value)));
        }

        private static double? Opt(XElement e, string name)
        {
            var a = (string)e.Attribute(name);
            return a != null && double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;
        }
    }
}
