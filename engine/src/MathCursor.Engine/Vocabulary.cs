// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
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
using System.Linq;
using System.Text.RegularExpressions;

namespace MathCursor.Engine;

// Fonction de rendu d'un symbole : parties déjà rendues + le nœud (certains
// symboles lisent n.implicit / n.parts / n.spaced). Postfixes : appelés avec
// une liste à 1 élément et n = null (cf. LatexRenderer).
internal delegate string RenderFn(IReadOnlyList<string> a, Node? n);

// Variante d'arité d'un n-aire (forme courte : moins d'args que l'arité
// canonique). Jamais complétée par des trous (ADR 2026-06-11 nary-arity-
// variants) ; Accept = guard sur les args parsés (null = toujours accepté).
internal sealed class NaryVariant
{
    public int Arity;
    public RenderFn Render = default!;
    public Func<IReadOnlyList<Node>, bool>? Accept;
}

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
    public List<string>? AltsUpper;// atom : lectures multiples en MAJUSCULE (raccourci grec @T/@P)
    public string? Coh;            // groupe de cohérence
    public RenderFn? Render;
    public List<NaryVariant>? Variants; // nary : arités courtes (Arity/Render = canonique)

    public RenderFn RenderFor(int argc)
    {
        if (Variants != null)
            foreach (var v in Variants)
                if (v.Arity == argc) return v.Render;
        return Render!;
    }

    public VocabEntry Clone() => (VocabEntry)MemberwiseClone();
}

// vocabulary.js — LE SEUL fichier qui connaît des opérateurs concrets.
// La TABLE est externalisée dans data/engine/symbols.json (source unique partagée
// avec le port Python, cf. ADR portable-engine-universal-vocab) ; ce fichier en est
// le BUILDER + le registre des logiques non-déclaratives (render-hooks, guards).
internal static class Vocabulary
{
    public const string WEAK = "WEAK";
    public const string STRONG = "STRONG";

    // looseness : REL au-dessus de tout, SUM lâche, QUANT entre PROD et SUM,
    // PROD/POW serrés, APP (fonctions/décorations) très serré. Ces consts servent
    // les hooks/guards (code) ; les valeurs sont aussi en data (symbols.json const)
    // pour les entrées — les deux DOIVENT coïncider.
    private const double REL = 5, SUM = 3, QUANT = 2.5, PROD = 2, POW = 1, APP = 0;

    // Tables remplies une seule fois au cctor, exposées en LECTURE SEULE
    // (ADR 2026-06-22 — durcissement audit : pas de corruption d'état partagé
    // entre analyses). netstandard2.0 n'a pas IReadOnlySet → Splittable est
    // encapsulé derrière IsSplittable (hot-path lexer, O(1)) + SplittableTokens.
    private static readonly Dictionary<string, VocabEntry> _vocab = new();
    public static IReadOnlyDictionary<string, VocabEntry> Vocab => _vocab;
    private static readonly Dictionary<char, int> _sep = new() { [','] = 0, [';'] = 2 };
    public static IReadOnlyDictionary<char, int> Sep => _sep;
    private static readonly HashSet<string> _splittable = new();
    public static bool IsSplittable(string s) => _splittable.Contains(s);
    public static IEnumerable<string> SplittableTokens => _splittable;
    private static readonly Dictionary<string, string> _role = new();
    public static IReadOnlyDictionary<string, string> Role => _role;

    // Sets d'alias (mot saisi → clé canonique de Vocab) par culture :
    // générique + langue, fusionnés une fois au cctor, jamais mutés ensuite.
    // Consommés par le lexer via EngineCulture.Aliases/Canon.
    internal static readonly IReadOnlyDictionary<string, string> AliasesFr;
    internal static readonly IReadOnlyDictionary<string, string> AliasesUs;

    private static readonly Dictionary<string, double> Loosenesses;

    public static double Loose(string? sym) =>
        sym != null && Vocab.TryGetValue(sym, out var v) ? v.Looseness : 0;

