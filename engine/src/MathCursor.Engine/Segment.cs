// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
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

// segment.js — pré-passe de BORNAGE (perf). GÉNÉRIQUE : lit class/spaced/cut via le
// vocabulaire, ne nomme aucun opérateur.
internal static class Segment
{
    // Seuil du garde-fou : forêt exhaustive tant que < MAX_CHAIN infixes de profondeur 0.
    public const int MaxChain = 8;

    private static VocabEntry? VInfix(Token t) => t.Kind == "infix" && t.Sym != null && Vocabulary.Vocab.TryGetValue(t.Sym, out var v) ? v : null;

    // COUPE = relation (cut) OU infixe ESPACÉ qui sépare large (looseness > 0).
    private static bool IsCut(Token t)
    {
        var v = VInfix(t);
        return v != null && (v.Cut || (t.Spaced && v.Looseness > 0));
    }

    // Découpe générique de seg aux infixes de profondeur 0 retenus par `keep`.
    private static (List<List<Token>> Parts, List<Token> Ops) CutBy(List<Token> seg, Func<Token, bool> keep)
    {
        var parts = new List<List<Token>>();
        var ops = new List<Token>();
        int depth = 0, brk = 0, brc = 0, start = 0;
        for (int i = 0; i < seg.Count; i++)
        {
            var t = seg[i];
            if (t.Kind == "lparen" || t.Kind == "lbrace") depth++;
            else if (t.Kind == "rparen" || t.Kind == "rbrace") depth--;
            else if (t.Kind == "bracket") brk++;
            else if (t.Kind == "bar") brc++;
            else if (depth == 0 && brk % 2 == 0 && brc % 2 == 0 && t.Kind == "infix" && keep(t))
            {
                parts.Add(seg.GetRange(start, i - start));
                ops.Add(t);
                start = i + 1;
            }
        }
        parts.Add(seg.GetRange(start, seg.Count - start));
        return (parts, ops);
    }

    public static (List<List<Token>> Segs, List<Token> Ops) Split(List<Token> toks)
    {
        var (parts, ops) = CutBy(toks, IsCut);
        return (parts, ops);
    }

    public static (List<List<Token>> Segs, List<Token> Ops) SplitRel(List<Token> toks)
    {
        var (parts, ops) = CutBy(toks, t => { var v = VInfix(t); return v != null && v.Cut; });
        return (parts, ops);
    }

    public static (List<List<Token>> Operands, List<Token> Ops) SplitOperands(List<Token> seg)
    {
        var (parts, ops) = CutBy(seg, _ => true);
        return (parts, ops);
    }

    public static int ChainLen(List<Token> seg) => SplitOperands(seg).Ops.Count;

    // Coupe de REPLI : aux infixes de profondeur 0 de looseness MAXIMALE
    // (relations > additifs > multiplicatifs > joins serrés). Sert le repli
    // par précédence (ADR 2026-06-12-Fix-fold-by-precedence) — jamais le
    // chemin nominal. Aucun op coupable → (seg entier, ops vides).
    public static (List<List<Token>> Parts, List<Token> Ops) SplitLoosest(List<Token> seg)
    {
        double max = double.NegativeInfinity;
        int depth = 0, brk = 0, brc = 0;
        foreach (var t in seg)
        {
            if (t.Kind == "lparen" || t.Kind == "lbrace") depth++;
            else if (t.Kind == "rparen" || t.Kind == "rbrace") depth--;
            else if (t.Kind == "bracket") brk++;
            else if (t.Kind == "bar") brc++;
            else if (depth == 0 && brk % 2 == 0 && brc % 2 == 0)
            {
                var v = VInfix(t);
                if (v != null && v.Looseness > max) max = v.Looseness;
            }
        }
        if (double.IsNegativeInfinity(max)) return (new List<List<Token>> { seg }, new List<Token>());
        double m = max;
        return CutBy(seg, t => { var v = VInfix(t); return v != null && v.Looseness >= m; });
    }

    // Recombinaison COHÉRENTE : segments de MÊME sig co-varient ; puis bracketings d'épine.
    public static List<Node> Combine(List<List<Node>> candsPerSeg, List<Token> ops)
    {
        var dimKey = new string[candsPerSeg.Count];
        for (int i = 0; i < candsPerSeg.Count; i++)
        {
            var c = candsPerSeg[i];
            dimKey[i] = c.Count > 0 && c[0].Sig != null && c[0].Type != "atom" ? "s:" + c[0].Sig : "u:" + i;
        }
        var dims = new List<string>();
        foreach (var k in dimKey) if (!dims.Contains(k)) dims.Add(k);
        var len = new Dictionary<string, int>();
        foreach (var d in dims)
        {
            int m = int.MaxValue;
            for (int i = 0; i < candsPerSeg.Count; i++) if (dimKey[i] == d && candsPerSeg[i].Count < m) m = candsPerSeg[i].Count;
            len[d] = m;
        }

        var assigns = new List<Dictionary<string, int>> { new() };
        foreach (var d in dims)
        {
            var next = new List<Dictionary<string, int>>();
            foreach (var a in assigns)
                for (int x = 0; x < len[d]; x++)
                {
                    var na = new Dictionary<string, int>(a) { [d] = x };
                    next.Add(na);
                }
            assigns = next;
        }

        List<Node> Bracket(List<Node> picked)
        {
            var memo = new Dictionary<string, List<Node>>();
            List<Node> Go(int i, int j)
            {
                string key = i + ":" + j;
                if (memo.TryGetValue(key, out var c)) return c;
                List<Node> outp;
                if (i == j) outp = new List<Node> { picked[i] };
                else
                {
                    outp = new List<Node>();
                    for (int k = i; k < j; k++)
                    {
                        var op = ops[k];
                        foreach (var L in Go(i, k))
                            foreach (var R in Go(k + 1, j))
                                outp.Add(new Node { Type = "infix", Sym = op.Sym, Spaced = op.Spaced, Parts = new() { L, R } });
                    }
                }
                memo[key] = outp;
                return outp;
            }
            return Go(0, picked.Count - 1);
        }

        var outl = new List<Node>();
        foreach (var a in assigns)
        {
            var picked = new List<Node>();
            for (int i = 0; i < candsPerSeg.Count; i++) picked.Add(candsPerSeg[i][a[dimKey[i]]]);
            foreach (var t in Bracket(picked)) outl.Add(t);
        }
        return outl;
    }
}
