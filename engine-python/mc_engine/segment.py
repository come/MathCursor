"""Pré-passe de bornage + recombinaison — port de Segment.cs.

Générique : lit class/spaced/cut via le vocabulaire, ne nomme aucun opérateur.
"""
import math
from .node import Node
from .vocab import VOCAB

MAX_CHAIN = 8


def _vinfix(t):
    if t.kind == "infix" and t.sym is not None and t.sym in VOCAB:
        return VOCAB[t.sym]
    return None


def _is_cut(t):
    v = _vinfix(t)
    return v is not None and (v.cut or (t.spaced and v.looseness > 0))


def _cut_by(seg, keep):
    parts = []
    ops = []
    depth = brk = brc = start = 0
    for i, t in enumerate(seg):
        if t.kind in ("lparen", "lbrace"):
            depth += 1
        elif t.kind in ("rparen", "rbrace"):
            depth -= 1
        elif t.kind == "bracket":
            brk += 1
        elif t.kind == "bar":
            brc += 1
        elif depth == 0 and brk % 2 == 0 and brc % 2 == 0 and t.kind == "infix" and keep(t):
            parts.append(seg[start:i])
            ops.append(t)
            start = i + 1
    parts.append(seg[start:])
    return parts, ops


def split(toks):
    return _cut_by(toks, _is_cut)


def split_rel(toks):
    return _cut_by(toks, lambda t: (_vinfix(t) is not None and _vinfix(t).cut))


def split_operands(seg):
    return _cut_by(seg, lambda t: True)


def chain_len(seg):
    return len(split_operands(seg)[1])


def split_loosest(seg):
    mx = -math.inf
    depth = brk = brc = 0
    for t in seg:
        if t.kind in ("lparen", "lbrace"):
            depth += 1
        elif t.kind in ("rparen", "rbrace"):
            depth -= 1
        elif t.kind == "bracket":
            brk += 1
        elif t.kind == "bar":
            brc += 1
        elif depth == 0 and brk % 2 == 0 and brc % 2 == 0:
            v = _vinfix(t)
            if v is not None and v.looseness > mx:
                mx = v.looseness
    if mx == -math.inf:
        return [seg], []
    return _cut_by(seg, lambda t, m=mx: (_vinfix(t) is not None and _vinfix(t).looseness >= m))


def combine(cands_per_seg, ops):
    n = len(cands_per_seg)
    dim_key = []
    for i in range(n):
        c = cands_per_seg[i]
        if len(c) > 0 and c[0].sig is not None and c[0].type != "atom":
            dim_key.append("s:" + c[0].sig)
        else:
            dim_key.append("u:" + str(i))
    dims = []
    for k in dim_key:
        if k not in dims:
            dims.append(k)
    length = {}
    for d in dims:
        m = math.inf
        for i in range(n):
            if dim_key[i] == d and len(cands_per_seg[i]) < m:
                m = len(cands_per_seg[i])
        length[d] = m

    assigns = [dict()]
    for d in dims:
        nxt = []
        for a in assigns:
            for x in range(int(length[d])):
                na = dict(a)
                na[d] = x
                nxt.append(na)
        assigns = nxt

    def bracket(picked):
        memo = {}

        def go(i, j):
            key = (i, j)
            if key in memo:
                return memo[key]
            if i == j:
                outp = [picked[i]]
            else:
                outp = []
                for k in range(i, j):
                    op = ops[k]
                    for L in go(i, k):
                        for R in go(k + 1, j):
                            outp.append(Node(type="infix", sym=op.sym, spaced=op.spaced, parts=[L, R]))
            memo[key] = outp
            return outp

        return go(0, len(picked) - 1)

    outl = []
    for a in assigns:
        picked = [cands_per_seg[i][a[dim_key[i]]] for i in range(n)]
        outl.extend(bracket(picked))
    return outl