    // ── guards des variantes courtes ────────────────────────────────────────
    // Atome purement numérique : Sym commence par un chiffre (couvre « 1{,}5 »).
    private static bool Numeric(Node n) =>
        n.Type == "atom" && !n.Hole && n.Sym is { Length: > 0 } s && char.IsDigit(s[0]);
    // Corps d'un sum/prod/lim court : tout sauf un nombre nu (protège la route
    // de frappe vers la forme pleine « sum k 1 n f(k) »).
    private static bool NonNumeric(Node n) => !Numeric(n);
    // Arg différentiel d'une intégrale indéfinie : un NOM atomique (dx, dt…),
    // pas un nombre — sinon « int 0 1 » rendrait \int 0 \, d1. Un TROU passe :
    // un arg pas-encore-tapé n'est pas invalide (squelette court en popup,
    // ADR 2026-06-12 nary-skeleton-pair).
    private static bool NameAtom(Node n) =>
        n.Hole || (n.Type == "atom" && n.Sym is { Length: > 0 } s && !char.IsDigit(s[0]));
    // Corps d'un lim court : doit lier plus serré que le quantificateur —
    // « lim x +inf » reste le squelette \lim_{x\to+\infty} □ (le + explicite
    // rend « x+∞ » parsable d'un bloc), et \lim x+\infty s'afficherait comme
    // (\lim x)+\infty, contresens.
    private static bool TightBody(Node n) =>
        NonNumeric(n) && !(n.Type == "infix" && Loose(n.Sym) >= QUANT);

    static Vocabulary()
    {
        var root = EngineData.Obj(EngineData.Load("symbols.json"));
        Loosenesses = new Dictionary<string, double>();
        foreach (var kv in EngineData.Obj(EngineData.Obj(root["const"])["looseness"]))
            Loosenesses[kv.Key] = EngineData.Num(kv.Value);
        var symbols = EngineData.Obj(root["symbols"]);

        // Pass 1 : entrées directes (hors sameAs).
        foreach (var kv in symbols)
        {
            var j = EngineData.Obj(kv.Value);
            if (!j.ContainsKey("sameAs")) _vocab[kv.Key] = BuildEntry(j);
        }
        // Pass 2 : sameAs = clone d'une entrée déjà construite + overrides
        // (ex. ·forallWord = forall + wordSpace ; ≤ = <= ; × = * ; …).
        foreach (var kv in symbols)
        {
            var j = EngineData.Obj(kv.Value);
            if (!j.ContainsKey("sameAs")) continue;
            var e = _vocab[EngineData.Str(j["sameAs"])].Clone();
            if (j.ContainsKey("wordSpace")) e.WordSpace = EngineData.Bool(j["wordSpace"]);
            _vocab[kv.Key] = e;
        }

        // Mots-unités (générés depuis Units.Words / units.json). Un SYMBOLE prime
        // sur l'unité homonyme (ex. « bar » = décoration \bar, pas le bar pression) :
        // on ne remplit que les clés non déjà définies (l'original définissait les
        // symboles APRÈS la boucle units, qui les écrasait — même invariant).
        foreach (var w in Units.Words)
            if (!_vocab.ContainsKey(w)) _vocab[w] = new VocabEntry { UnitWord = true };

        // Splittable (liste data — inclut « conj », alias sans entrée Vocab).
        foreach (var w in EngineData.Arr(root["splittable"])) _splittable.Add(EngineData.Str(w));

        // ── ALIAS (data/engine/cultures.json) + validation ─────────────────
        var aliasMaps = EngineData.Obj(EngineData.Obj(EngineData.Load("cultures.json"))["aliases"]);
        AliasesFr = AddPrefixAliases(MergeAliases(EngineData.StrMap(aliasMaps["generic"]), EngineData.StrMap(aliasMaps["fr"])));
        AliasesUs = AddPrefixAliases(MergeAliases(EngineData.StrMap(aliasMaps["generic"]), EngineData.StrMap(aliasMaps["en"])));

        // ── ROLE : rôle de jonction → symbole (aucun opérateur nommé en dur) ─
        foreach (var kv in _vocab)
        {
            var e = kv.Value;
            if (e.Implicit) _role["implicit"] = kv.Key;
            if (e.Sup) _role["sup"] = kv.Key;
            if (e.Sub) _role["sub"] = kv.Key;
            if (e.UnitOp) _role["unitOp"] = kv.Key;
            if (e.Apply) _role["apply"] = kv.Key;
        }
    }

