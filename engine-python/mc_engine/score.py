"""filtre COUPE + COÛT — port de Score.cs.

Générique : ne lit que les FEATURES (class, looseness, cut, bracketed, mapping),
jamais un nom d'opérateur.
"""
from .vocab import VOCAB

PENALTY = 1.5
WIDEN = 1.0
MODE_MIX = 3.0
ORIENT = 0.5
MAP_DEF = 2.5
HOLE_COST = 3.0
STRONG = "STRONG"
WEAK = "WEAK"


def _decl(n):
    return VOCAB.get(n.sym) if n.sym is not None else None


def _parts(n):
    return n.effective_parts()


def _is_strong(n):
    d = _decl(n)
    return n.type != "atom" and d is not None and d.cls == STRONG


def _is_weak(n):
    d = _decl(n)
    return n.type != "atom" and d is not None and d.cls == WEAK


# ── Filtre COUPE ─────────────────────────────────────────────────────────────
def _covers_spaced_weak(n):
    if n.type == "atom" or n.grouped:
        return False
    if _is_weak(n) and n.spaced:
        return True
    return any(_covers_spaced_weak(c) for c in _parts(n))


def _is_cut_op(n):
    d = _decl(n)
    return n.type != "atom" and d is not None and d.cut


def crosses_cut(n):
    if n.type == "atom":
        return False
    if _is_strong(n):
        if any(_covers_spaced_weak(c) for c in _parts(n)):
            return True
    if not _is_cut_op(n):
        if any(_is_cut_op(c) and not c.grouped for c in _parts(n)):
            return True
    return any(crosses_cut(c) for c in _parts(n))


# ── Coût ─────────────────────────────────────────────────────────────────────
def _loose(n):
    d = _decl(n)
    if d is None:
        return 0.0
    return d.looseness + (1.5 if (d.shape == "infix" and n.spaced) else 0.0)


def _inversions(n):
    if n.type == "atom":
        return 0
    inv = 0
    d = _decl(n)
    for c in _parts(n):
        if c.type != "atom" and d is not None and _loose(n) < _loose(c):
            inv += 1
        inv += _inversions(c)
    return inv


def _all_atom_parts(n):
    return all(c.type == "atom" for c in _parts(n))


def _atomic_script(n):
    d = _decl(n)
    return d is not None and (d.sup or d.sub) and _all_atom_parts(n)


def _nest_strong(n):
    d = _decl(n)
    return _is_strong(n) and not n.implicit and not (d is not None and d.tight)


def _contains_nest(n, in_brackets):
    if n.type == "atom":
        return False
    if _nest_strong(n) and not (in_brackets and _atomic_script(n)):
        return True
    return any(_contains_nest(c, in_brackets) for c in _parts(n))


def _nesting(n):
    if n.type == "atom":
        return 0
    c = 0
    for x in _parts(n):
        c += _nesting(x)
    if _nest_strong(n):
        d = _decl(n)
        br = d is not None and d.bracketed
        for x in _parts(n):
            if _contains_nest(x, br):
                c += 1
                break
    return c


def _widen(n):
    if n.type == "atom":
        return 0.0
    c = 0.0
    for x in _parts(n):
        c += _widen(x)
    if n.implicit:
        for p in _parts(n):
            if p.type != "atom" and not p.grouped:
                c += WIDEN
                break
    return c


def _base(n):
    return _inversions(n) + _nesting(n) + _widen(n)


def _shape(n):
    if n.type == "atom":
        return "a"
    parts = _parts(n)
    inner = ",".join(_shape(p) for p in parts)
    return f"{n.sym if n.sym is not None else n.type}({inner})"


