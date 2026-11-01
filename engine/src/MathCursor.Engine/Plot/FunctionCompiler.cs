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
using System.Text;

namespace MathCursor.Engine.Plot;

/// <summary>Fonction numérique d'une variable, compilée depuis une saisie MathCursor.
/// <see cref="Evaluate"/> renvoie NaN hors du domaine (√ d'un négatif, ln 0…).</summary>
public sealed class CompiledFunction
{
    private readonly Func<double, double> _f;

    /// <summary>Nom de la variable (« x » par défaut, sinon la seule lettre libre : t, n…).</summary>
    public string Variable { get; }
    /// <summary>LaTeX de l'expression tracée (membre droit d'un « f(x) = … »).</summary>
    public string Latex { get; }
    /// <summary>Nom de la fonction si la saisie la nomme (« f » dans f(x) = …), sinon null.</summary>
    public string? Name { get; }

    internal CompiledFunction(Func<double, double> f, string variable, string latex, string? name)
    { _f = f; Variable = variable; Latex = latex; Name = name; }

    public double Evaluate(double x)
    {
        double y = _f(x);
        return double.IsInfinity(y) ? double.NaN : y;
    }
}

// Arbre de la forêt → délégué. PUR, sans dépendance. Ne connaît que les clés du
// vocabulaire (data/engine/symbols.json) ; tout le reste échoue avec une raison
// (jamais une courbe fausse). ADR 2026-10-31-Feat-insert-curves.
internal static class FunctionCompiler
{
    private sealed class Fail : Exception { public Fail(string m) : base(m) { } }

    // Clé canonique d'un symbole : le nœud garde l'alias tapé (× pour *, ** pour ^),
    // on suit les « sameAs » de symbols.json jusqu'à l'entrée de base.
    private static readonly Dictionary<string, string> SameAs = LoadSameAs();

    private static Dictionary<string, string> LoadSameAs()
    {
        var map = new Dictionary<string, string>();
        foreach (var kv in EngineData.Obj(EngineData.Obj(EngineData.Load("symbols.json"))["symbols"]))
            if (EngineData.Obj(kv.Value).TryGetValue("sameAs", out var s)) map[kv.Key] = EngineData.Str(s);
        return map;
    }

    private static string? Canon(string? sym)
    {
        for (int i = 0; sym != null && i < 8 && SameAs.TryGetValue(sym, out var next); i++) sym = next;
        return sym;
    }

    private static readonly Dictionary<string, Func<double, double>> Unary = new()
    {
        ["neg"] = v => -v, ["pos"] = v => v,
        ["sin"] = Math.Sin, ["cos"] = Math.Cos, ["tan"] = Math.Tan,
        ["arcsin"] = Math.Asin, ["arccos"] = Math.Acos, ["arctan"] = Math.Atan,
        ["sinh"] = Math.Sinh, ["cosh"] = Math.Cosh, ["tanh"] = Math.Tanh,
        ["ln"] = v => v > 0 ? Math.Log(v) : double.NaN,
        ["log"] = v => v > 0 ? Math.Log10(v) : double.NaN,
        ["exp"] = Math.Exp,
        ["sqrt"] = v => v >= 0 ? Math.Sqrt(v) : double.NaN,
        ["abs"] = Math.Abs,
        ["floor"] = Math.Floor, ["ceil"] = Math.Ceiling,
    };

    public static bool TryCompile(Node root, string latex, out CompiledFunction? fn, out string? error)
    {
        fn = null; error = null;
        try
        {
            var (body, name, declared) = StripLhs(root);
            var free = new SortedSet<string>(StringComparer.Ordinal);
            CollectLetters(body, free);
            if (declared != null) free.Add(declared);
            if (free.Count > 1) throw new Fail("plusieurs variables : " + string.Join(", ", free));
            string v = free.Count == 1 ? free.First() : (declared ?? "x");
            var f = Compile(body, v);
            string lx = ReferenceEquals(body, root) ? latex : LatexRenderer.Render(body, EngineCulture.Fr);
            fn = new CompiledFunction(f, v, lx, name);
            return true;
        }
        catch (Fail e) { error = e.Message; return false; }
    }

