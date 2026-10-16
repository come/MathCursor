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

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MathCursor.Engine;

// Mode physique-chimie (ADR 2026-10-16-Feat-physics-chemistry-mode) : reconnaît une
// formule chimique bornée (H2O, Fe2+, Ca(OH)2, SO42-, Na+(aq)) par GRAMMAIRE sur la
// table des éléments (PhysChem.Elements). La formule devient UN atome portant
// ses lectures : le reste du moteur (parser, score, rendu) ne la voit pas.
//   formule := groupe+ charge? état?
//   groupe  := Élément indice? | '(' groupe+ ')' indice?      indice := '_'? chiffres
//   charge  := '^'? chiffres? (+|-)   suivie d'une frontière   état := (aq|s|l|g)
internal static class ChemFormula
{
    private static HashSet<string> Elements => PhysChem.Elements;
    private static List<string> States => PhysChem.States;

    private enum Kind { El, Num, Open, Close }

    private static bool IsUpper(char c) => c >= 'A' && c <= 'Z';
    private static bool IsLower(char c) => c >= 'a' && c <= 'z';
    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    /// <summary>Formule commençant en <paramref name="i"/> : longueur consommée
    /// (0 = pas une formule) et lectures LaTeX, la première étant la lecture par défaut.</summary>
    public static int Scan(string src, int i, out List<string> readings)
    {
        readings = new List<string>();
        char before = Ch(src, i - 1);
        // début : un élément, ou un groupe parenthésé indicé ((NH4)2SO4, vérifié plus bas)
        bool parenStart = Ch(src, i) == '(' && IsUpper(Ch(src, i + 1));
        if (!(IsUpper(Ch(src, i)) || parenStart) || IsLower(before) || IsUpper(before) || before == '\\') return 0;

        var items = new List<(Kind K, string S)>();
        int p = i, depth = 0;
        while (p < src.Length)
        {
            char c = src[p];
            if (IsUpper(c))
            {
                string two = p + 1 < src.Length ? src.Substring(p, 2) : "";
                string el = IsLower(Ch(src, p + 1)) && Elements.Contains(two) ? two
                    : Elements.Contains(c.ToString()) ? c.ToString() : null!;
                if (el == null) return 0;
                items.Add((Kind.El, el)); p += el.Length;
            }
            else if ((IsDigit(c) || (c == '_' && IsDigit(Ch(src, p + 1)))) && items.Count > 0
                     && items[items.Count - 1].K is Kind.El or Kind.Close)
            {
                if (c == '_') p++;
                int st = p;
                while (IsDigit(Ch(src, p))) p++;
                items.Add((Kind.Num, src.Substring(st, p - st)));
            }
            else if (c == '(' && IsUpper(Ch(src, p + 1))) { items.Add((Kind.Open, "(")); depth++; p++; }
            else if (c == ')' && depth > 0) { items.Add((Kind.Close, ")")); depth--; p++; }
            else break;
        }
        if (depth != 0 || items.Count == 0) return 0;
        // groupe de tête : seulement s'il porte un indice ((NH4)2), sinon (BC), (AB)
        // restent de la géométrie
        bool groupOnly = false;   // la formule n'est que le groupe indicé : (BC)2
        if (parenStart)
        {
            int d = 0, close = 0;
            for (; close < items.Count; close++)
            {
                d += items[close].K == Kind.Open ? 1 : items[close].K == Kind.Close ? -1 : 0;
                if (d == 0) break;
            }
            if (close + 1 >= items.Count || items[close + 1].K != Kind.Num) return 0;
            groupOnly = close + 2 == items.Count;
        }

        // charge : signe final. Sans « ^ », ses chiffres ont déjà été lus en
        // indice (ambigus) ; avec « ^ » (Fe^2+), ils sont explicitement la charge.
        bool hat = Ch(src, p) == '^';
        int q = hat ? p + 1 : p;
        while (hat && IsDigit(Ch(src, q))) q++;
        string? charge = null;
        if ("+-".IndexOf(Ch(src, q)) >= 0 && ChargeEnd(src, q + 1))
        {
            charge = (hat ? src.Substring(p + 1, q - p - 1) : "") + Ch(src, q);
            p = q + 1;
        }
        else hat = false;
        string? state = StateAt(src, p, items.Count > 0);
        if (state != null) p += state.Length + 2;
        if (char.IsLetter(Ch(src, p)) || IsDigit(Ch(src, p))) return 0;

        bool bare = items.Count == 1;
        bool singleLetters = items.All(t => t.K != Kind.El || t.S.Length == 1);
        bool digits = items.Any(t => t.K == Kind.Num);
        int elements = items.Count(t => t.K == Kind.El);
        // un élément seul sans rien autour reste une variable (C, H, V) ou un mot
        // déjà connu du moteur : unité (2 Pa), fonction (Re(z))
        if (bare && charge == null && state == null
            && (items[0].S.Length == 1 || Vocabulary.Vocab.ContainsKey(items[0].S))) return 0;

        string suffix = state != null ? $"(\\mathrm{{{state}}})" : "";
        if (charge != null && !hat && items[items.Count - 1].K == Kind.Num)
        {
            // chiffres juste avant le signe : charge ou indice, à égalité (popup)
            string d = items[items.Count - 1].S;
            string head = Render(items.Take(items.Count - 1));
            readings.Add(head + (d.Length > 1 ? $"_{{{d.Substring(0, d.Length - 1)}}}" : "")
                + $"^{{{d.Substring(d.Length - 1)}{charge}}}" + suffix);
            readings.Add(Render(items) + $"^{{{charge}}}" + suffix);
        }
        else
        {
            string chem = Render(items) + (charge != null ? $"^{{{charge}}}" : "") + suffix;
            readings.Add(chem);
            // lecture maths gardée en alternative quand la formule y ressemble :
            // lettres seules (BC, UI → maths d'abord), une lettre indicée (V2, O2) ou un
            // groupe de tête de lettres seules ((BC)2 : longueur au carré)
            if (charge == null && state == null && singleLetters && (!digits || elements == 1 || groupOnly))
            {
                string math = string.Concat(items.Select(t => t.K == Kind.Num ? $"^{{{t.S}}}" : t.S));
                if (digits) readings.Add(math); else readings.Insert(0, math);
            }
        }
        return p - i;
    }

    private static string Render(IEnumerable<(Kind K, string S)> items)
    {
        var sb = new StringBuilder();
        foreach (var (k, s) in items)
            sb.Append(k switch
            {
                Kind.El => $"\\mathrm{{{s}}}",
                Kind.Num => $"_{{{s}}}",
                _ => s,
            });
        return sb.ToString();
    }

    // après une charge : fin, espace, fermant, séparateur, « = » ou état
    internal static bool ChargeEnd(string src, int p)
    {
        char c = Ch(src, p);
        return c == '\0' || c == ' ' || c == '\u00A0' || c == '\u202F' || ")],;=".IndexOf(c) >= 0 || StateAt(src, p, true) != null;
    }

    private static string? StateAt(string src, int p, bool any)
    {
        if (!any || Ch(src, p) != '(') return null;
        foreach (var s in States)
            if (p + s.Length + 1 < src.Length && string.CompareOrdinal(src, p + 1, s, 0, s.Length) == 0
                && src[p + s.Length + 1] == ')')
                return s;
        return null;
    }

    private static char Ch(string s, int i) => i >= 0 && i < s.Length ? s[i] : '\0';
}
