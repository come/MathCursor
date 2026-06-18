"""AST -> StarMath (format formule natif de LibreOffice Math).

P3 du programme moteur portable. Rend depuis l'AST (pas de re-parsing du LaTeX).
ATTENTION : la justesse fine de la syntaxe StarMath n'est confirmable que
visuellement dans LibreOffice (P4) — ce module est best-effort + couverture (ne
plante sur aucune construction du moteur). Cf. ADR portable-engine.
"""
import re
from . import data
from .vocab import VOCAB

# sameAs (×->*, ≤-><=, ·forallWord->forall…) : un nœud peut porter la clé alias
# (ex. Role["implicit"] = "×") — on normalise vers la clé de base pour le mapping.
_SAMEAS = {k: v["sameAs"] for k, v in data.load("symbols.json")["symbols"].items() if "sameAs" in v}


def _canon(sym):
    return _SAMEAS.get(sym, sym)

# ── atomes : fragment LaTeX -> StarMath ──────────────────────────────────────
_ATOM = {
    "\\infty ": "infinity",
    "\\emptyset ": "emptyset",
    "\\ldots ": "dotslow",
    "\\cdots ": "cdots",
    "\\vdots ": "vdots",
    "\\ddots ": "ddots",
    "\\square ": "{}",
}
_MATHBB = re.compile(r"^\\mathbb\{([A-Z])\} ?$")
_GREEK = re.compile(r"^\\([a-zA-Z]+) $")


def _atom_sm(sym):
    if sym is None:
        return "{}"
    if sym in _ATOM:
        return _ATOM[sym]
    m = _MATHBB.match(sym)
    if m:
        return "set" + m.group(1)
    m = _GREEK.match(sym)
    if m:
        name = m.group(1)
        return "%" + (name.upper() if name[0].isupper() else name)
    return sym.replace("{,}", ",")


# ── opérateurs infixes : sym canonique -> gabarit StarMath ({0}/{1}) ──────────
_INFIX = {
    "+": "{0} + {1}", "-": "{0} - {1}",
    ".": "{0} cdot {1}", "·div": "{0} div {1}",
    "=": "{0} = {1}", "<": "{0} < {1}", ">": "{0} > {1}",
    "!=": "{0} <> {1}", ">=": "{0} >= {1}", "<=": "{0} <= {1}",
    "~": "{0} sim {1}", "->": "{0} rightarrow {1}", "mapsto": "{0} rightarrow {1}",
    "in": "{0} in {1}", "notin": "{0} notin {1}",
    "subset": "{0} subset {1}", "subseteq": "{0} subseteq {1}",
    "supset": "{0} supset {1}", "supseteq": "{0} supseteq {1}",
    "notsubset": "{0} nsubset {1}",
    "and": "{0} and {1}", "or": "{0} or {1}",
    "equiv": "{0} equiv {1}", "cong": "{0} cong {1}",
    "approx": "{0} approx {1}", "propto": "{0} prop {1}",
    "union": "{0} union {1}", "inter": "{0} intersection {1}", "setminus": "{0} setminus {1}",
    "mod": "{0} mod {1}", "perp": "{0} ortho {1}", "circ": "{0} circ {1}",
    "pm": "{0} +- {1}", "mp": "{0} -+ {1}", "parallel": "{0} parallel {1}",
    "·colon": "{0} : {1}", "·mid": "{0} divides {1}",
}
# fonctions « standard » StarMath (rendues nom + arg)
_STD_FUNC = {"cos", "sin", "tan", "arcsin", "arccos", "arctan", "ln", "log"}
# décorations
_DECO = {"bar": "overline", "vec": "vec", "hat": "hat"}


def _g(n, cu):
    """opérande groupée : accolades StarMath si composite (atome laissé nu)."""
    s = _sm(n, cu)
    return s if n.type == "atom" else "{" + s + "}"


def _prefix_sm(n, cu):
    sym = _canon(n.sym)
    a0 = n.parts[0]
    if sym in _STD_FUNC:
        return sym + " " + _g(a0, cu)
    if sym in _DECO:
        return _DECO[sym] + " " + _g(a0, cu)
    if sym == "exp":
        return "func e^" + _g(a0, cu)
    if sym in ("sinh", "cosh", "tanh", "coth", "arg", "Re", "Im", "det", "dim", "pgcd", "ppcm"):
        return "func " + sym + " " + _g(a0, cu)
    if sym == "sqrt":
        return "sqrt " + _g(a0, cu)
    if sym == "abs":
        return "abs " + _g(a0, cu)
    if sym == "norm":
        return "norm " + _g(a0, cu)
    if sym == "floor":
        return "lfloor " + _sm(a0, cu) + " rfloor"
    if sym == "ceil":
        return "lceil " + _sm(a0, cu) + " rceil"
    if sym in ("forall", "exists"):
        return sym + " " + _g(a0, cu)
    if sym == "nexists":
        return "not exists " + _g(a0, cu)
    if sym == "not":
        return "neg " + _g(a0, cu)
    if sym == "partial":
        return "partial " + _g(a0, cu)
    if sym == "nabla":
        return "nabla " + _g(a0, cu)
    if sym == "neg":
        return "-" + _g(a0, cu)
    if sym == "pos":
        return "+" + _g(a0, cu)
    if sym == "upm":
        return "+-" + _g(a0, cu)
    if sym == "ump":
        return "-+" + _g(a0, cu)
    return "func " + (sym or "?") + " " + _g(a0, cu)


