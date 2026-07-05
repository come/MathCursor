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

namespace MathCursor.Engine;

// parser.js — tokens → FORÊT (tous les parses). GÉNÉRIQUE : ne connaît que des FORMES
// (atome / groupe / préfixe / n-aire / infixe). Parser de spans mémoïsé (CYK/chart) sur
// une grammaire AMBIGUË : E → atome | (E) | PREFIX E | NARY E…E | E INFIX E.
internal sealed class Forest
{
    private const int Cap = 64; // produit cartésien des cellules au-delà → repli

    private readonly List<Token> _toks;
    private readonly Func<List<Token>, List<Node>>? _onGroup;
    private readonly EngineCulture _culture;
    private readonly Dictionary<string, List<Node>> _memo = new();
    private readonly int _end;
    // Fallback « espace séparant deux propositions » (étiquette \quad + produit
    // fraction×scalaire) : autorisé UNIQUEMENT au vrai top-level. Faux quand on
    // parse un INTÉRIEUR de parenthèses (via OnGroup), sinon « (x 1/2) » lirait
    // « (x\frac{1}{2}) » par réentrance — ne doit jamais arriver.
    private readonly bool _allowSpaceFallback;

    private Forest(List<Token> toks, Func<List<Token>, List<Node>>? onGroup, EngineCulture culture, bool allowSpaceFallback)
    {
        _toks = toks;
        _onGroup = onGroup;
        _culture = culture;
        _allowSpaceFallback = allowSpaceFallback;
        int vIdx = toks.FindIndex(t => t.Virtual);
        _end = vIdx == -1 ? toks.Count : vIdx;
    }

    public static List<Node> Parse(List<Token> toks, Func<List<Token>, List<Node>>? onGroup, EngineCulture culture, bool allowSpaceFallback = true)
        => new Forest(toks, onGroup, culture, allowSpaceFallback).ParseSpan(0, toks.Count);

    private static Node Hole() => new() { Type = "atom", Sym = "\\square ", Hole = true };

    // Intérieur « visuellement atomique » dont un groupe délimité TAPÉ garde ses
    // délimiteurs au rendu (ADR 2026-06-16 keep-typed-parens) : un atome, ou un
    // INDICE (feature Sub). Lit des FEATURES, jamais un opérateur nommé. Exclut
    // les exposants (donc les modificateurs d'ensemble (R*)/(0+), lexés en sup),
    // les fonctions et les opérateurs lâches → leurs parenthèses restent du
    // groupement ordinaire (dissous au rendu).
    private static bool KeepEligible(Node e)
    {
        if (e.Type == "atom") return !e.Hole;
        return e.Type == "infix" && e.Sym != null
            && Vocabulary.Vocab.TryGetValue(e.Sym, out var v) && v.Sub;
    }

    // Repère (O, ⃗ı, ⃗ȷ) : origine = 1 atome lettre MAJUSCULE, puis 2-3
    // segments « décoration Tight + atome » (vec/bar/hat — features du
    // vocab, aucun opérateur nommé). Matché → les lectures matrices se
    // taisent et le tuple littéral passe en auto.
    private bool IsRepere(List<(int A, int B)> segs)
    {
        if (segs.Count < 3 || segs.Count > 4) return false;
        var (a0, b0) = segs[0];
        if (b0 - a0 != 1) return false;
        var o = _toks[a0];
        if (o.Kind != "atom" || o.Syms != null
            || o.Sym is not { Length: 1 } s0 || s0[0] < 'A' || s0[0] > 'Z') return false;
        for (int k = 1; k < segs.Count; k++)
        {
            var (a, b) = segs[k];
            if (b - a != 2) return false;
            var p = _toks[a];
            if (p.Kind != "prefix" || p.Sym == null
                || !Vocabulary.Vocab.TryGetValue(p.Sym, out var pv) || !pv.Tight) return false;
            if (_toks[a + 1].Kind != "atom") return false;
        }
        return true;
    }

    // Segment = un UNIQUE atome-MOT (≥ 2 lettres, pas un nombre, pas de
    // lectures multiples) — la garde anti-prose des listes nues.
    private bool IsWordAtomSeg(int a, int b)
    {
        if (b - a != 1) return false;
        var t = _toks[a];
        if (t.Kind != "atom" || t.Num || t.Syms != null || t.Sym is not { Length: >= 2 } s) return false;
        foreach (var c in s)
            if (!char.IsLetter(c)) return false;
        return true;
    }

