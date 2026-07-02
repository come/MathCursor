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

    private static bool AllAtomParts(Node n)
    {
        foreach (var c in Parts(n)) if (c.Type != "atom") return false;
        return true;
    }

    // Script à opérandes atomiques (x², u_n) : une lettre habillée.
    private static bool AtomicScript(Node n) =>
        Decl(n) is { Sup: true } or { Sub: true } && AllAtomParts(n);

    // Les décorations TIGHT (bar/vec/hat : opérande d'un seul morceau) sont
    // visuellement atomiques — \bar{z} est une lettre décorée, pas une
    // imbrication : elles ne déclenchent pas la pénalité de nesting (sinon
    // « abs z-conjz » perdait la lecture large \left|z-\bar{z}\right| que
    // « abs z+1 » propose).
    private static bool NestStrong(Node n) =>
        IsStrong(n) && !n.Implicit && Decl(n) is not { Tight: true };
    // inBrackets : sous un conteneur Bracketed (fraction…), un script
    // atomique est aplati par les accolades — x² n'y est pas une imbrication
    // (sinon « 1/x+x2 » perd \frac{1}{x+x^2}). HORS conteneur bracketed
    // (lim, ÷…), il compte toujours : c'est ce qui écarte les lectures
    // absurdes \frac{\lim…1}{x} et f÷R² (mesuré, 2026-06-10).
    private static bool ContainsNest(Node n, bool inBrackets)
    {
        if (n.Type == "atom") return false;
        if (NestStrong(n) && !(inBrackets && AtomicScript(n))) return true;
        foreach (var c in Parts(n)) if (ContainsNest(c, inBrackets)) return true;
        return false;
    }

    private static int Nesting(Node n)
    {
        if (n.Type == "atom") return 0;
        int c = 0;
        foreach (var x in Parts(n)) c += Nesting(x);
        if (NestStrong(n))
        {
            bool br = Decl(n) is { Bracketed: true };
            foreach (var x in Parts(n)) if (ContainsNest(x, br)) { c++; break; }
        }
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
        sb.Append(n.Sym ?? n.Type).Append('(');
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

    // ── Cohérence de SLURP (« la symétrie », formulation utilisateur) ──────
    // Une fraction qui AVAIT la place de déborder (Node.Choice) a fait un
    // CHOIX : opérande droite composite (slurp) ou atomique (minimal). Comme
    // ModeCoherence pour R/ℝ : mélanger les choix dans une même expression
    // coûte MODE_MIX ; un choix slurp RÉPÉTÉ obtient la remise du dupliqué
    // (−min(Base)×(n−1), l'esprit de GlobalCoherence — un bonus fixe ne
    // réduit pas l'écart de base, qui vaut pile le PopupGap). Les fractions
    // SANS choix (1/2 en bord de segment) ne votent pas. Remplace les
    // régimes géométriques SiblingEcho (prolongement/jumelles) — couvre
    // « 1/2x + 1/2x2 », « 1/x+x2 * 1/x-x2 » ET « 1/x+x2 - 1/x-x2+x3 »
    // sans notion de fratrie ni de longueur.
    private static bool MinimalFrac(Node n) =>
        n.Type != "atom" && !n.Grouped && Decl(n) is { Bracketed: true }
        && Parts(n).Count == 2 && Parts(n)[1].Type == "atom";

    // feuille la plus à gauche — le LITTÉRAL que l'œil voit en premier.
    private static string? Leftmost(Node n)
    {
        while (n.Type != "atom")
        {
            var p = n.EffectiveParts();
            if (p.Count == 0) return null;
            n = p[0];
        }
        return n.Sym;
    }

    private readonly struct FracSite
    {
        public readonly bool Slurp; public readonly string Key; public readonly string Sig; public readonly double B;
        public FracSite(bool slurp, string key, string sig, double b) { Slurp = slurp; Key = key; Sig = sig; B = b; }
    }

    private static double SlurpCoherence(Node root)
    {
        // SITES de choix : fraction qui a débordé (Choice + opérande droite
        // composite) = SLURP ; fraction minimale enfant gauche d'un infixe
        // NON-espacé (le matériau à droite était slurpable) = MIN. Une
        // fraction sans choix (bord de segment) ne vote pas.
        List<FracSite>? sites = null;
        void Add(Node f, bool slurp)
        {
            var p = Parts(f);
            string key = (Leftmost(p[0]) ?? "?") + "/" + (Leftmost(p[1]) ?? "?");
            (sites ??= new List<FracSite>()).Add(new FracSite(slurp, key, f.Sig ?? "", Base(f)));
        }
        void Walk(Node x)
        {
            if (x.Type == "atom") return;
            var parts = Parts(x);
            if (x.Type == "infix" && !x.Spaced && parts.Count == 2 && MinimalFrac(parts[0]))
                Add(parts[0], slurp: false);
            if (x.Choice && Decl(x) is { Bracketed: true } && parts.Count == 2 && parts[1].Type != "atom")
                Add(x, slurp: true);
            foreach (var c in parts) Walk(c);
        }
        Walk(root);
        if (sites == null || sites.Count < 2) return 0;

        // COMPARABLES = mêmes littéraux de tête (numérateur + 1re feuille du
        // dénominateur) : l'œil voit deux « 1/x… » — \frac{3}{4} et
        // \frac{1}{2x} n'ont rien à se dire (mesuré : un mode GLOBAL pénalise
        // les expressions mixtes légitimes). Par paire comparable : modes
        // mélangés → +1 ; slurps répétés à signatures DISTINCTES (les
        // identiques sont déjà remboursées par GlobalCoherence) → remise du
        // dupliqué −min(Base).
        double d = 0;
        for (int i = 0; i < sites.Count; i++)
            for (int k = i + 1; k < sites.Count; k++)
            {
                var a = sites[i]; var b = sites[k];
                if (a.Key != b.Key) continue;
                if (a.Slurp != b.Slurp) d += 1;
                // remise du dupliqué, prudente : min des deux bases (max
                // sur-corrige — mesuré : la paire symétrique MINIMALE de
                // « 1/2x + 1/2x2 » sortait de la fenêtre).
                else if (a.Slurp && a.Sig != b.Sig) d -= System.Math.Min(a.B, b.B);
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
        Base(n) + MatrixExtra(n) + HOLE_COST * Holes(n) + GlobalCoherence(n) + ModeCoherence(n) + SlurpCoherence(n) - ParentRefund(n) - DefChain(n);
}