    // ── builder d'une entrée depuis sa description JSON ──────────────────────
    private static VocabEntry BuildEntry(Dictionary<string, object?> j)
    {
        var e = new VocabEntry();
        if (j.TryGetValue("shape", out var sh)) e.Shape = EngineData.Str(sh);
        if (j.TryGetValue("arity", out var ar)) e.Arity = (int)EngineData.Num(ar);
        if (j.TryGetValue("class", out var cl)) e.Class = EngineData.Str(cl);
        if (j.TryGetValue("looseness", out var lo)) e.Looseness = Loosenesses[EngineData.Str(lo)];
        e.Bracketed = Flag(j, "bracketed");
        e.Cut = Flag(j, "cut");
        e.Implicit = Flag(j, "implicit");
        e.Sup = Flag(j, "sup");
        e.Sub = Flag(j, "sub");
        e.Tight = Flag(j, "tight");
        e.List = Flag(j, "list");
        e.Mapping = Flag(j, "mapping");
        e.WordSpace = Flag(j, "wordSpace");
        e.UnitWord = Flag(j, "unitWord");
        e.UnitOp = Flag(j, "unitOp");
        e.Apply = Flag(j, "apply");
        if (j.TryGetValue("unary", out var un)) e.Unary = EngineData.Str(un);
        if (j.TryGetValue("postSign", out var ps)) e.PostSign = EngineData.Str(ps);
        if (j.TryGetValue("lower", out var lw)) e.Lower = EngineData.Str(lw);
        if (j.TryGetValue("upper", out var up)) e.Upper = EngineData.Str(up);
        if (j.TryGetValue("alts", out var al)) e.Alts = StrList(al);
        if (j.TryGetValue("altsUpper", out var au)) e.AltsUpper = StrList(au);
        if (j.TryGetValue("coh", out var co)) e.Coh = EngineData.Str(co);

        if (j.TryGetValue("renderHook", out var rh))
        {
            e.Render = Hook(EngineData.Str(rh), j.TryGetValue("hookParams", out var hp) ? hp : null);
        }
        else if (j.TryGetValue("render", out var rd))
        {
            var tmpl = EngineData.Str(rd);
            if (j.TryGetValue("renderImplicit", out var ri))
            {
                var timpl = EngineData.Str(ri);
                e.Render = (a, n) => Fill(n is { Implicit: true } ? timpl : tmpl, a);
            }
            else
            {
                e.Render = (a, _) => Fill(tmpl, a);
            }
        }

        if (j.TryGetValue("variants", out var vs))
        {
            e.Variants = new List<NaryVariant>();
            foreach (var v in EngineData.Arr(vs))
            {
                var vo = EngineData.Obj(v);
                var tmpl = EngineData.Str(vo["render"]);
                var nv = new NaryVariant
                {
                    Arity = (int)EngineData.Num(vo["arity"]),
                    Render = (a, _) => Fill(tmpl, a),
                };
                if (vo.TryGetValue("acceptGuard", out var g)) nv.Accept = Guard(EngineData.Str(g));
                e.Variants.Add(nv);
            }
        }
        return e;
    }

    private static bool Flag(Dictionary<string, object?> j, string k) =>
        j.TryGetValue(k, out var v) && EngineData.Bool(v);

    private static List<string> StrList(object? o)
    {
        var list = new List<string>();
        foreach (var x in EngineData.Arr(o)) list.Add(EngineData.Str(x));
        return list;
    }

    // ── mini-langage de template : {0}..{5}, substitution SINGLE-PASS (la leçon
    // du spike : un .replace séquentiel re-scanne le texte substitué). Les
    // accolades littérales du LaTeX sont conservées telles quelles. ───────────
    private static readonly Regex Placeholder = new(@"\{([0-9])\}", RegexOptions.Compiled);
    private static string Fill(string tmpl, IReadOnlyList<string> a) =>
        Placeholder.Replace(tmpl, m => a[m.Value[1] - '0']);