    // Fraction = infixe division (rendu \frac). Facteur = opérande multipliable :
    // atome nu (variable/nombre), OU application d'opérateur (préfixe cos/sin/ln,
    // n-aire int/sum/lim, postfixe n!) — un coefficient fractionnaire devant une
    // intégrale/fonction est courant. Les parenthèses sont gérées par le tier A
    // (étiquette \quad), pas ici. Servent au fallback produit fraction×facteur.
    private static bool IsFraction(Node n) => n.Type == "infix" && n.Sym == "/";
    private static bool IsFactor(Node n) =>
        (n.Type == "atom" && !n.Hole) || n.Type is "prefix" or "nary" or "postfix";

    private int MatchClose(int i)
    {
        int d = 0;
        for (int k = i; k < _toks.Count; k++)
        {
            if (_toks[k].Kind == "lparen") d++;
            else if (_toks[k].Kind == "rparen" && --d == 0) return k;
        }
        return -1;
    }

    private int MatchBrace(int i)
    {
        int d = 0;
        for (int k = i; k < _toks.Count; k++)
        {
            if (_toks[k].Kind == "lbrace") d++;
            else if (_toks[k].Kind == "rbrace" && --d == 0) return k;
        }
        return -1;
    }

    // découpe [a,b) aux virgules de profondeur 0 → spans des éléments. null si aucune.
    private List<(int A, int B)>? CommaSegs(int a, int b)
    {
        var segs = new List<(int, int)>();
        int depth = 0, start = a;
        for (int k = a; k < b; k++)
        {
            var t = _toks[k];
            if (t.Kind == "lparen" || t.Kind == "lbrace") depth++;
            else if (t.Kind == "rparen" || t.Kind == "rbrace") depth--;
            else if (depth == 0 && t.Kind == "sep" && t.Sym == ",") { segs.Add((start, k)); start = k + 1; }
        }
        segs.Add((start, b));
        return segs.Count >= 2 ? segs : null;
    }

    private static readonly Dictionary<string, string> SigCh = new() { ["atom"] = "a", ["lparen"] = "(", ["rparen"] = ")" };

    private string SigOf(int a, int b)
    {
        var s = new System.Text.StringBuilder();
        for (int k = a; k < b; k++)
        {
            var t = _toks[k];
            if (SigCh.TryGetValue(t.Kind, out var ch)) s.Append(ch);
            else if (t.Sym != null) s.Append(t.Sym);
            else if (t.Syms != null) s.Append(string.Concat(t.Syms));
            else s.Append('?');
        }
        return s.ToString();
    }

    private static List<List<Node>> Cartesian(List<List<Node>> lists)
    {
        var acc = new List<List<Node>> { new() };
        foreach (var l in lists)
        {
            var next = new List<List<Node>>();
            foreach (var c in acc)
                foreach (var x in l)
                {
                    var combo = new List<Node>(c) { x };
                    next.Add(combo);
                }
            acc = next;
        }
        return acc;
    }