def _postfix_sm(n, cu):
    sym = _canon(n.sym)
    base = _g(n.parts[0], cu)
    if sym == "!":
        return base + "!"
    if sym == "'":
        return base + "'"
    if sym == "%":
        return base + " \"%\""
    if sym == "°":
        return base + "^circ"
    if sym == "°C":
        return base + "^circ roman C"
    if sym == "°F":
        return base + "^circ roman F"
    return base


def _nary_sm(n, cu):
    sym = _canon(n.sym)
    p = n.parts
    k = len(p)
    if sym == "root":
        return "nroot " + _g(p[0], cu) + " " + _g(p[1], cu)
    if sym == "binom":
        return "binom " + _g(p[0], cu) + " " + _g(p[1], cu)
    if sym == "dot":
        return "langle " + _sm(p[0], cu) + " , " + _sm(p[1], cu) + " rangle"
    if sym == "lim":
        if k == 1:
            return "lim " + _g(p[0], cu)
        return "lim from {" + _sm(p[0], cu) + " toward " + _sm(p[1], cu) + "} " + _g(p[2], cu)
    if sym == "sum":
        if k == 2:
            return "sum from {" + _sm(p[0], cu) + "} " + _g(p[1], cu)
        return "sum from {" + _sm(p[0], cu) + " = " + _sm(p[1], cu) + "} to {" + _sm(p[2], cu) + "} " + _g(p[3], cu)
    if sym == "prod":
        if k == 2:
            return "prod from {" + _sm(p[0], cu) + "} " + _g(p[1], cu)
        return "prod from {" + _sm(p[0], cu) + " = " + _sm(p[1], cu) + "} to {" + _sm(p[2], cu) + "} " + _g(p[3], cu)
    if sym == "int":
        if k == 2:
            return "int " + _g(p[0], cu) + " \" d\"" + _sm(p[1], cu)
        return "int from {" + _sm(p[0], cu) + "} to {" + _sm(p[1], cu) + "} " + _g(p[2], cu) + " \" d\"" + _sm(p[3], cu)
    if sym in ("iint", "iiint"):
        op = sym
        if (sym == "iint" and k == 3) or (sym == "iiint" and k == 4):
            diffs = "".join(" \" d\"" + _sm(x, cu) for x in p[1:])
            return op + " " + _g(p[0], cu) + diffs
        diffs = "".join(" \" d\"" + _sm(x, cu) for x in p[2:])
        return op + " from {" + _sm(p[0], cu) + "} to {" + _sm(p[1], cu) + "} " + diffs
    # fallback
    return (sym or "?") + " " + " ".join(_g(x, cu) for x in p)


def _infix_sm(n, cu):
    sym = _canon(n.sym)
    a = [_g(c, cu) for c in n.parts]
    if sym == "/":
        return "{" + _sm(n.parts[0], cu) + "} over {" + _sm(n.parts[1], cu) + "}"
    if sym == "·parmi":
        return "binom " + a[1] + " " + a[0]
    if sym == "^":
        return _g(n.parts[0], cu) + "^" + _g(n.parts[1], cu)
    if sym == "_":
        return _g(n.parts[0], cu) + "_" + _g(n.parts[1], cu)
    if sym in ("*", "·apply"):
        return _g(n.parts[0], cu) + " " + _g(n.parts[1], cu) if (n.implicit or sym == "·apply") \
            else _g(n.parts[0], cu) + " times " + _g(n.parts[1], cu)
    if sym == "·unit":
        return _sm(n.parts[0], cu) + " roman " + _atom_sm(n.parts[1].sym)
    tmpl = _INFIX.get(sym)
    if tmpl is not None:
        # Substitution EN UNE PASSE : un opérande peut contenir littéralement
        # « {0} »/« {1} » (ex. fraction « {1} over {2} ») ; un double .replace()
        # corromprait ces occurrences internes.
        return re.sub(r"\{([01])\}", lambda m: a[int(m.group(1))], tmpl)
    return a[0] + " " + (sym or "?") + " " + a[1]


def _sm(n, cu):
    t = n.type
    if t == "atom":
        return _atom_sm(n.sym)
    if t == "paren":
        return "left " + (n.lb or "(") + " " + _sm(n.parts[0], cu) + " right " + (n.rb or ")")
    if t == "tuple":
        return "left ( " + " , ".join(_sm(p, cu) for p in n.parts) + " right )"
    if t == "list":
        return " , ".join(_sm(p, cu) for p in n.parts)
    if t == "set":
        return "lbrace " + " , ".join(_sm(p, cu) for p in n.parts) + " rbrace"
    if t == "interval":
        return "left " + n.lb + " " + _sm(n.parts[0], cu) + " " + cu.interval_sep + " " + _sm(n.parts[1], cu) + " right " + n.rb
    if t == "matrix":
        lb, rb = ("[", "]") if cu.matrix_env == "bmatrix" else ("(", ")")
        body = " ## ".join(" # ".join(_sm(x, cu) for x in row) for row in n.rows)
        return "left " + lb + " matrix{ " + body + " } right " + rb
    if t == "postfix":
        return _postfix_sm(n, cu)
    if t == "prefix":
        return _prefix_sm(n, cu)
    if t == "nary":
        return _nary_sm(n, cu)
    return _infix_sm(n, cu)


def to_starmath(node, cu):
    return _sm(node, cu)
