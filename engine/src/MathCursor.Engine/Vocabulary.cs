using System.Collections.Generic;

namespace MathCursor.Engine;

// Fonction de rendu d'un symbole : parties déjà rendues + le nœud (certains
// symboles lisent n.implicit / n.parts / n.spaced). Postfixes : appelés avec
// une liste à 1 élément et n = null (cf. LatexRenderer).
internal delegate string RenderFn(IReadOnlyList<string> a, Node? n);

internal sealed class VocabEntry
{
    public string? Shape;          // "infix" | "prefix" | "nary" | "postfix" | "atom"
    public int Arity;
    public string? Class;          // WEAK | STRONG
    public double Looseness;
    public bool Bracketed;
    public bool Cut;
    public bool Implicit;
    public bool Sup;
    public bool Sub;
    public bool Tight;
    public bool List;
    public bool Mapping;
    public bool WordSpace;
    public bool UnitWord;
    public bool UnitOp;
    public bool Apply;
    public string? Unary;          // symbole à émettre en position d'opérande
    public string? PostSign;       // LaTeX de l'exposant quand le signe est terminal
    public string? Lower;          // atom
    public string? Upper;          // atom
    public List<string>? Alts;     // atom/infix : lectures multiples
    public string? Coh;            // groupe de cohérence
    public RenderFn? Render;

    public VocabEntry Clone() => (VocabEntry)MemberwiseClone();
}

// vocabulary.js — LE SEUL fichier qui connaît des opérateurs concrets.
internal static class Vocabulary
{
    public const string WEAK = "WEAK";
    public const string STRONG = "STRONG";

    // looseness : REL au-dessus de tout, SUM lâche, QUANT entre PROD et SUM,
    // PROD/POW serrés, APP (fonctions/décorations) très serré.
    private const double REL = 5, SUM = 3, QUANT = 2.5, PROD = 2, POW = 1, APP = 0;
    private const string MUL = "\\times ";

    public static readonly Dictionary<string, VocabEntry> Vocab = new();
    public static readonly Dictionary<char, int> Sep = new() { [','] = 0, [';'] = 2 };
    public static readonly HashSet<string> Splittable = new();
    public static readonly Dictionary<string, string> Role = new();

    public static double Loose(string? sym) =>
        sym != null && Vocab.TryGetValue(sym, out var v) ? v.Looseness : 0;

    // ── factories ──────────────────────────────────────────────────────────
    private static VocabEntry Fn(string tex) => new()
    { Shape = "prefix", Arity = 1, Class = STRONG, Looseness = APP, Render = (a, _) => $"{tex}({a[0]})" };

    private static VocabEntry Quant(string tex) => new()
    { Shape = "prefix", Arity = 1, Class = STRONG, Looseness = APP, List = true, Render = (a, _) => $"{tex} {a[0]}" };

    private static VocabEntry Deco(string wide, string narrow) => new()
    {
        Shape = "prefix", Arity = 1, Class = STRONG, Looseness = APP, Tight = true,
        Render = (a, _) => a[0].Length > 1 ? $"\\{wide}{{{a[0]}}}" : $"\\{narrow}{{{a[0]}}}",
    };

    private static VocabEntry Lit(string tex) => new() { Shape = "atom", Lower = tex, Upper = tex };

    private static VocabEntry Op(string tex) => new()
    { Shape = "prefix", Arity = 1, Class = STRONG, Looseness = APP, Render = (a, _) => $"{tex} {a[0]}" };

    private static VocabEntry Rel(string cmd) => new()
    { Shape = "infix", Arity = 2, Class = WEAK, Looseness = REL, Cut = true, Render = (a, _) => $"{a[0]}{cmd} {a[1]}" };

    private static VocabEntry SetOp(string cmd, double loose) => new()
    { Shape = "infix", Arity = 2, Class = WEAK, Looseness = loose, Render = (a, _) => $"{a[0]}{cmd} {a[1]}" };