    private List<Node> ParseSpan(int i, int j)
    {
        string key = i + ":" + j;
        if (_memo.TryGetValue(key, out var cached)) return cached;
        if (i == j) { var h = new List<Node> { Hole() }; _memo[key] = h; return h; }

        var outl = new List<Node>();
        var head = _toks[i];

        // atome : un span d'un seul token (lectures multiples via syms).
        if (j - i == 1 && head.Kind == "atom")
        {
            var syms = head.Syms ?? new List<string> { head.Sym! };
            for (int ai = 0; ai < syms.Count; ai++)
                outl.Add(new Node { Type = "atom", Sym = syms[ai], Coh = head.Coh, Ai = ai });
        }

        // ( E ) — intérieur via le callback (segmentation récursive) si fourni
        if (head.Kind == "lparen" && MatchClose(i) == j - 1)
        {
            // TUPLE : intérieur à VIRGULES profondeur 0 → lecture littérale
            // « (e1, e2, …) » émise AVANT les matrices (à coût égal, la
            // virgule tapée penche « éléments écrits » ; le ; reste la voie
            // grille — ADR 2026-06-12 comma-tuples-bare-lists-repere).
            // L'intérieur n'est pas groupé dans ce cas : un intérieur à
            // virgules ne parse pas d'un bloc, et la liste nue y ferait un
            // doublon SANS parenthèses.
            var tupleSegs = CommaSegs(i + 1, j - 1);
            if (tupleSegs != null)
            {
                var tlists = new List<List<Node>>();
                foreach (var (a, b) in tupleSegs) tlists.Add(ParseSpan(a, b));
                foreach (var combo in Cartesian(tlists))
                {
                    var parts = new List<Node>();
                    foreach (var e in combo) { var g = e.Clone(); g.Grouped = true; parts.Add(g); }
                    outl.Add(new Node { Type = "tuple", Parts = parts });
                }
            }
            else
            {
                List<Node> interior = _onGroup != null ? _onGroup(_toks.GetRange(i + 1, j - 1 - (i + 1))) : ParseSpan(i + 1, j - 1);
                foreach (var e in interior)
                {
                    // Parenthèses tapées CONSERVÉES si l'intérieur est atome/indice
                    // (ADR 2026-06-16) — lecture UNIQUE, pas de version dissoute :
                    // (U_n) → (U_{n}) en auto. Sinon groupement ordinaire (les
                    // parenthèses ne fondent qu'au rendu sous un parent regroupant).
                    if (KeepEligible(e))
                        outl.Add(new Node { Type = "paren", Grouped = true, Parts = new() { e.Clone() } });
                    else { var g = e.Clone(); g.Grouped = true; outl.Add(g); }
                }
            }
            // REPÈRE (O, ⃗ı, ⃗ȷ) : pattern matché → les lectures matrices se
            // taisent, le tuple reste seul dans la fenêtre → AUTO, à toute
            // profondeur d'imbrication.
            if (tupleSegs == null || !IsRepere(tupleSegs))
                foreach (var m in Matrices(i + 1, j - 1)) outl.Add(m);
        }

        // [AB], [a_i]… = groupe crochet TAPÉ autour d'un atome/indice → crochets
        // conservés (ADR 2026-06-16, généralise l'ancien [AB] segment). La voie
        // intervalle ci-dessous exige un séparateur et ne s'applique pas ici ;
        // un intérieur non keep-eligible ([x+1]) ne produit aucune lecture.
        if (head.Kind == "bracket" && head.Sym == "[" && j - i >= 3
            && _toks[j - 1].Kind == "bracket" && _toks[j - 1].Sym == "]")
            foreach (var e in ParseSpan(i + 1, j - 1))
                if (KeepEligible(e))
                    outl.Add(new Node { Type = "paren", Grouped = true, Lb = "[", Rb = "]", Parts = new() { e.Clone() } });

        // INTERVALLE : crochet … SEP … crochet
        if (head.Kind == "bracket" && _toks[j - 1].Kind == "bracket" && j - i >= 3)
        {
            int depth = 0, comma = -1; bool ok = true;
            for (int k = i + 1; k < j - 1 && ok; k++)
            {
                var t = _toks[k];
                if (t.Kind == "lparen") depth++;
                else if (t.Kind == "rparen") depth--;
                else if (depth == 0 && t.Kind == "bracket") ok = false;
                else if (depth == 0 && t.Kind == "sep" && t.Sym == _culture.IntervalSep) { if (comma == -1) comma = k; else ok = false; }
            }
            if (ok && comma != -1)
                foreach (var lo in ParseSpan(i + 1, comma))
                    foreach (var hi in ParseSpan(comma + 1, j - 1))
                    {
                        var glo = lo.Clone(); glo.Grouped = true;
                        var ghi = hi.Clone(); ghi.Grouped = true;
                        outl.Add(new Node { Type = "interval", Lb = head.Sym, Rb = _toks[j - 1].Sym, Parts = new() { glo, ghi } });
                    }
        }

        // ENSEMBLE en extension : { e1, e2, … }
        if (head.Kind == "lbrace" && MatchBrace(i) == j - 1)
        {
            var segs = CommaSegs(i + 1, j - 1) ?? new List<(int, int)> { (i + 1, j - 1) };
            var lists = new List<List<Node>>();
            foreach (var (a, b) in segs) lists.Add(ParseSpan(a, b));
            foreach (var combo in Cartesian(lists))
            {
                var parts = new List<Node>();
                foreach (var e in combo) { var g = e.Clone(); g.Grouped = true; parts.Add(g); }
                outl.Add(new Node { Type = "set", Parts = parts });
            }
        }

        // LISTE NUE : virgules à profondeur 0 hors délimiteurs — « x, y »,
        // « u_1, u_2, ..., u_n ». Garde anti-prose : aucun segment ne doit
        // être un unique atome-MOT (« oui, non » reste une erreur — les
        // zones NER qui débordent sur de la prose ne popent pas).
        var bareSegs = CommaSegs(i, j);
        if (bareSegs != null && !bareSegs.Exists(sg => IsWordAtomSeg(sg.A, sg.B)))
        {
            var blists = new List<List<Node>>();
            foreach (var (a, b) in bareSegs) blists.Add(ParseSpan(a, b));
            foreach (var combo in Cartesian(blists))
            {
                var parts = new List<Node>();
                foreach (var e in combo) { var g = e.Clone(); g.Grouped = true; parts.Add(g); }
                outl.Add(new Node { Type = "list", Parts = parts });
            }
        }

        // VALEUR ABSOLUE |x| / NORME ||x||
        if (head.Kind == "bar" && _toks[j - 1].Kind == "bar" && _toks[j - 1].Sym == head.Sym && j - i >= 2)
        {
            int depth = 0; bool ok = true;
            for (int k = i + 1; k < j - 1 && ok; k++)
            {
                var t = _toks[k];
                if (t.Kind == "lparen") depth++;
                else if (t.Kind == "rparen") depth--;
                else if (depth == 0 && t.Kind == "bar") ok = false;
            }
            // Réutilise l'opérateur vocab abs/norm (head.Sym) plutôt qu'un nœud
            // maison : le rendu \left|…\right| / \left\|…\right\| vit en UN seul
            // endroit (Vocabulary), pas dupliqué dans le renderer. Enfant Grouped :
            // les barres bracketent visuellement (ADR 2026-06-15-Refactor-abs-norm-bar-via-vocab).
            if (ok)
                foreach (var a in ParseSpan(i + 1, j - 1))
                {
                    var g = a.Clone(); g.Grouped = true;
                    outl.Add(new Node { Type = "prefix", Sym = head.Sym, Parts = new() { g } });
                }
        }

        // POSTFIXE SERRÉ : n!, f'
        if (_toks[j - 1].Kind == "postfix" && j - i >= 2)
        {
            bool tight = j - i == 2
                || (_toks[i].Kind == "lparen" && MatchClose(i) == j - 2)
                || _toks[j - 2].Kind == "postfix";
            if (tight)
                foreach (var a in ParseSpan(i, j - 1))
                    outl.Add(new Node { Type = "postfix", Sym = _toks[j - 1].Sym, Parts = new() { a } });
        }

        // barre « mid » AU MILIEU = infixe A\mid B
        for (int k = i + 1; k < j - 1; k++)
            if (_toks[k].Kind == "bar" && _toks[k].Sym == "abs")
                foreach (var l in ParseSpan(i, k))
                    foreach (var r in ParseSpan(k + 1, j))
                        outl.Add(new Node { Type = "infix", Sym = "·mid", Parts = new() { l, r } });

        // FALLBACK « espace séparant deux propositions » — SEULEMENT si l'entrée
        // ENTIÈRE n'a aucune lecture (vrai fallback, zéro impact sur l'existant) et au
        // vrai top-level (jamais dans un intérieur de parenthèses : « (x 1/2) » ne doit
        // PAS devenir « (x\frac{1}{2}) »). Un espace de profondeur 0 a laissé un trou
        // opératoire (l'espace supprime la jonction implicite du lexer).
        if (_allowSpaceFallback && outl.Count == 0 && i == 0 && j == _toks.Count)
        {
            // (A) ÉTIQUETTE : groupe parenthésé FERMÉ en tête + espace → (groupe)\quad reste.
            // Garde anti-prose : droite pas un mot nu (« (E) triangle »). Le cas « () » NE
            // change PAS. « (E)x » collé reste une multiplication (jonction collée).
            if (head.Kind == "lparen")
            {
                int close = MatchClose(i);
                if (close != -1 && close < j - 1 && _toks[close + 1].SpaceBefore && !IsWordAtomSeg(close + 1, j))
                    foreach (var lab in ParseSpan(i, close + 1))
                        if (lab.Type == "paren")
                            foreach (var body in ParseSpan(close + 1, j))
                                outl.Add(new Node { Type = "infix", Sym = "·gap", Parts = new() { lab, body } });
            }

            // (B) PRODUIT fraction × facteur : couper à un espace de profondeur 0 ; si
            // un côté est une FRACTION et l'autre un FACTEUR (atome, ou application cos/
            // int/sum…), lire le PRODUIT implicite COLLÉ « x b/a → x\frac{b}{a} »,
            // « 1/x cos x → \frac{1}{x}\cos(x) ». « 2 x » (aucune fraction) reste erreur ;
            // fraction × fraction hors scope (le facteur exclut la fraction).
            int depth = 0;
            for (int k = i + 1; k < j; k++)
            {
                var prev = _toks[k - 1];
                if (prev.Kind is "lparen" or "lbrace") depth++;
                else if (prev.Kind is "rparen" or "rbrace") depth--;
                else if (prev.Kind == "bracket") depth += prev.Sym == "[" ? 1 : -1;
                if (depth != 0 || !_toks[k].SpaceBefore) continue;
                foreach (var l in ParseSpan(i, k))
                    foreach (var r in ParseSpan(k, j))
                        if ((IsFraction(l) && IsFactor(r)) || (IsFactor(l) && IsFraction(r)))
                            outl.Add(new Node { Type = "infix", Sym = Vocabulary.Role["implicit"], Implicit = true, Parts = new() { l, r } });
            }
        }

        if (head.Kind == "prefix")
        {
            int close = i + 1 < _toks.Count && _toks[i + 1].Kind == "lparen" ? MatchClose(i + 1) : -1;
            if (close != -1)
            {
                if (close == j - 1)
                    foreach (var a in ParseSpan(i + 1, j))
                        outl.Add(new Node { Type = "prefix", Sym = head.Sym, Spaced = head.Spaced, Parts = new() { a } });
            }
            else
            {
                if (!Vocabulary.Vocab[head.Sym!].Tight || j - i <= 2)
                    foreach (var a in ParseSpan(i + 1, j))
                        outl.Add(new Node { Type = "prefix", Sym = head.Sym, Spaced = head.Spaced, Parts = new() { a } });
            }
            // quantificateur (list) sur LISTE de variables
            var segs = Vocabulary.Vocab[head.Sym!].List ? CommaSegs(i + 1, j) : null;
            if (segs != null)
            {
                var lists = new List<List<Node>>();
                foreach (var (a, b) in segs) lists.Add(ParseSpan(a, b));
                foreach (var combo in Cartesian(lists))
                    outl.Add(new Node { Type = "prefix", Sym = head.Sym, Spaced = head.Spaced, Parts = new() { new Node { Type = "list", Parts = combo } } });
            }
        }

        if (head.Kind == "nary")
        {
            var ve = Vocabulary.Vocab[head.Sym!];
            foreach (var parts in Splits(i + 1, j, ve.Arity))
                outl.Add(new Node { Type = "nary", Sym = head.Sym, Spaced = head.Spaced, Parts = parts });
            // formes courtes : trous autorisés comme l'arité canonique — la
            // décision force la PAIRE de squelettes en popup au lieu d'un
            // arbitrage silencieux (ADR 2026-06-12 nary-skeleton-pair, qui
            // amende la règle no-hole). Toujours seulement à la frontière de
            // frappe — sinon « lim x +inf » lirait (\lim x)+∞ au lieu du
            // squelette \lim_{x\to+\infty} □
            if (ve.Variants != null && j >= _end)
                foreach (var v in ve.Variants)
                    foreach (var parts in Splits(i + 1, j, v.Arity))
                        if (v.Accept == null || v.Accept(parts))
                            outl.Add(new Node { Type = "nary", Sym = head.Sym, Spaced = head.Spaced, Parts = parts });
        }

        // opérateur infixe = sommet
        for (int k = i; k < j; k++)
            if (_toks[k].Kind == "infix")
            {
                if (k == i && i != 0) continue;             // trou gauche : seulement au début de l'entrée
                if (k == j - 1 && j < _end) continue;       // trou droite : seulement à la frontière de frappe
                if (_toks[k].Sticky && j - k - 1 != 1) continue;
                var syms = _toks[k].Syms ?? new List<string> { _toks[k].Sym! };
                // Choice : il restait >1 token à droite — l'opérande droite
                // pouvait déborder (slurp) ou s'arrêter. Le Score couple ces
                // choix entre eux (cohérence structurelle).
                bool choice = j - k - 1 > 1;
                foreach (var l in ParseSpan(i, k))
                    foreach (var r in ParseSpan(k + 1, j))
                        foreach (var sym in syms)
                            outl.Add(new Node { Type = "infix", Sym = sym, Spaced = _toks[k].Spaced, Implicit = _toks[k].Implicit, Choice = choice, Parts = new() { l, r } });
            }

        string sg = SigOf(i, j);
        foreach (var n in outl) n.Sig = sg;
        _memo[key] = outl;
        return outl;
    }

