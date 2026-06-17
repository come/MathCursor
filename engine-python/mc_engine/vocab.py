"""Vocabulaire du moteur — port du builder de Vocabulary.cs.

La TABLE vient de data/engine/symbols.json (source unique partagée avec le C#).
Ce module = le BUILDER + le registre des logiques non-déclaratives (render-hooks,
accept-guards), strictement équivalents au C#. Cf. ADR portable-engine.
"""
import re
from dataclasses import dataclass, field
from typing import Optional, Callable

from . import data

_SYM = data.load("symbols.json")
LOOSE = {k: float(v) for k, v in _SYM["const"]["looseness"].items()}
# consts utilisés par les hooks/guards (doivent coïncider avec symbols.json).
REL, SUM, QUANT, PROD, POW, APP = (LOOSE[k] for k in ("REL", "SUM", "QUANT", "PROD", "POW", "APP"))


@dataclass
class VocabEntry:
    shape: Optional[str] = None
    arity: int = 0
    cls: Optional[str] = None
    looseness: float = 0.0
    bracketed: bool = False
    cut: bool = False
    implicit: bool = False
    sup: bool = False
    sub: bool = False
    tight: bool = False
    list: bool = False
    mapping: bool = False
    word_space: bool = False
    unit_word: bool = False
    unit_op: bool = False
    apply: bool = False
    unary: Optional[str] = None
    post_sign: Optional[str] = None
    lower: Optional[str] = None
    upper: Optional[str] = None
    alts: Optional[list] = None
    alts_upper: Optional[list] = None
    coh: Optional[str] = None
    render: Optional[Callable] = None
    variants: Optional[list] = None      # list[(arity, render, accept)]

    def render_for(self, argc):
        if self.variants:
            for (ar, rnd, _acc) in self.variants:
                if ar == argc:
                    return rnd
        return self.render


VOCAB: dict[str, VocabEntry] = {}
SPLITTABLE: set[str] = set()
ROLE: dict[str, str] = {}
SEP = {",": 0, ";": 2}


def loose(sym):
    e = VOCAB.get(sym)
    return e.looseness if e else 0.0


# ── substitution single-pass des templates ({0}..{5}) ────────────────────────
_PH = re.compile(r"\{([0-9])\}")
def _fill(tmpl, a):
    return _PH.sub(lambda m: a[int(m.group(1))], tmpl)


# ── guards (sur les Node parsés) ─────────────────────────────────────────────
def _numeric(n):
    return n.type == "atom" and not n.hole and n.sym and n.sym[0].isdigit()
def _non_numeric(n):
    return not _numeric(n)
def _name_atom(n):
    return n.hole or (n.type == "atom" and n.sym and not n.sym[0].isdigit())
def _tight_body(n):
    return _non_numeric(n) and not (n.type == "infix" and loose(n.sym) >= QUANT)

_GUARDS = {
    "tight_body_0": lambda p: _tight_body(p[0]),
    "non_numeric_1": lambda p: _non_numeric(p[1]),
    "name_atom_tail": lambda p: all(_name_atom(x) for x in p[1:]),
}


# ── render-hooks (logique conditionnelle, référencée par nom) ─────────────────
def _hook(name, params):
    if name == "frac_or_setminus":
        def f(a, n):
            if n and n.parts and len(n.parts) > 1 and n.parts[1].type == "set":
                return f"{a[0]}\\setminus {a[1]}"
            return f"\\frac{{{a[0]}}}{{{a[1]}}}"
        return f
    if name == "neg":
        return lambda a, n: f"-({a[0]})" if loose(n.parts[0].sym) >= SUM else f"-{a[0]}"
    if name == "pos":
        return lambda a, n: f"+({a[0]})" if loose(n.parts[0].sym) >= SUM else f"+{a[0]}"
    if name == "unit":
        return lambda a, n: f"{a[0]}{(chr(92) + ',') if (n and n.spaced) else ''}\\mathrm{{{a[1]}}}"
    if name == "deco":
        wide, narrow = params["wide"], params["narrow"]
        return lambda a, n: (f"\\{wide}{{{a[0]}}}" if len(a[0]) > 1 else f"\\{narrow}{{{a[0]}}}")
    raise ValueError(f"renderHook inconnu : {name}")


def _build_entry(j) -> VocabEntry:
    e = VocabEntry()
    if "shape" in j: e.shape = j["shape"]
    if "arity" in j: e.arity = int(j["arity"])
    if "class" in j: e.cls = j["class"]
    if "looseness" in j: e.looseness = LOOSE[j["looseness"]]
    for flag, attr in (("bracketed", "bracketed"), ("cut", "cut"), ("implicit", "implicit"),
                       ("sup", "sup"), ("sub", "sub"), ("tight", "tight"), ("list", "list"),
                       ("mapping", "mapping"), ("wordSpace", "word_space"),
                       ("unitWord", "unit_word"), ("unitOp", "unit_op"), ("apply", "apply")):
        if j.get(flag):
            setattr(e, attr, True)
    if "unary" in j: e.unary = j["unary"]
    if "postSign" in j: e.post_sign = j["postSign"]
    if "lower" in j: e.lower = j["lower"]
    if "upper" in j: e.upper = j["upper"]
    if "alts" in j: e.alts = list(j["alts"])
    if "altsUpper" in j: e.alts_upper = list(j["altsUpper"])
    if "coh" in j: e.coh = j["coh"]

    if "renderHook" in j:
        e.render = _hook(j["renderHook"], j.get("hookParams"))
    elif "render" in j:
        tmpl = j["render"]
        if "renderImplicit" in j:
            timpl = j["renderImplicit"]
            e.render = lambda a, n, t=tmpl, ti=timpl: _fill(ti if (n and n.implicit) else t, a)
        else:
            e.render = lambda a, n, t=tmpl: _fill(t, a)

    if "variants" in j:
        e.variants = []
        for v in j["variants"]:
            t = v["render"]
            rnd = (lambda a, n, t=t: _fill(t, a))
            acc = _GUARDS[v["acceptGuard"]] if "acceptGuard" in v else None
            e.variants.append((int(v["arity"]), rnd, acc))
    return e


def _build():
    from . import data as _d
    cult = _d.load("cultures.json")
    units = _d.load("units.json")
    symbols = _SYM["symbols"]

    # pass 1 : entrées directes
    for key, j in symbols.items():
        if "sameAs" not in j:
            VOCAB[key] = _build_entry(j)
    # pass 2 : sameAs = clone + overrides
    import copy
    for key, j in symbols.items():
        if "sameAs" not in j:
            continue
        e = copy.copy(VOCAB[j["sameAs"]])
        if "wordSpace" in j:
            e.word_space = bool(j["wordSpace"])
        VOCAB[key] = e

    # unités : un SYMBOLE prime sur l'unité homonyme (ne remplir que le libre).
    seen = set()
    for cat in units["categories"]:
        for w in cat:
            if w not in seen:
                seen.add(w)
                if w not in VOCAB:
                    VOCAB[w] = VocabEntry(unit_word=True)

    # splittable (liste data)
    for w in _SYM["splittable"]:
        SPLITTABLE.add(w)

    # role
    for key, e in VOCAB.items():
        if e.implicit: ROLE["implicit"] = key
        if e.sup: ROLE["sup"] = key
        if e.sub: ROLE["sub"] = key
        if e.unit_op: ROLE["unitOp"] = key
        if e.apply: ROLE["apply"] = key


_build()