    // « f(x) = … », « y = … » → membre droit. Les autres relations échouent.
    private static (Node Body, string? Name, string? Var) StripLhs(Node n)
    {
        while (n.Type == "paren") n = n.Parts![0];
        if (n.Type != "infix" || Canon(n.Sym) != "=") return (n, null, null);
        var lhs = n.Parts![0]; var rhs = n.Parts[n.Parts.Count - 1];
        if (n.Parts.Count != 2) throw new Fail("chaîne d'égalités");
        if (lhs.Type == "atom" && lhs.Sym == "y") return (rhs, null, null);
        if (lhs.Type == "infix" && lhs.Sym == "·apply" && lhs.Parts![0].Type == "atom")
        {
            var arg = lhs.Parts[1];
            while (arg.Type == "paren") arg = arg.Parts![0];
            if (arg.Type == "atom" && IsLetter(arg.Sym)) return (rhs, lhs.Parts[0].Sym, arg.Sym);
        }
        throw new Fail("équation, pas une fonction");
    }

    private static bool IsLetter(string? s) => s is { Length: 1 } && char.IsLetter(s[0]) && s != "e";
    private static bool IsWord(string? s) => s is { Length: > 0 } && s.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');

    private static void CollectLetters(Node n, ISet<string> acc)
    {
        if (n.Type == "atom")
        {
            // « xe » (xe^x) : un atome de plusieurs lettres est un produit de lettres
            if (IsWord(n.Sym)) foreach (char c in n.Sym!) if (c != 'e') acc.Add(c.ToString());
            return;
        }
        // tête d'application (g dans g(x)) : pas une variable, Product tranchera
        if (n.Type == "infix" && n.Sym == "·apply" && n.Parts![0].Type == "atom") { CollectLetters(n.Parts[1], acc); return; }
        foreach (var c in n.EffectiveParts()) CollectLetters(c, acc);
    }

    private static Func<double, double> Compile(Node n, string v)
    {
        switch (n.Type)
        {
            case "atom": return Atom(n, v);
            case "paren": return Compile(n.Parts![0], v);
            case "prefix":
            {
                var sym = Canon(n.Sym);
                if (sym == null || !Unary.TryGetValue(sym, out var op)) throw new Fail("fonction non prise en charge : " + n.Sym);
                var a = Compile(n.Parts![0], v);
                return x => op(a(x));
            }
            case "postfix":
            {
                var a = Compile(n.Parts![0], v);
                if (Canon(n.Sym) == "%") return x => a(x) / 100;
                throw new Fail("opération non prise en charge : " + n.Sym);
            }
            case "nary":
                if (Canon(n.Sym) == "root" && n.Parts!.Count == 2)
                {
                    var k = Compile(n.Parts[0], v); var a = Compile(n.Parts[1], v);
                    return x => Root(a(x), k(x));
                }
                throw new Fail("non traçable : " + n.Sym);
            case "infix":
                return Infix(n, v);
        }
        throw new Fail("non traçable : " + n.Type);
    }

    private static Func<double, double> Infix(Node n, string v)
    {
        var ps = n.Parts!;
        var sym = Canon(n.Sym);
        if (sym == "·apply") return Product(ps, v);
        // « xe^x » : l'exposant ne coiffe que la dernière lettre (rendu xe^{x} = x·eˣ)
        if (sym == "^" && ps[0].Type == "atom" && !ps[0].Grouped && IsWord(ps[0].Sym) && ps[0].Sym!.Length > 1)
        {
            string w = ps[0].Sym!;
            var rest = Atom(new Node { Type = "atom", Sym = w.Substring(0, w.Length - 1) }, v);
            var last = Atom(new Node { Type = "atom", Sym = w.Substring(w.Length - 1) }, v);
            var ex = Compile(ps[1], v);
            return x => rest(x) * Pow(last(x), ex(x));
        }
        var fs = ps.Select(p => Compile(p, v)).ToList();
        Func<double, double> a = fs[0], b = fs.Count > 1 ? fs[1] : (_ => double.NaN);
        switch (sym)
        {
            case "+": case "-": return Chain(n, v, "+", "-");
            case "*": case ".": case "·div": return Chain(n, v, "*", ".", "·div");
            case "/": return Fold(fs, (p, q) => p / q);
            case "^": return x => Pow(a(x), b(x));
            case "·sci": return x => a(x) * Math.Pow(10, -b(x));
        }
        throw new Fail("opération non prise en charge : " + n.Sym);
    }

