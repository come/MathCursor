using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Engine;

// render.js — AST → LaTeX. GÉNÉRIQUE : dispatche vers le `render` déclaré par chaque
// symbole. Le moteur ne sait pas faire un \frac ; c'est `/` qui le dit.
internal static class LatexRenderer
{
    // parenthèse un enfant infixe plus lâche que son parent — sauf si le parent groupe déjà.
    private static string Child(Node c, VocabEntry parent)
    {
        string s = Render(c);
        if (parent.Bracketed) return s;
        if (c.Type == "atom" && !parent.Apply) return s;
        bool selfGrouped = c.Sym != null && Vocabulary.Vocab.TryGetValue(c.Sym, out var cv) && cv.Bracketed;
        if ((c.Grouped && !selfGrouped) ||
            (c.Type == "infix" && Vocabulary.Vocab[c.Sym!].Looseness > parent.Looseness))
            return $"({s})";
        return s;
    }

    public static string Render(Node n)
    {
        switch (n.Type)
        {
            case "atom":
                return n.Sym!;
            case "matrix":
            {
                var m = Vocabulary.Matrix[n.Delim!];
                return m.Open
                    + string.Join(" \\\\ ", n.Rows!.Select(r => string.Join(" & ", r.Select(Render))))
                    + m.Close;
            }
            case "interval":
                return $"{n.Lb}{Render(n.Parts![0])}{Vocabulary.Locale.IntervalSep}{Render(n.Parts[1])}{n.Rb}";
            case "list":
                return string.Join(",", n.Parts!.Select(Render));
            case "set":
                return "\\{" + string.Join(",", n.Parts!.Select(Render)) + "\\}";
            case "delim":
                return n.Dk == "norm"
                    ? $"\\left\\|{Render(n.Parts![0])}\\right\\|"
                    : $"\\left|{Render(n.Parts![0])}\\right|";
            case "postfix":
            {
                var c = n.Parts![0];
                string s = Render(c);
                bool wrap = c.Type is "infix" or "prefix" or "nary";
                return Vocabulary.Vocab[n.Sym!].Render!(new[] { wrap ? $"({s})" : s }, null);
            }
        }

        var d = Vocabulary.Vocab[n.Sym!];
        if (d.Shape == "infix")
        {
            if (d.Sup || d.Sub)
                return d.Render!(new[] { Child(n.Parts![0], d), Render(n.Parts[1]) }, n);
            return d.Render!(n.Parts!.Select(c => Child(c, d)).ToList(), n);
        }
        return d.Render!(n.Parts!.Select(Render).ToList(), n); // prefix / nary
    }
}