    // découpes de [a,b) en K spans (args d'un n-aire). Complétées par des TROUS en fin.
    private List<List<Node>> Splits(int a, int b, int K)
    {
        if (K == 0) return a == b ? new List<List<Node>> { new() } : new List<List<Node>>();
        if (a == b)
        {
            var holes = new List<Node>();
            for (int t = 0; t < K; t++) holes.Add(Hole());
            return new List<List<Node>> { holes };
        }
        var res = new List<List<Node>>();
        var headTok = _toks[a];
        // Un sup issu d'un postSign (0⁺) ne peut PAS s'orienter en chapeau orphelin
        // quand la découpe n-aire coupe sa base : sinon « lim x 0+ » → \lim_{x→0} \hat{+}
        // au lieu de \lim_{x→0⁺} □ (ADR 2026-06-22 orphan-postsign-sup-not-hat).
        string? unary = !headTok.SignSup && headTok.Kind == "infix" && headTok.Sym != null && Vocabulary.Vocab.TryGetValue(headTok.Sym, out var hv) ? hv.Unary : null;
        for (int m = a + 1; m <= b; m++)
        {
            var firsts = ParseSpan(a, m);
            var seq = new List<Node>(firsts);
            if (unary != null && m > a + 1)
                foreach (var x in ParseSpan(a + 1, m))
                    seq.Add(new Node { Type = "prefix", Sym = unary, Spaced = headTok.Spaced, Parts = new() { x } });
            foreach (var first in seq)
                foreach (var rest in Splits(m, b, K - 1))
                {
                    var combo = new List<Node> { first };
                    combo.AddRange(rest);
                    res.Add(combo);
                }
            // L'espace devant une unité reste un séparateur d'args possible :
            // le ·unit qui a traversé l'espace cède le pas, en lecture
            // ALTERNATIVE (le Score tranche) — sinon « lim x 0 g(x) » n'aurait
            // que les lectures « 0 grammes » et jamais \lim_{x\to 0} g(x)
            // (ADR 2026-06-11 unit-vs-nary-arg-boundary).
            if (m < b && _toks[m] is { Kind: "infix", Spaced: true } ju
                && ju.Sym == Vocabulary.Role["unitOp"])
                foreach (var first in ParseSpan(a, m))
                    foreach (var rest in Splits(m + 1, b, K - 1))
                    {
                        var combo = new List<Node> { first };
                        combo.AddRange(rest);
                        res.Add(combo);
                    }
        }
        return res;
    }

