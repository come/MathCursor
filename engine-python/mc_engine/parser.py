"""tokens -> FORÊT (tous les parses) — port de Parser.cs (Forest).

Parser de spans mémoïsé sur une grammaire AMBIGUË :
E -> atome | (E) | PREFIX E | NARY E…E | E INFIX E.
"""
import copy
from .node import Node
from .vocab import VOCAB, ROLE

CAP = 64  # produit cartésien des cellules au-delà -> repli


def _hole():
    return Node(type="atom", sym="\\square ", hole=True)


def _clone(n):
    return copy.copy(n)  # shallow (parts partagé par ref, comme C# Clone)


def _keep_eligible(e):
    if e.type == "atom":
        return not e.hole
    return e.type == "infix" and e.sym is not None and e.sym in VOCAB and VOCAB[e.sym].sub


def _cartesian(lists):
    acc = [[]]
    for l in lists:
        nxt = []
        for c in acc:
            for x in l:
                nxt.append(c + [x])
        acc = nxt
    return acc


_SIG_CH = {"atom": "a", "lparen": "(", "rparen": ")"}


class Forest:
    def __init__(self, toks, on_group, culture):
        self._toks = toks
        self._on_group = on_group
        self._culture = culture
        self._memo = {}
        v_idx = next((idx for idx, t in enumerate(toks) if t.virtual), -1)
        self._end = len(toks) if v_idx == -1 else v_idx

    def _match_close(self, i):
        d = 0
        for k in range(i, len(self._toks)):
            if self._toks[k].kind == "lparen":
                d += 1
            elif self._toks[k].kind == "rparen":
                d -= 1
                if d == 0:
                    return k
        return -1

    def _match_brace(self, i):
        d = 0
        for k in range(i, len(self._toks)):
            if self._toks[k].kind == "lbrace":
                d += 1
            elif self._toks[k].kind == "rbrace":
                d -= 1
                if d == 0:
                    return k
        return -1

    def _comma_segs(self, a, b):
        segs = []
        depth = 0
        start = a
        for k in range(a, b):
            t = self._toks[k]
            if t.kind in ("lparen", "lbrace"):
                depth += 1
            elif t.kind in ("rparen", "rbrace"):
                depth -= 1
            elif depth == 0 and t.kind == "sep" and t.sym == ",":
                segs.append((start, k))
                start = k + 1
        segs.append((start, b))
        return segs if len(segs) >= 2 else None

    def _is_repere(self, segs):
        if len(segs) < 3 or len(segs) > 4:
            return False
        a0, b0 = segs[0]
        if b0 - a0 != 1:
            return False
        o = self._toks[a0]
        if o.kind != "atom" or o.syms is not None or o.sym is None or len(o.sym) != 1 \
                or not ('A' <= o.sym[0] <= 'Z'):
            return False
        for k in range(1, len(segs)):
            a, b = segs[k]
            if b - a != 2:
                return False
            p = self._toks[a]
            if p.kind != "prefix" or p.sym is None or p.sym not in VOCAB or not VOCAB[p.sym].tight:
                return False
            if self._toks[a + 1].kind != "atom":
                return False
        return True

    def _is_word_atom_seg(self, a, b):
        if b - a != 1:
            return False
        t = self._toks[a]
        if t.kind != "atom" or t.num or t.syms is not None or t.sym is None or len(t.sym) < 2:
            return False
        return all(c.isalpha() for c in t.sym)

    def _sig_of(self, a, b):
        s = []
        for k in range(a, b):
            t = self._toks[k]
            if t.kind in _SIG_CH:
                s.append(_SIG_CH[t.kind])
            elif t.sym is not None:
                s.append(t.sym)
            elif t.syms is not None:
                s.append("".join(t.syms))
            else:
                s.append("?")
        return "".join(s)

    def parse_span(self, i, j):
        key = (i, j)
        if key in self._memo:
            return self._memo[key]
        if i == j:
            h = [_hole()]
            self._memo[key] = h
            return h

        outl = []
        toks = self._toks
        head = toks[i]

        # atome
        if j - i == 1 and head.kind == "atom":
            syms = head.syms if head.syms is not None else [head.sym]
            for ai in range(len(syms)):
                outl.append(Node(type="atom", sym=syms[ai], coh=head.coh, ai=ai))

        # ( E )
        if head.kind == "lparen" and self._match_close(i) == j - 1:
            tuple_segs = self._comma_segs(i + 1, j - 1)
            if tuple_segs is not None:
                tlists = [self.parse_span(a, b) for (a, b) in tuple_segs]
                for combo in _cartesian(tlists):
                    parts = []
                    for e in combo:
                        g = _clone(e); g.grouped = True; parts.append(g)
                    outl.append(Node(type="tuple", parts=parts))
            else:
                if self._on_group is not None:
                    interior = self._on_group(toks[i + 1:j - 1])
                else:
                    interior = self.parse_span(i + 1, j - 1)
                for e in interior:
                    if _keep_eligible(e):
                        outl.append(Node(type="paren", grouped=True, parts=[_clone(e)]))
                    else:
                        g = _clone(e); g.grouped = True; outl.append(g)
            if tuple_segs is None or not self._is_repere(tuple_segs):
                outl.extend(self._matrices(i + 1, j - 1))

        # [AB], [a_i] : crochets tapés conservés
        if head.kind == "bracket" and head.sym == "[" and j - i >= 3 \
                and toks[j - 1].kind == "bracket" and toks[j - 1].sym == "]":
            for e in self.parse_span(i + 1, j - 1):
                if _keep_eligible(e):
                    outl.append(Node(type="paren", grouped=True, lb="[", rb="]", parts=[_clone(e)]))

        # INTERVALLE : crochet … SEP … crochet
        if head.kind == "bracket" and toks[j - 1].kind == "bracket" and j - i >= 3:
            depth = 0
            comma = -1
            ok = True
            k = i + 1
            while k < j - 1 and ok:
                t = toks[k]
                if t.kind == "lparen":
                    depth += 1
                elif t.kind == "rparen":
                    depth -= 1
                elif depth == 0 and t.kind == "bracket":
                    ok = False
                elif depth == 0 and t.kind == "sep" and t.sym == self._culture.interval_sep:
                    if comma == -1:
                        comma = k
                    else:
                        ok = False
                k += 1
            if ok and comma != -1:
                for lo in self.parse_span(i + 1, comma):
                    for hi in self.parse_span(comma + 1, j - 1):
                        glo = _clone(lo); glo.grouped = True
                        ghi = _clone(hi); ghi.grouped = True
                        outl.append(Node(type="interval", lb=head.sym, rb=toks[j - 1].sym, parts=[glo, ghi]))

        # ENSEMBLE { e1, e2, … }
        if head.kind == "lbrace" and self._match_brace(i) == j - 1:
            segs = self._comma_segs(i + 1, j - 1) or [(i + 1, j - 1)]
            lists = [self.parse_span(a, b) for (a, b) in segs]
            for combo in _cartesian(lists):
                parts = []
                for e in combo:
                    g = _clone(e); g.grouped = True; parts.append(g)
                outl.append(Node(type="set", parts=parts))

        # LISTE NUE : virgules profondeur 0 (anti-prose : aucun segment mot-atome)
        bare_segs = self._comma_segs(i, j)
        if bare_segs is not None and not any(self._is_word_atom_seg(a, b) for (a, b) in bare_segs):
            blists = [self.parse_span(a, b) for (a, b) in bare_segs]
            for combo in _cartesian(blists):
                parts = []
                for e in combo:
                    g = _clone(e); g.grouped = True; parts.append(g)
                outl.append(Node(type="list", parts=parts))

        # VALEUR ABSOLUE |x| / NORME ||x||
        if head.kind == "bar" and toks[j - 1].kind == "bar" and toks[j - 1].sym == head.sym and j - i >= 2:
            depth = 0
            ok = True
            k = i + 1
            while k < j - 1 and ok:
                t = toks[k]
                if t.kind == "lparen":
                    depth += 1
                elif t.kind == "rparen":
                    depth -= 1
                elif depth == 0 and t.kind == "bar":
                    ok = False
                k += 1
            if ok:
                for a in self.parse_span(i + 1, j - 1):
                    g = _clone(a); g.grouped = True
                    outl.append(Node(type="prefix", sym=head.sym, parts=[g]))

        # POSTFIXE SERRÉ : n!, f'
        if toks[j - 1].kind == "postfix" and j - i >= 2:
            tight = (j - i == 2) \
                or (toks[i].kind == "lparen" and self._match_close(i) == j - 2) \
                or (toks[j - 2].kind == "postfix")
            if tight:
                for a in self.parse_span(i, j - 1):
                    outl.append(Node(type="postfix", sym=toks[j - 1].sym, parts=[a]))

        # barre « mid » au milieu = infixe A\mid B
        for k in range(i + 1, j - 1):
            if toks[k].kind == "bar" and toks[k].sym == "abs":
                for l in self.parse_span(i, k):
                    for r in self.parse_span(k + 1, j):
                        outl.append(Node(type="infix", sym="·mid", parts=[l, r]))

        if head.kind == "prefix":
            close = self._match_close(i + 1) if (i + 1 < len(toks) and toks[i + 1].kind == "lparen") else -1
            if close != -1:
                if close == j - 1:
                    for a in self.parse_span(i + 1, j):
                        outl.append(Node(type="prefix", sym=head.sym, spaced=head.spaced, parts=[a]))
            else:
                if not VOCAB[head.sym].tight or j - i <= 2:
                    for a in self.parse_span(i + 1, j):
                        outl.append(Node(type="prefix", sym=head.sym, spaced=head.spaced, parts=[a]))
            segs = self._comma_segs(i + 1, j) if VOCAB[head.sym].list else None
            if segs is not None:
                lists = [self.parse_span(a, b) for (a, b) in segs]
                for combo in _cartesian(lists):
                    outl.append(Node(type="prefix", sym=head.sym, spaced=head.spaced,
                                     parts=[Node(type="list", parts=combo)]))

        if head.kind == "nary":
            ve = VOCAB[head.sym]
            for parts in self._splits(i + 1, j, ve.arity):
                outl.append(Node(type="nary", sym=head.sym, spaced=head.spaced, parts=parts))
            if ve.variants is not None and j >= self._end:
                for (var_arity, _var_render, var_accept) in ve.variants:
                    for parts in self._splits(i + 1, j, var_arity):
                        if var_accept is None or var_accept(parts):
                            outl.append(Node(type="nary", sym=head.sym, spaced=head.spaced, parts=parts))

        # opérateur infixe = sommet
        for k in range(i, j):
            if toks[k].kind == "infix":
                if k == i and i != 0:
                    continue
                if k == j - 1 and j < self._end:
                    continue
                if toks[k].sticky and j - k - 1 != 1:
                    continue
                syms = toks[k].syms if toks[k].syms is not None else [toks[k].sym]
                choice = (j - k - 1) > 1
                for l in self.parse_span(i, k):
                    for r in self.parse_span(k + 1, j):
                        for sym in syms:
                            outl.append(Node(type="infix", sym=sym, spaced=toks[k].spaced,
                                             implicit=toks[k].implicit, choice=choice, parts=[l, r]))

        sg = self._sig_of(i, j)
        for n in outl:
            n.sig = sg
        self._memo[key] = outl
        return outl

    def _splits(self, a, b, K):
        if K == 0:
            return [[]] if a == b else []
        if a == b:
            return [[_hole() for _ in range(K)]]
        res = []
        head_tok = self._toks[a]
        unary = None
        if head_tok.kind == "infix" and head_tok.sym is not None and head_tok.sym in VOCAB:
            unary = VOCAB[head_tok.sym].unary
        for m in range(a + 1, b + 1):
            firsts = self.parse_span(a, m)
            seq = list(firsts)
            if unary is not None and m > a + 1:
                for x in self.parse_span(a + 1, m):
                    seq.append(Node(type="prefix", sym=unary, spaced=head_tok.spaced, parts=[x]))
            for first in seq:
                for rest in self._splits(m, b, K - 1):
                    res.append([first] + rest)
            if m < b and self._toks[m].kind == "infix" and self._toks[m].spaced \
                    and self._toks[m].sym == ROLE["unitOp"]:
                for first in self.parse_span(a, m):
                    for rest in self._splits(m + 1, b, K - 1):
                        res.append([first] + rest)
        return res

    def _matrices(self, a, b):
        if b <= a:
            return []
        cells = []
        seps = []
        depth = 0
        start = a
        prev_sep = True
        for k in range(a, b):
            t = self._toks[k]
            if depth == 0 and t.kind != "sep" and t.space_before and not prev_sep and k > start:
                cells.append((start, k)); seps.append(1); start = k
            if t.kind == "lparen":
                depth += 1; prev_sep = False; continue
            if t.kind == "rparen":
                depth -= 1; prev_sep = False; continue
            if depth == 0 and t.kind == "sep":
                cells.append((start, k)); seps.append(t.rank); start = k + 1; prev_sep = True; continue
            prev_sep = False
        cells.append((start, b))
        if len(cells) < 2:
            return []

        ranks = []
        for r in seps:
            if r not in ranks:
                ranks.append(r)

        def by_rank(rank):
            rows = []
            row = [cells[0]]
            for k in range(len(seps)):
                if seps[k] == rank:
                    rows.append(row); row = [cells[k + 1]]
                else:
                    row.append(cells[k + 1])
            rows.append(row)
            return rows

        grids = []  # (rows, alt)
        if len(ranks) >= 2:
            mx = max(ranks)
            grids.append((by_rank(mx), False))
        elif ranks[0] == 2:
            grids.append(([[c] for c in cells], False))
        else:
            grids.append(([list(cells)], False))
            grids.append(([[c] for c in cells], True))

        outl = []
        for (g_rows, g_alt) in grids:
            w = max((len(r) for r in g_rows), default=0)
            forests = []
            for row in g_rows:
                padded = list(row) + [None] * (w - len(row))
                cell_forests = [self.parse_span(c[0], c[1]) if c is not None else [_hole()] for c in padded]
                forests.append(cell_forests)
            flat = [f for rf in forests for f in rf]
            if any(len(f) == 0 for f in flat):
                continue
            prod = 1
            for f in flat:
                prod *= len(f)
            lists = flat
            if prod > CAP:
                lists = [[f[0]] for f in flat]
            for combo in _cartesian(lists):
                rows = []
                for ri in range(len(forests)):
                    row_nodes = [combo[ri * w + ci] for ci in range(len(forests[ri]))]
                    rows.append(row_nodes)
                outl.append(Node(type="matrix", rows=rows, delim="(", alt=g_alt))
        return outl


def parse(toks, on_group, culture):
    return Forest(toks, on_group, culture).parse_span(0, len(toks))