    private static VocabEntry Grk(string name, string? upper = null) => new()
    { Shape = "atom", Lower = $"\\{name} ", Upper = upper ?? $"\\{name} " };

    private static VocabEntry Set(string l) => new()
    { Shape = "atom", Alts = new() { l, $"\\mathbb{{{l}}} " }, Coh = "set" };

    private static VocabEntry Infix(double loose, string cls, RenderFn render,
        bool cut = false, bool bracketed = false, bool implicit_ = false,
        bool sup = false, bool sub = false, bool mapping = false,
        string? unary = null, string? postSign = null) => new()
    {
        Shape = "infix", Arity = 2, Class = cls, Looseness = loose, Cut = cut,
        Bracketed = bracketed, Implicit = implicit_, Sup = sup, Sub = sub,
        Mapping = mapping, Unary = unary, PostSign = postSign, Render = render,
    };

    private static VocabEntry Prefix(RenderFn render, bool tight = false) => new()
    { Shape = "prefix", Arity = 1, Class = STRONG, Looseness = APP, Tight = tight, Render = render };

    private static VocabEntry Postfix(RenderFn render) => new()
    { Shape = "postfix", Class = STRONG, Looseness = POW, Render = render };

    private static VocabEntry Nary(int arity, double loose, RenderFn render) => new()
    { Shape = "nary", Arity = arity, Class = STRONG, Looseness = loose, Render = render };