    // ── render-hooks : la logique de rendu non-déclarative, référencée par nom ─
    private static RenderFn Hook(string name, object? hookParams)
    {
        switch (name)
        {
            case "frac_or_setminus":
                return (a, n) => n is { Parts: { Count: > 1 } p } && p[1].Type == "set"
                    ? $"{a[0]}\\setminus {a[1]}"
                    : $"\\frac{{{a[0]}}}{{{a[1]}}}";
            case "neg":
                return (a, n) => Loose(n!.Parts![0].Sym) >= SUM ? $"-({a[0]})" : $"-{a[0]}";
            case "pos":
                return (a, n) => Loose(n!.Parts![0].Sym) >= SUM ? $"+({a[0]})" : $"+{a[0]}";
            case "unit":
                return (a, n) => $"{a[0]}{(n is { Spaced: true } ? "\\," : "")}\\mathrm{{{a[1]}}}";
            case "deco":
            {
                var p = EngineData.Obj(hookParams);
                string wide = EngineData.Str(p["wide"]), narrow = EngineData.Str(p["narrow"]);
                return (a, _) => a[0].Length > 1 ? $"\\{wide}{{{a[0]}}}" : $"\\{narrow}{{{a[0]}}}";
            }
            default:
                throw new InvalidOperationException($"renderHook inconnu : {name}");
        }
    }

    // ── accept-guards des variantes n-aires, référencés par nom ───────────────
    private static Func<IReadOnlyList<Node>, bool> Guard(string name) => name switch
    {
        "tight_body_0" => p => TightBody(p[0]),
        "non_numeric_1" => p => NonNumeric(p[1]),
        "name_atom_tail" => p => p.Skip(1).All(NameAtom),
        _ => throw new InvalidOperationException($"acceptGuard inconnu : {name}"),
    };

    // Fusion générique+langue ; vérifie que chaque cible est bien une clé
    // canonique de Vocab (une typo d'alias doit échouer au premier Analyze,
    // pas produire un atome silencieux).
    private static IReadOnlyDictionary<string, string> MergeAliases(
        Dictionary<string, string> generic, Dictionary<string, string> lang)
    {
        var merged = new Dictionary<string, string>(generic);
        foreach (var kv in lang) merged[kv.Key] = kv.Value;
        foreach (var kv in merged)
            if (!Vocab.ContainsKey(kv.Value))
                throw new InvalidOperationException($"alias '{kv.Key}' → cible inconnue '{kv.Value}'");
        return merged;
    }

    // Préfixe minimal généré comme alias : 4 lettres. À 3, trop de mots/variables
    // courants (« for », « per », « uni »…) seraient capturés ; à 4 (« fora »,
    // « unio », « appro »…) l'intention est sans ambiguïté.
    private const int MinPrefixLen = 4;

    private static bool IsAlphaWord(string s)
    {
        if (s.Length == 0) return false;
        foreach (var c in s)
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
        return true;
    }

    // Génère les alias de PRÉFIXE non ambigus (backlog moteur #2) : pour chaque
    // mot-clé Vocab / alias alphabétique, tout préfixe ≥ MinPrefixLen qui n'est
    // pas déjà une forme exacte et ne préfixe qu'UNE seule cible canonique devient
    // un alias vers cette cible. Réutilise le mécanisme d'alias (Canon) — pas de
    // machinerie dédiée. Ambigus (« arc »→arcsin/arccos/arctan, « sub »→subset/
    // subseteq) : non générés → l'utilisateur tape une lettre de plus.
    private static IReadOnlyDictionary<string, string> AddPrefixAliases(IReadOnlyDictionary<string, string> aliases)
    {
        var forms = new List<KeyValuePair<string, string>>();
        foreach (var key in Vocab.Keys) if (IsAlphaWord(key)) forms.Add(new KeyValuePair<string, string>(key, key));
        foreach (var kv in aliases) if (IsAlphaWord(kv.Key)) forms.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));

        var prefixCanons = new Dictionary<string, HashSet<string>>();
        foreach (var f in forms)
            for (int len = MinPrefixLen; len < f.Key.Length; len++)
            {
                var p = f.Key.Substring(0, len);
                if (Vocab.ContainsKey(p) || aliases.ContainsKey(p)) continue; // forme exacte prioritaire
                if (!prefixCanons.TryGetValue(p, out var set)) prefixCanons[p] = set = new HashSet<string>();
                set.Add(f.Value);
            }

        var merged = new Dictionary<string, string>();
        foreach (var kv in aliases) merged[kv.Key] = kv.Value;
        foreach (var kv in prefixCanons)
            if (kv.Value.Count == 1) merged[kv.Key] = kv.Value.First(); // préfixe NON ambigu
        return merged;
    }
}
