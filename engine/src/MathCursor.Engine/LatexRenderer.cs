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
            case "delim":
                return n.Dk == "norm"
                    ? $"\\left\\|{Render(n.Parts![0], cu)}\\right\\|"
                    : $"\\left|{Render(n.Parts![0], cu)}\\right|";
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
        // prefix / nary — les n-aires dispatchent par arité (formes courtes)
        return (d.Shape == "nary" ? d.RenderFor(n.Parts!.Count) : d.Render!)(n.Parts!.Select(x => Render(x, cu)).ToList(), n);
    }
}