    static Vocabulary()
    {
        // ── GREEK (atomes-feuilles ; render.js ressort n.Sym tel quel) ──────
        var greek = new (string Name, string? Upper)[]
        {
            ("alpha", null), ("beta", null), ("gamma", "\\Gamma "),
            ("delta", "\\Delta "), ("epsilon", null), ("zeta", null),
            ("eta", null), ("theta", "\\Theta "), ("iota", null),
            ("kappa", null), ("lambda", "\\Lambda "), ("mu", null),
            ("nu", null), ("xi", "\\Xi "), ("omicron", null),
            ("pi", "\\Pi "), ("rho", null), ("sigma", "\\Sigma "),
            ("tau", null), ("upsilon", "\\Upsilon "), ("phi", "\\Phi "),
            ("chi", null), ("psi", "\\Psi "), ("omega", "\\Omega "),
        };
        foreach (var (name, upper) in greek) { Vocab[name] = Grk(name, upper); Splittable.Add(name); }
        foreach (var f in new[] { "cos", "sin", "tan", "arcsin", "arccos", "arctan", "ln", "log", "exp" })
            Splittable.Add(f);

        // ── SETS (atomes ambigus : variable | \mathbb) ; priment sur N/C unités
        foreach (var s in new[] { "R", "N", "Z", "Q", "C" }) Vocab[s] = Set(s);

        // ── UNITS (mots-unités, actifs seulement après un nombre) ───────────
        foreach (var w in Units.Words) Vocab[w] = new VocabEntry { UnitWord = true };

        // ── connecteurs internes ────────────────────────────────────────────
        Vocab["·unit"] = new VocabEntry
        {
            Shape = "infix", Arity = 2, Class = STRONG, Looseness = APP, UnitOp = true, Sup = false,
            Render = (a, n) => $"{a[0]}{(n is { Spaced: true } ? "\\," : "")}\\mathrm{{{a[1]}}}",
        };
        // sticky représenté via le token, pas le vocab ; mais ·unit est sticky côté token.

        // ── arithmétique ──────────────────────────────────────────────────
        Vocab["+"] = Infix(SUM, WEAK, (a, _) => $"{a[0]}+{a[1]}", unary: "pos", postSign: "+");
        Vocab["-"] = Infix(SUM, WEAK, (a, _) => $"{a[0]}-{a[1]}", unary: "neg", postSign: "-");
        Vocab["*"] = Infix(PROD, STRONG, (a, n) => n is { Implicit: true } ? $"{a[0]}{a[1]}" : $"{a[0]}{MUL}{a[1]}",
            implicit_: true, postSign: "\\ast ");
        Vocab["·apply"] = new VocabEntry
        { Shape = "infix", Arity = 2, Class = STRONG, Looseness = APP, Apply = true, Render = (a, _) => $"{a[0]}{a[1]}" };
        Vocab["."] = Infix(PROD, STRONG, (a, _) => $"{a[0]}\\cdot {a[1]}");
        Vocab["/"] = Infix(PROD, STRONG,
            (a, n) => n is { Parts: { Count: > 1 } p } && p[1].Type == "set" ? $"{a[0]}\\setminus {a[1]}" : $"\\frac{{{a[0]}}}{{{a[1]}}}",
            bracketed: true);
        Vocab["^"] = Infix(POW, STRONG, (a, _) => $"{a[0]}^{{{a[1]}}}", sup: true, unary: "hat");
        Vocab["_"] = Infix(POW, STRONG, (a, _) => $"{a[0]}_{{{a[1]}}}", sub: true);

        // ── postfixes ──────────────────────────────────────────────────────
        Vocab["!"] = Postfix((a, _) => $"{a[0]}!");
        Vocab["'"] = Postfix((a, _) => $"{a[0]}'");
        Vocab["°"] = Postfix((a, _) => $"{a[0]}^{{\\circ}}");
        Vocab["°C"] = Postfix((a, _) => $"{a[0]}^{{\\circ}}\\mathrm{{C}}");
        Vocab["°F"] = Postfix((a, _) => $"{a[0]}^{{\\circ}}\\mathrm{{F}}");

        // ── relations (REL, cut) ───────────────────────────────────────────
        Vocab["~"] = Infix(REL, WEAK, (a, _) => $"{a[0]}\\sim {a[1]}", cut: true);
        Vocab["="] = Infix(REL, WEAK, (a, _) => $"{a[0]}={a[1]}", cut: true);
        Vocab["<"] = Infix(REL, WEAK, (a, _) => $"{a[0]}<{a[1]}", cut: true);
        Vocab[">"] = Infix(REL, WEAK, (a, _) => $"{a[0]}>{a[1]}", cut: true);
        Vocab["!="] = Infix(REL, WEAK, (a, _) => $"{a[0]}\\neq {a[1]}", cut: true);
        Vocab[">="] = Infix(REL, WEAK, (a, _) => $"{a[0]}\\geq {a[1]}", cut: true);
        Vocab["<="] = Infix(REL, WEAK, (a, _) => $"{a[0]}\\leq {a[1]}", cut: true);
        Vocab["->"] = Infix(REL, WEAK, (a, _) => $"{a[0]}\\to {a[1]}", cut: true, mapping: true);

        // ":" ambigu (alts) → ·div | ·colon
        Vocab[":"] = new VocabEntry { Shape = "infix", Alts = new() { "·div", "·colon" } };
        Vocab["·div"] = Infix(PROD, STRONG, (a, _) => $"{a[0]}\\div {a[1]}");
        Vocab["·colon"] = Infix(REL, WEAK, (a, _) => $"{a[0]}\\colon {a[1]}", cut: true);
        Vocab["·mid"] = Infix(REL, WEAK, (a, _) => $"{a[0]}\\mid {a[1]}", cut: true);

        // ── unaires ────────────────────────────────────────────────────────
        Vocab["neg"] = Prefix((a, n) => Loose(n!.Parts![0].Sym) >= SUM ? $"-({a[0]})" : $"-{a[0]}");
        Vocab["pos"] = Prefix((a, n) => Loose(n!.Parts![0].Sym) >= SUM ? $"+({a[0]})" : $"+{a[0]}");

        // ── fonctions ──────────────────────────────────────────────────────
        Vocab["cos"] = Fn("\\cos"); Vocab["sin"] = Fn("\\sin"); Vocab["tan"] = Fn("\\tan");
        Vocab["arcsin"] = Fn("\\arcsin"); Vocab["arccos"] = Fn("\\arccos"); Vocab["arctan"] = Fn("\\arctan");
        Vocab["ln"] = Fn("\\ln"); Vocab["log"] = Fn("\\log");
        Vocab["sinh"] = Fn("\\sinh"); Vocab["cosh"] = Fn("\\cosh"); Vocab["tanh"] = Fn("\\tanh"); Vocab["coth"] = Fn("\\coth");
        Vocab["exp"] = Prefix((a, _) => $"e^{{{a[0]}}}");
        Vocab["arg"] = Fn("\\arg"); Vocab["Re"] = Fn("\\operatorname{Re}"); Vocab["Im"] = Fn("\\operatorname{Im}");
        Vocab["bar"] = Deco("overline", "bar");
        Vocab["vec"] = Deco("overrightarrow", "vec");
        Vocab["hat"] = Deco("widehat", "hat");
        Vocab["norm"] = Prefix((a, _) => $"\\left\\|{a[0]}\\right\\|");
        Vocab["sqrt"] = Prefix((a, _) => $"\\sqrt{{{a[0]}}}");
        Vocab["root"] = Nary(2, APP, (a, _) => $"\\sqrt[{a[0]}]{{{a[1]}}}");
        Vocab["floor"] = Prefix((a, _) => $"\\lfloor {a[0]}\\rfloor ");
        Vocab["ceil"] = Prefix((a, _) => $"\\lceil {a[0]}\\rceil ");

        // ── quantificateurs / logique ──────────────────────────────────────
        Vocab["forall"] = Quant("\\forall"); Vocab["exists"] = Quant("\\exists");
        Vocab["nexists"] = Quant("\\nexists"); Vocab["not"] = Quant("\\neg");

        Vocab["in"] = Rel("\\in"); Vocab["notin"] = Rel("\\notin");
        Vocab["subset"] = Rel("\\subset"); Vocab["subseteq"] = Rel("\\subseteq");
        Vocab["supset"] = Rel("\\supset"); Vocab["supseteq"] = Rel("\\supseteq");
        Vocab["notsubset"] = Rel("\\not\\subset");
        Vocab["and"] = Rel("\\wedge"); Vocab["or"] = Rel("\\vee");
        Vocab["equiv"] = Rel("\\equiv"); Vocab["cong"] = Rel("\\cong");
        Vocab["approx"] = Rel("\\approx"); Vocab["propto"] = Rel("\\propto");

        Vocab["union"] = SetOp("\\cup", SUM); Vocab["inter"] = SetOp("\\cap", PROD);
        Vocab["setminus"] = SetOp("\\setminus", SUM);
        Vocab["emptyset"] = Lit("\\emptyset "); Vocab["inf"] = Lit("\\infty ");

        Vocab["partial"] = Prefix((a, _) => $"\\partial {a[0]}", tight: true);
        Vocab["nabla"] = Prefix((a, _) => $"\\nabla {a[0]}", tight: true);
        Vocab["det"] = Op("\\det"); Vocab["dim"] = Op("\\dim");
        Vocab["dot"] = Nary(2, APP, (a, _) => $"\\langle {a[0]},{a[1]}\\rangle ");

        // ── n-aires (quantificateurs scopants) ──────────────────────────────
        Vocab["lim"] = Nary(3, QUANT, (a, _) => $"\\lim_{{{a[0]}\\to {a[1]}}} {a[2]}");
        Vocab["sum"] = Nary(4, QUANT, (a, _) => $"\\sum_{{{a[0]}={a[1]}}}^{{{a[2]}}} {a[3]}");
        Vocab["prod"] = Nary(4, QUANT, (a, _) => $"\\prod_{{{a[0]}={a[1]}}}^{{{a[2]}}} {a[3]}");
        Vocab["int"] = Nary(4, QUANT, (a, _) => $"\\int_{{{a[0]}}}^{{{a[1]}}} {a[2]} \\, d{a[3]}");
        Vocab["iint"] = Nary(5, QUANT, (a, _) => $"\\iint_{{{a[0]}}}^{{{a[1]}}} {a[2]} \\, d{a[3]}\\,d{a[4]}");
        Vocab["iiint"] = Nary(6, QUANT, (a, _) => $"\\iiint_{{{a[0]}}}^{{{a[1]}}} {a[2]} \\, d{a[3]}\\,d{a[4]}\\,d{a[5]}");
        Vocab["binom"] = Nary(2, APP, (a, _) => $"\\binom{{{a[0]}}}{{{a[1]}}}");

        // ── opérateurs-mots infixes ─────────────────────────────────────────
        Vocab["mod"] = Infix(PROD, STRONG, (a, _) => $"{a[0]} \\bmod {a[1]}");
        Vocab["perp"] = Infix(SUM, WEAK, (a, _) => $"{a[0]} \\perp {a[1]}");
        Vocab["circ"] = Infix(PROD, STRONG, (a, _) => $"{a[0]}\\circ {a[1]}");
        Vocab["pm"] = Infix(SUM, WEAK, (a, _) => $"{a[0]}\\pm {a[1]}", unary: "upm");
        Vocab["mp"] = Infix(SUM, WEAK, (a, _) => $"{a[0]}\\mp {a[1]}", unary: "ump");
        Vocab["upm"] = Prefix((a, _) => $"\\pm {a[0]}");
        Vocab["ump"] = Prefix((a, _) => $"\\mp {a[0]}");
        Vocab["parallel"] = Infix(SUM, WEAK, (a, _) => $"{a[0]} \\mathbin{{/\\!/}} {a[1]}");
        Vocab["mapsto"] = Infix(REL, WEAK, (a, _) => $"{a[0]}\\mapsto {a[1]}", cut: true, mapping: true);

        // ── ALIAS ────────────────────────────────────────────────────────────
        Vocab["somme"] = Vocab["som"] = Vocab["sum"];
        Vocab["racine"] = Vocab["rac"] = Vocab["sqrt"];
        Vocab["racn"] = Vocab["root"];
        Vocab["cup"] = Vocab["U"] = Vocab["Union"] = Vocab["union"];
        Vocab["cap"] = Vocab["Inter"] = Vocab["inter"];
        Vocab["pourtout"] = Vocab["forall"];
        var v = Vocab["forall"].Clone(); v.WordSpace = true; Vocab["V"] = v;
        Vocab["ilexiste"] = Vocab["existe"] = Vocab["exist"] = Vocab["exists"];
        Vocab["nexiste"] = Vocab["nexist"] = Vocab["nexists"];
        Vocab["congr"] = Vocab["congru"] = Vocab["cong"];
        Vocab["dans"] = Vocab["appartient"] = Vocab["appt"] = Vocab["app"] = Vocab["in"];
        Vocab["inclus"] = Vocab["incl"] = Vocab["inclu"] = Vocab["subset"];
        Vocab["pasinclus"] = Vocab["pasincl"] = Vocab["pasinclu"] = Vocab["notsubset"];
        Vocab["pasdans"] = Vocab["nappartient"] = Vocab["napp"] = Vocab["notin"];
        Vocab["rond"] = Vocab["circ"];
        Vocab["conj"] = Vocab["bar"];

        // ── ROLE : rôle de jonction → symbole (aucun opérateur nommé en dur) ─
        foreach (var kv in Vocab)
        {
            var e = kv.Value;
            if (e.Implicit) Role["implicit"] = kv.Key;
            if (e.Sup) Role["sup"] = kv.Key;
            if (e.Sub) Role["sub"] = kv.Key;
            if (e.UnitOp) Role["unitOp"] = kv.Key;
            if (e.Apply) Role["apply"] = kv.Key;
        }
    }
}