    // Lecture MATRICE de [a,b) : grille dont les cellules sont des sous-expressions.
    private List<Node> Matrices(int a, int b)
    {
        if (b <= a) return new List<Node>();
        var cells = new List<(int A, int B)>();
        var seps = new List<int>();
        int depth = 0, start = a; bool prevSep = true;
        // Greffe unaire des cellules activée si : séparateur EXPLICITE ; ou ,
        // (vraie matrice 2D), OU la matrice atteint la frontière de frappe
        // (b == _end = paren EN COURS de saisie « (a -2 », « (a -2 c » → on propose
        // la lecture matrice + on évite l'« aucune lecture » sur l'état transitoire).
        // Un paren COMPLET suivi d'autre chose (« (a +b)/2 ») n'est PAS à la frontière
        // → pas de greffe → reste une expression groupée (sinon régression auto→popup).
        // ADR 2026-06-30-Fix-matrix-signed-cell-unary-graft.
        bool hasExplicitSep = false;
        for (int k = a; k < b; k++)
        {
            var t = _toks[k];
            if (depth == 0 && t.Kind != "sep" && t.SpaceBefore && !prevSep && k > start) { cells.Add((start, k)); seps.Add(1); start = k; }
            if (t.Kind == "lparen") { depth++; prevSep = false; continue; }
            if (t.Kind == "rparen") { depth--; prevSep = false; continue; }
            if (depth == 0 && t.Kind == "sep") { cells.Add((start, k)); seps.Add(t.Rank); start = k + 1; prevSep = true; hasExplicitSep = true; continue; }
            prevSep = false;
        }
        cells.Add((start, b));
        if (cells.Count < 2) return new List<Node>();

        var ranks = new List<int>();
        foreach (var r in seps) if (!ranks.Contains(r)) ranks.Add(r);

        List<List<(int A, int B)>> ByRank(int rank)
        {
            var rows = new List<List<(int, int)>>();
            var row = new List<(int, int)> { cells[0] };
            for (int k = 0; k < seps.Count; k++)
                if (seps[k] == rank) { rows.Add(row); row = new List<(int, int)> { cells[k + 1] }; }
                else row.Add(cells[k + 1]);
            rows.Add(row);
            return rows;
        }

        var grids = new List<(List<List<(int A, int B)>> Rows, bool Alt)>();
        if (ranks.Count >= 2) { int mx = ranks[0]; foreach (var r in ranks) if (r > mx) mx = r; grids.Add((ByRank(mx), false)); }
        else if (ranks[0] == 2) { var rows = new List<List<(int, int)>>(); foreach (var c in cells) rows.Add(new() { c }); grids.Add((rows, false)); }
        else
        {
            grids.Add((new List<List<(int, int)>> { new(cells) }, false));
            var rows = new List<List<(int, int)>>(); foreach (var c in cells) rows.Add(new() { c });
            grids.Add((rows, true));
        }

        var outl = new List<Node>();
        foreach (var g in grids)
        {
            int w = 0; foreach (var r in g.Rows) if (r.Count > w) w = r.Count;
            // forests : par ligne, par cellule (paddée à w avec null) → liste de parses
            var forests = new List<List<List<Node>?>>();
            foreach (var row in g.Rows)
            {
                var padded = new List<(int A, int B)?>();
                foreach (var c in row) padded.Add(c);
                while (padded.Count < w) padded.Add(null);
                var cellForests = new List<List<Node>?>();
                foreach (var c in padded) cellForests.Add(c.HasValue ? CellForest(c.Value.A, c.Value.B, hasExplicitSep || b == _end) : new List<Node> { Hole() });
                forests.Add(cellForests);
            }
            // flat (row-major, largeur w)
            var flat = new List<List<Node>>();
            foreach (var rf in forests) foreach (var f in rf) flat.Add(f!);
            bool dead = false; foreach (var f in flat) if (f.Count == 0) { dead = true; break; }
            if (dead) continue;
            long prod = 1; foreach (var f in flat) prod *= f.Count;
            var lists = flat;
            if (prod > Cap) { lists = new List<List<Node>>(); foreach (var f in flat) lists.Add(new List<Node> { f[0] }); }
            foreach (var combo in Cartesian(lists))
            {
                var rows = new List<List<Node>>();
                for (int ri = 0; ri < forests.Count; ri++)
                {
                    var rowNodes = new List<Node>();
                    for (int ci = 0; ci < forests[ri].Count; ci++) rowNodes.Add(combo[ri * w + ci]);
                    rows.Add(rowNodes);
                }
                outl.Add(new Node { Type = "matrix", Rows = rows, Delim = "(", Alt = g.Alt });
            }
        }
        return outl;
    }

