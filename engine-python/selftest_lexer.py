#!/usr/bin/env python3
"""Smoke-test du lexer Python (port de Lexer.cs). Ne PROUVE pas la justesse
complète (ça viendra avec parser+score sur les fixtures) — confirme que le lexer
tourne sur des entrées variées sans exception et produit des tokens plausibles."""
import sys
from mc_engine import culture
from mc_engine.lexer import lex, lex_all

FR = culture.FR


def toks_of(s):
    return lex(s, FR)


def tag(t):
    return t.kind + (("=" + t.sym) if t.sym else ("=" + "|".join(t.syms) if t.syms else ""))


INPUTS = ["cos x", "x2", "1/x+1", "lim x 0 g(x)", "sum k 1 n k2", "pi r2",
          "f(x)=2x+1", "vec AB", "conjz", "x dans R", "4,5 cm", "[0;1]",
          "(1,2)", "x^2+1/2", "a : b"]

fail = 0
for s in INPUTS:
    try:
        ts = toks_of(s)
        streams = lex_all(s, FR)
        print(f"  {s!r:22} -> {len(ts)} toks, {len(streams)} flux : " + " ".join(tag(t) for t in ts))
    except Exception as e:
        fail += 1
        print(f"  {s!r:22} -> EXCEPTION {e}")

# quelques vérifs précises de tokenisation
def kinds(s): return [t.kind for t in toks_of(s)]
checks = [
    ("x2 -> sup collé",      lambda: [t.kind for t in toks_of("x2")] == ["atom", "infix", "atom"]
                                     and toks_of("x2")[1].sticky),
    ("cos x -> prefix",      lambda: toks_of("cos x")[0].kind == "prefix" and toks_of("cos x")[0].sym == "cos"),
    ("dans -> alias in",     lambda: any(t.sym == "in" for t in toks_of("x dans R"))),
    ("conjz splitte (lex_all multi-flux)", lambda: len(lex_all("conjz", FR)) >= 2),
]
for label, fn in checks:
    ok = False
    try:
        ok = bool(fn())
    except Exception as e:
        label += f" (EX {e})"
    print(f"  [{'OK ' if ok else 'XX '}] {label}")
    fail += 0 if ok else 1

print(f"\n{'OK' if fail == 0 else 'FAIL'} — {fail} problème(s)")
sys.exit(0 if fail == 0 else 1)
