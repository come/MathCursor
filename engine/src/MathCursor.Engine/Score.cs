using System.Collections.Generic;

namespace MathCursor.Engine;

// score.js — filtre COUPE + COÛT. GÉNÉRIQUE : ne lit que les FEATURES (class, looseness,
// cut, bracketed, mapping), jamais un nom d'opérateur.
internal static class Score
{
    private const double PENALTY = 1.5;
    private const double WIDEN = 1;
    private const double MODE_MIX = 3;
    private const double ORIENT = 0.5;
    private const double MAP_DEF = 2.5;
    private const double HOLE_COST = 3;
    private const double SUM = 3; // pour cohérence avec looseness (non utilisé directement ici)

    private static VocabEntry? Decl(Node n) => n.Sym != null && Vocabulary.Vocab.TryGetValue(n.Sym, out var v) ? v : null;
    private static List<Node> Parts(Node n) => n.EffectiveParts();
    private static bool IsStrong(Node n) { var d = Decl(n); return n.Type != "atom" && d != null && d.Class == Vocabulary.STRONG; }
    private static bool IsWeak(Node n) { var d = Decl(n); return n.Type != "atom" && d != null && d.Class == Vocabulary.WEAK; }

    // ── Filtre COUPE ─────────────────────────────────────────────────────────
    private static bool CoversSpacedWeak(Node n)
    {
        if (n.Type == "atom" || n.Grouped) return false;
        if (IsWeak(n) && n.Spaced) return true;
        foreach (var c in Parts(n)) if (CoversSpacedWeak(c)) return true;
        return false;
    }

    private static bool IsCutOp(Node n) { var d = Decl(n); return n.Type != "atom" && d != null && d.Cut; }

    public static bool CrossesCut(Node n)
    {
        if (n.Type == "atom") return false;
        if (IsStrong(n)) { foreach (var c in Parts(n)) if (CoversSpacedWeak(c)) return true; }
        if (!IsCutOp(n)) { foreach (var c in Parts(n)) if (IsCutOp(c) && !c.Grouped) return true; }
        foreach (var c in Parts(n)) if (CrossesCut(c)) return true;
        return false;
    }

    // ── Coût ───────────────────────────────────────────────────────────────
    private static double Loose(Node n)
    {
        var d = Decl(n);
        if (d == null) return 0;
        return d.Looseness + (d.Shape == "infix" && n.Spaced ? 1.5 : 0);
    }

    private static int Inversions(Node n)
    {
        if (n.Type == "atom") return 0;
        int inv = 0;
        var d = Decl(n);
        foreach (var c in Parts(n))
        {
            if (c.Type != "atom" && d != null && Loose(n) < Loose(c)) inv++;
            inv += Inversions(c);
        }
        return inv;
    }

    private static bool NestStrong(Node n) => IsStrong(n) && !n.Implicit;
    private static bool ContainsNest(Node n)
    {
        if (n.Type == "atom") return false;
        if (NestStrong(n)) return true;
        foreach (var c in Parts(n)) if (ContainsNest(c)) return true;
        return false;
    }

    private static int Nesting(Node n)
    {
        if (n.Type == "atom") return 0;
        int c = 0;
        foreach (var x in Parts(n)) c += Nesting(x);
        if (NestStrong(n)) { foreach (var x in Parts(n)) if (ContainsNest(x)) { c++; break; } }
        return c;
    }

    private static double Widen(Node n)
    {
        if (n.Type == "atom") return 0;
        double c = 0;
        foreach (var x in Parts(n)) c += Widen(x);
        if (n.Implicit) { foreach (var p in Parts(n)) if (p.Type != "atom" && !p.Grouped) { c += WIDEN; break; } }
        return c;
    }

    private static double Base(Node n) => Inversions(n) + Nesting(n) + Widen(n);

    private static string Shape(Node n)
    {
        if (n.Type == "atom") return "a";
        var sb = new System.Text.StringBuilder();
        sb.Append(n.Sym ?? "mat").Append('(');
        var parts = Parts(n);
        for (int k = 0; k < parts.Count; k++) { if (k > 0) sb.Append(','); sb.Append(Shape(parts[k])); }
        sb.Append(')');
        return sb.ToString();
    }