    // Chaîne de même niveau (a − b + c, a × b ÷ c) évaluée dans l'ORDRE AFFICHÉ, de gauche
    // à droite : l'arbre peut grouper a − (b + c) sans parenthèses visibles (le rendu ne
    // parenthèse pas un enfant de même précédence), ce que l'élève lit a − b + c.
    private static Func<double, double> Chain(Node n, string v, params string[] ops)
    {
        var seq = new List<(string Op, Node Term)>();
        void Lin(Node m, string op)
        {
            var s = Canon(m.Sym);
            if (m.Type == "infix" && !m.Grouped && s != null && Array.IndexOf(ops, s) >= 0 && m.Parts!.Count >= 2)
            {
                Lin(m.Parts[0], op);
                for (int i = 1; i < m.Parts.Count; i++) Lin(m.Parts[i], s);
            }
            else seq.Add((op, m));
        }
        // la racine se déplie toujours, même groupée (« (x+1) ») : ses parenthèses
        // l'entourent sans couper la chaîne (sinon Lin la renverrait telle quelle, en boucle)
        Lin(n.Parts![0], "+");
        for (int i = 1; i < n.Parts.Count; i++) Lin(n.Parts[i], Canon(n.Sym)!);
        var fs = seq.Select(t => (t.Op, F: Compile(t.Term, v))).ToList();
        return x =>
        {
            double acc = fs[0].F(x);
            for (int i = 1; i < fs.Count; i++)
            {
                double y = fs[i].F(x);
                acc = fs[i].Op switch { "+" => acc + y, "-" => acc - y, "·div" => acc / y, _ => acc * y };
            }
            return acc;
        };
    }

    private static Func<double, double> Fold(List<Func<double, double>> fs, Func<double, double, double> op) =>
        x => { double acc = fs[0](x); for (int i = 1; i < fs.Count; i++) acc = op(acc, fs[i](x)); return acc; };

    // Application f(x) dans le corps : une lettre devant une parenthèse est un produit
    // (x(x+1)), une fonction inconnue (g(x)) n'est pas traçable.
    private static Func<double, double> Product(List<Node> ps, string v)
    {
        var head = ps[0];
        if (head.Type == "atom" && IsLetter(head.Sym) && head.Sym != v) throw new Fail("fonction inconnue : " + head.Sym);
        var a = Compile(head, v); var b = Compile(ps[1], v);
        return x => a(x) * b(x);
    }

    private static Func<double, double> Atom(Node n, string v)
    {
        if (n.Hole) throw new Fail("expression incomplète");
        string s = (n.Sym ?? "").Trim();
        if (s == v) return x => x;
        if (s is "\\pi" or "π") return _ => Math.PI;
        if (s == "e") return _ => Math.E;
        string num = s.Replace("{,}", ".").Replace(",", ".").Replace("\\,", "").Replace(" ", "");
        if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double c)) return _ => c;
        if (IsWord(s) && s.Length > 1 && s.All(ch => ch.ToString() == v || ch == 'e'))
        {
            int k = s.Count(ch => ch.ToString() == v), ne = s.Length - k;
            return x => Math.Pow(x, k) * Math.Pow(Math.E, ne);
        }
        throw new Fail("symbole non pris en charge : " + s);
    }

    // Puissance réelle : base négative et exposant rationnel à dénominateur impair (x^(1/3)).
    private static double Pow(double b, double e)
    {
        if (b >= 0 || e == Math.Floor(e)) return Math.Pow(b, e);
        for (int q = 3; q <= 15; q += 2)
        {
            double p = e * q;
            if (Math.Abs(p - Math.Round(p)) < 1e-9)
                return ((long)Math.Round(p) % 2 == 0 ? 1 : -1) * Math.Pow(-b, e);
        }
        return double.NaN;
    }

    private static double Root(double a, double k)
    {
        if (k == Math.Floor(k) && k > 0 && ((long)k) % 2 == 1) return Math.Sign(a) * Math.Pow(Math.Abs(a), 1 / k);
        return a >= 0 ? Math.Pow(a, 1 / k) : double.NaN;
    }

    /// <summary>Arbre lisible (tests et diagnostic).</summary>
    internal static string Dump(Node n)
    {
        var sb = new StringBuilder();
        sb.Append(n.Type).Append(':').Append(n.Sym);
        if (n.Implicit) sb.Append("·imp");
        var ps = n.EffectiveParts();
        if (ps.Count > 0) sb.Append('[').Append(string.Join(" ; ", ps.Select(Dump))).Append(']');
        return sb.ToString();
    }
}