    // Forêt d'une cellule de matrice = sa lecture normale + (si la tête est un
    // infixe unaire-capable, « - »/« + ») la lecture greffée prefix(unaire, reste).
    // Sans ça, une cellule « -1 » (le « - » lexé BINAIRE car précédé d'un opérande
    // dans le flux plat, cf. Lexer binaryPos) a une forêt VIDE → cellule morte →
    // matrice morte → « erreur ». La greffe fait coexister `x-1` (lecture non-
    // matricielle) et la matrice [x,-1] ; le Score arbitre. Réutilise le primitif
    // unaire de Splits (mêmes conditions). ADR 2026-06-30-Fix-matrix-signed-cell-unary-graft.
    private List<Node> CellForest(int s, int e, bool allowGraft)
    {
        var f = new List<Node>(ParseSpan(s, e));
        if (allowGraft && e > s + 1)
        {
            var ht = _toks[s];
            if (!ht.SignSup && ht.Kind == "infix" && ht.Sym != null
                && Vocabulary.Vocab.TryGetValue(ht.Sym, out var hv) && hv.Unary != null)
                foreach (var x in ParseSpan(s + 1, e))
                    f.Add(new Node { Type = "prefix", Sym = hv.Unary, Spaced = ht.Spaced, Parts = new() { x } });
        }
        return f;
    }
}