    private static double ParentRefund(Node n)
    {
        double r = 0;
        foreach (var c in Parts(n)) r += ParentRefund(c);
        var groups = new Dictionary<string, List<Node>>();
        foreach (var c in Parts(n))
        {
            if (c.Type == "atom" || c.Sig == null) continue;
            if (!groups.TryGetValue(c.Sig, out var g)) groups[c.Sig] = g = new List<Node>();
            g.Add(c);
        }
        double sym = 0;
        foreach (var g in groups.Values)
        {
            if (g.Count < 2) continue;
            var shapes = new HashSet<string>();
            foreach (var c in g) shapes.Add(Shape(c));
            if (shapes.Count == 1)
                foreach (var c in g) if (Loose(n) < Loose(c)) sym++;
        }
        double gestalt = 0;
        var d = Decl(n);
        if (d != null && d.Bracketed)
        {
            int inv = 0;
            foreach (var c in Parts(n)) if (c.Type != "atom" && Loose(n) < Loose(c)) inv++;
            if (inv >= 2) gestalt = inv - 1;
        }
        return r + (sym > gestalt ? sym : gestalt);
    }

    private static double GlobalCoherence(Node root)
    {
        var bySig = new Dictionary<string, (HashSet<string> Shapes, List<Node> Nodes)>();
        void Walk(Node x)
        {
            if (x.Type != "atom" && x.Sig != null)
            {
                if (!bySig.TryGetValue(x.Sig, out var g)) bySig[x.Sig] = g = (new HashSet<string>(), new List<Node>());
                g.Shapes.Add(Shape(x));
                g.Nodes.Add(x);
            }
            foreach (var c in Parts(x)) Walk(c);
        }
        Walk(root);
        double d = 0;
        foreach (var (shapes, nodes) in bySig.Values)
        {
            if (shapes.Count > 1) d += PENALTY * (shapes.Count - 1);
            else if (nodes.Count >= 2) d -= (nodes.Count - 1) * Base(nodes[0]);
        }
        return d;
    }

    private static double ModeCoherence(Node root)
    {
        var byCoh = new Dictionary<string, HashSet<int>>();
        void Walk(Node x)
        {
            if (x.Type == "atom" && x.Coh != null)
            {
                if (!byCoh.TryGetValue(x.Coh, out var g)) byCoh[x.Coh] = g = new HashSet<int>();
                g.Add(x.Ai);
            }
            foreach (var c in Parts(x)) Walk(c);
        }
        Walk(root);
        double d = 0;
        foreach (var idx in byCoh.Values) if (idx.Count > 1) d += MODE_MIX * (idx.Count - 1);
        return d;
    }

    // Écho de symétrie entre FRÈRES (« 1/2x + 1/2x2 ») : deux enfants dont la
    // signature de l'un PROLONGE celle de l'autre (préfixe strict) sont la
    // même tournure étendue par l'utilisateur — même forme de tête (/ et /)
    // → bonus, formes divergentes (/ et ·) → malus. C'est l'analogue « à
    // prolongement » de GlobalCoherence, qui ne couple que les signatures
    // IDENTIQUES (1/2x + 1/2y) et laissait l'hybride
    // \frac{1}{2x}+\frac{1}{2}x^2 gagner.
    private static double SiblingEcho(Node n)
    {
        double d = 0;
        var kids = Parts(n);
        foreach (var c in kids) d += SiblingEcho(c);
        for (int i = 0; i < kids.Count; i++)
            for (int k = i + 1; k < kids.Count; k++)
            {
                var a = kids[i]; var b = kids[k];
                if (a.Type == "atom" || b.Type == "atom" || a.Sig == null || b.Sig == null) continue;
                if (a.Sig.Length == b.Sig.Length) continue; // identiques : GlobalCoherence
                var (shorter, longer) = a.Sig.Length < b.Sig.Length ? (a, b) : (b, a);
                if (!longer.Sig!.StartsWith(shorter.Sig!, System.StringComparison.Ordinal)) continue;
                d += shorter.Sym == longer.Sym ? -1 : 1;
            }
        return d;
    }

    private static double MatrixExtra(Node n)
    {
        if (n.Type == "atom") return 0;
        double e = 0;
        foreach (var c in Parts(n)) e += MatrixExtra(c);
        if (n.Type == "matrix" && n.Alt) e += ORIENT;
        return e;
    }

    private static double DefChain(Node n)
    {
        double r = 0;
        foreach (var c in Parts(n)) r += DefChain(c);
        var d = Decl(n);
        if (d != null && d.Mapping)
            foreach (var c in Parts(n)) if (IsCutOp(c) && !c.Grouped) { r += MAP_DEF; break; }
        return r;
    }

    private static int Holes(Node n)
    {
        int h = n.Hole ? 1 : 0;
        foreach (var c in Parts(n)) h += Holes(c);
        return h;
    }

    public static double Cost(Node n) =>
        Base(n) + MatrixExtra(n) + HOLE_COST * Holes(n) + GlobalCoherence(n) + ModeCoherence(n) + SiblingEcho(n) - ParentRefund(n) - DefChain(n);
}
