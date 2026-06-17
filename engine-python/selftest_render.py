#!/usr/bin/env python3
"""P2 — self-test du builder + renderer Python sur la VRAIE data universelle
(data/engine/*.json). Prouve que Python charge symbols.json/cultures.json et rend
un LaTeX identique au C# sur des AST construits main (templates, parenthésage par
looseness, dissolution paren sous frac, hooks deco/neg, n-aire). Le lexer/parser/
score arrivent ensuite (conformance fixtures)."""
import sys
from mc_engine.node import Node
from mc_engine.render import render
from mc_engine import culture

CU = culture.FR


def atom(s): return Node(type="atom", sym=s)
def inf(sym, *parts, **kw): return Node(type="infix", sym=sym, parts=list(parts), **kw)
def pre(sym, *parts): return Node(type="prefix", sym=sym, parts=list(parts))
def nary(sym, *parts): return Node(type="nary", sym=sym, parts=list(parts))
def paren(x): return Node(type="paren", parts=[x])

CASES = [
    ("cos x",     pre("cos", atom("x")),                              "\\cos(x)"),
    ("sqrt x",    pre("sqrt", atom("x")),                             "\\sqrt{x}"),
    ("pi",        atom("\\pi "),                                      "\\pi "),
    ("1/2",       inf("/", atom("1"), atom("2")),                     "\\frac{1}{2}"),
    ("(U_n)/2",   inf("/", paren(inf("_", atom("U"), atom("n"))), atom("2")), "\\frac{U_{n}}{2}"),
    ("(a+b)*c",   inf("*", inf("+", atom("a"), atom("b")), atom("c")), "(a+b)\\times c"),
    ("a*b glued", inf("*", atom("a"), atom("b"), implicit=True),      "ab"),
    ("x^2",       inf("^", atom("x"), atom("2")),                     "x^{2}"),
    ("vec AB",    pre("vec", atom("AB")),                             "\\overrightarrow{AB}"),
    ("conj z",    pre("bar", atom("z")),                              "\\bar{z}"),
    ("neg x",     pre("neg", atom("x")),                              "-x"),
    ("neg(a+b)",  pre("neg", inf("+", atom("a"), atom("b"))),         "-(a+b)"),
    ("sum",       nary("sum", atom("k"), atom("1"), atom("n"), atom("k^{2}")), "\\sum_{k=1}^{n} k^{2}"),
]

ok = 0
for label, ast, expected in CASES:
    got = render(ast, CU)
    good = got == expected
    ok += good
    print(f"  [{'OK ' if good else 'XX '}] {label:11} -> {got!r}" + ("" if good else f"   ATTENDU {expected!r}"))
print(f"\n{ok}/{len(CASES)} rendus identiques (Python, sur la vraie data/engine/)")
sys.exit(0 if ok == len(CASES) else 1)
