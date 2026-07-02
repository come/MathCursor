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

namespace MathCursor.Engine;

// render.js — AST → LaTeX. GÉNÉRIQUE : dispatche vers le `render` déclaré par chaque
// symbole. Le moteur ne sait pas faire un \frac ; c'est `/` qui le dit.
// La culture (env matriciel, séparateur d'intervalle) est threadée en paramètre.
internal static class LatexRenderer
{
    // parenthèse un enfant infixe plus lâche que son parent — sauf si le parent groupe déjà.
    private static string Child(Node c, VocabEntry parent, EngineCulture cu)
    {
        // Nœud paren = AUTO-délimité : dissous sous un parent regroupant
        // (fraction…) — (U_n)/2 → \frac{U_{n}}{2} — sinon rendu tel quel SANS
        // re-parenthéser malgré son Grouped (ADR 2026-06-16).
        if (c.Type == "paren")
            return parent.Bracketed ? Render(c.Parts![0], cu) : Render(c, cu);
        string s = Render(c, cu);
        if (parent.Bracketed) return s;
        if (c.Type == "atom" && !parent.Apply) return s;
        bool selfGrouped = c.Sym != null && Vocabulary.Vocab.TryGetValue(c.Sym, out var cv) && cv.Bracketed;
        if ((c.Grouped && !selfGrouped) ||
            (c.Type == "infix" && Vocabulary.Vocab[c.Sym!].Looseness > parent.Looseness))
            return $"({s})";
        return s;
    }

    public static string Render(Node n, EngineCulture cu)
    {
        switch (n.Type)
        {
            case "atom":
                return n.Sym!;
            case "matrix":
                return $"\\begin{{{cu.MatrixEnv}}}"
                    + string.Join(" \\\\ ", n.Rows!.Select(r => string.Join(" & ", r.Select(x => Render(x, cu)))))
                    + $"\\end{{{cu.MatrixEnv}}}";
            case "interval":
                return $"{n.Lb}{Render(n.Parts![0], cu)}{cu.IntervalSep}{Render(n.Parts[1], cu)}{n.Rb}";
            case "list":
                return string.Join(",", n.Parts!.Select(x => Render(x, cu)));
            case "tuple": // (e1, e2, …) — virgules et parenthèses CONSERVÉES
                return "(" + string.Join(",", n.Parts!.Select(x => Render(x, cu))) + ")";
            case "set":
                return "\\{" + string.Join(",", n.Parts!.Select(x => Render(x, cu))) + "\\}";
            case "paren": // parenthèses/crochets TAPÉS conservés (ADR 2026-06-16)
                return (n.Lb ?? "(") + Render(n.Parts![0], cu) + (n.Rb ?? ")");
            case "postfix":
            {
                var c = n.Parts![0];
                string s = Render(c, cu);
                bool wrap = c.Type is "infix" or "prefix" or "nary";
                return Vocabulary.Vocab[n.Sym!].Render!(new[] { wrap ? $"({s})" : s }, null);
            }
        }

        var d = Vocabulary.Vocab[n.Sym!];
        if (d.Shape == "infix")
        {
            if (d.Sup || d.Sub)
                return d.Render!(new[] { Child(n.Parts![0], d, cu), Render(n.Parts[1], cu) }, n);
            return d.Render!(n.Parts!.Select(c => Child(c, d, cu)).ToList(), n);
        }
        // prefix / nary — les n-aires dispatchent par arité (formes courtes).
        // Une fonction/décoration fournit ses propres délimiteurs → les
        // parenthèses tapées de l'argument se dissolvent (cos((x)) → \cos(x)).
        return (d.Shape == "nary" ? d.RenderFor(n.Parts!.Count) : d.Render!)(
            n.Parts!.Select(x => x.Type == "paren" ? Render(x.Parts![0], cu) : Render(x, cu)).ToList(), n);
    }
}