def _parent_refund(n):
    r = 0.0
    for c in _parts(n):
        r += _parent_refund(c)
    groups = {}
    for c in _parts(n):
        if c.type == "atom" or c.sig is None:
            continue
        groups.setdefault(c.sig, []).append(c)
    sym = 0.0
    for g in groups.values():
        if len(g) < 2:
            continue
        shapes = {_shape(c) for c in g}
        if len(shapes) == 1:
            for c in g:
                if _loose(n) < _loose(c):
                    sym += 1
    gestalt = 0.0
    d = _decl(n)
    if d is not None and d.bracketed:
        inv = 0
        for c in _parts(n):
            if c.type != "atom" and _loose(n) < _loose(c):
                inv += 1
        if inv >= 2:
            gestalt = inv - 1
    return r + (sym if sym > gestalt else gestalt)


def _global_coherence(root):
    by_sig = {}

    def walk(x):
        if x.type != "atom" and x.sig is not None:
            g = by_sig.setdefault(x.sig, [set(), []])
            g[0].add(_shape(x))
            g[1].append(x)
        for c in _parts(x):
            walk(c)

    walk(root)
    d = 0.0
    for (shapes, nodes) in by_sig.values():
        if len(shapes) > 1:
            d += PENALTY * (len(shapes) - 1)
        elif len(nodes) >= 2:
            d -= (len(nodes) - 1) * _base(nodes[0])
    return d


def _mode_coherence(root):
    by_coh = {}

    def walk(x):
        if x.type == "atom" and x.coh is not None:
            by_coh.setdefault(x.coh, set()).add(x.ai)
        for c in _parts(x):
            walk(c)

    walk(root)
    d = 0.0
    for idx in by_coh.values():
        if len(idx) > 1:
            d += MODE_MIX * (len(idx) - 1)
    return d


def _minimal_frac(n):
    d = _decl(n)
    return (n.type != "atom" and not n.grouped and d is not None and d.bracketed
            and len(_parts(n)) == 2 and _parts(n)[1].type == "atom")


def _leftmost(n):
    while n.type != "atom":
        p = n.effective_parts()
        if len(p) == 0:
            return None
        n = p[0]
    return n.sym


def _slurp_coherence(root):
    sites = []  # (slurp, key, sig, b)

    def add(f, slurp):
        p = _parts(f)
        key = (_leftmost(p[0]) or "?") + "/" + (_leftmost(p[1]) or "?")
        sites.append((slurp, key, f.sig or "", _base(f)))

    def walk(x):
        if x.type == "atom":
            return
        parts = _parts(x)
        if x.type == "infix" and not x.spaced and len(parts) == 2 and _minimal_frac(parts[0]):
            add(parts[0], False)
        d = _decl(x)
        if x.choice and d is not None and d.bracketed and len(parts) == 2 and parts[1].type != "atom":
            add(x, True)
        for c in parts:
            walk(c)

    walk(root)
    if len(sites) < 2:
        return 0.0
    d = 0.0
    for i in range(len(sites)):
        for k in range(i + 1, len(sites)):
            a = sites[i]
            b = sites[k]
            if a[1] != b[1]:
                continue
            if a[0] != b[0]:
                d += 1
            elif a[0] and a[2] != b[2]:
                d -= min(a[3], b[3])
    return d


def _matrix_extra(n):
    if n.type == "atom":
        return 0.0
    e = 0.0
    for c in _parts(n):
        e += _matrix_extra(c)
    if n.type == "matrix" and n.alt:
        e += ORIENT
    return e


def _def_chain(n):
    r = 0.0
    for c in _parts(n):
        r += _def_chain(c)
    d = _decl(n)
    if d is not None and d.mapping:
        for c in _parts(n):
            if _is_cut_op(c) and not c.grouped:
                r += MAP_DEF
                break
    return r


def _holes(n):
    h = 1 if n.hole else 0
    for c in _parts(n):
        h += _holes(c)
    return h


def cost(n):
    return (_base(n) + _matrix_extra(n) + HOLE_COST * _holes(n)
            + _global_coherence(n) + _mode_coherence(n) + _slurp_coherence(n)
            - _parent_refund(n) - _def_chain(n))
