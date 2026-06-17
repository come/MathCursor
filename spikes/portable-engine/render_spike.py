#!/usr/bin/env python3
"""
SPIKE jetable (P0) — dérisque le COEUR du programme moteur portable :
peut-on porter le RENDU (AST -> LaTeX) en Python en le pilotant 100 % par
une data universelle (vocab.sample.json), y compris :
  - le mini-langage de template ({0}/{1}),
  - la parenthésation pilotée par `looseness` (donnée, pas code),
  - la dissolution des parenthèses tapées sous un parent `bracketed` (frac),
  - les "hooks" nommés pour les 3 rendus conditionnels (ici `/` frac|setminus),
  - le switch template implicite/explicite (x).

Port FIDÈLE de LatexRenderer.Render + Child (engine/src/.../LatexRenderer.cs).
On valide sur des AST construits à la main dont le LaTeX attendu vient des
fixtures / templates C#. Le lexer + forest parser + scorer NE sont PAS portés
ici (c'est l'effort "grind" mesuré à part) — ce spike isole le risque NOUVEAU.
"""
import json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = json.load(open(os.path.join(HERE, "vocab.sample.json"), encoding="utf-8"))
VOCAB = DATA["symbols"]

# Substitution EN UN SEUL PASSAGE sur le template original : un .replace()
# séquentiel re-scanne le texte déjà substitué et un arg numérique collé à une
# accolade littérale recrée un faux placeholder (1/2 -> \frac2{2}). Leçon du
# spike pour le format universel : single-pass obligatoire.
_PH = re.compile(r"\{([01])\}")
def fill(tmpl, a):
    return _PH.sub(lambda m: a[int(m.group(1))], tmpl)


# --- hooks nommés = la logique des rendus conditionnels, portée par langage ---
def hook_frac_or_setminus(a, n):
    # `/` : \setminus si le 2e opérande est un ensemble, sinon \frac (bracketed)
    if n["parts"][1].get("type") == "set":
        return f"{a[0]}\\setminus {a[1]}"
    return fill(VOCAB["/"]["render"], a)

HOOKS = {"frac_or_setminus": hook_frac_or_setminus}


def emit(d, a, n):
    if "renderHook" in d:
        return HOOKS[d["renderHook"]](a, n)
    if d.get("implicit") and n is not None and n.get("implicit"):
        return fill(d["renderImplicit"], a)
    return fill(d["render"], a)


def child(c, parent, cu):
    # port de LatexRenderer.Child
    if c["type"] == "paren":
        return render(c["parts"][0], cu) if parent.get("bracketed") else render(c, cu)
    s = render(c, cu)
    if parent.get("bracketed"):
        return s
    if c["type"] == "atom" and not parent.get("apply"):
        return s
    self_grouped = c.get("sym") in VOCAB and VOCAB[c["sym"]].get("bracketed")
    looser = c["type"] == "infix" and VOCAB[c["sym"]]["looseness"] > parent["looseness"]
    if (c.get("grouped") and not self_grouped) or looser:
        return f"({s})"
    return s


def render(n, cu):
    # port de LatexRenderer.Render (slice : atom/paren/infix/prefix)
    t = n["type"]
    if t == "atom":
        return n["sym"]
    if t == "paren":
        return (n.get("lb") or "(") + render(n["parts"][0], cu) + (n.get("rb") or ")")
    d = VOCAB[n["sym"]]
    if d["shape"] == "infix":
        if d.get("sup") or d.get("sub"):
            a = [child(n["parts"][0], d, cu), render(n["parts"][1], cu)]
        else:
            a = [child(c, d, cu) for c in n["parts"]]
        return emit(d, a, n)
    # prefix : l'argument paren se dissout (la fonction fournit ses délimiteurs)
    a = [render(p["parts"][0], cu) if p["type"] == "paren" else render(p, cu) for p in n["parts"]]
    return emit(d, a, n)


# --- AST de test (construits main) + LaTeX attendu (issus des fixtures/C#) ---
def atom(s): return {"type": "atom", "sym": s}
def inf(sym, l, r, **kw): return dict(type="infix", sym=sym, parts=[l, r], **kw)
def pre(sym, x): return {"type": "prefix", "sym": sym, "parts": [x]}
def paren(x): return {"type": "paren", "parts": [x]}

CASES = [
    ("cos x",     pre("cos", atom("x")),                                  "\\cos(x)"),
    ("sqrt x",    pre("sqrt", atom("x")),                                 "\\sqrt{x}"),
    ("pi",        atom("\\pi "),                                          "\\pi "),
    ("1/2",       inf("/", atom("1"), atom("2")),                         "\\frac{1}{2}"),
    ("(U_n)/2",   inf("/", paren(inf("_", atom("U"), atom("n"))), atom("2")), "\\frac{U_{n}}{2}"),
    ("(a+b)*c",   inf("*", inf("+", atom("a"), atom("b")), atom("c")),    "(a+b)\\times c"),
    ("x^2",       inf("^", atom("x"), atom("2")),                         "x^{2}"),
]

cu = DATA["cultures"]["fr"]
ok = 0
for label, ast, expected in CASES:
    got = render(ast, cu)
    good = got == expected
    ok += good
    print(f"  [{'OK ' if good else 'XX '}] {label:10} -> {got!r}" + ("" if good else f"   ATTENDU {expected!r}"))
print(f"\n{ok}/{len(CASES)} rendus identiques (data-driven, Python)")
sys.exit(0 if ok == len(CASES) else 1)
