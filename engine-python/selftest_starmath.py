#!/usr/bin/env python3
"""P3 — test du convertisseur StarMath.

(1) COUVERTURE : tout candidat de toutes les fixtures se convertit sans exception.
(2) CORPUS best-effort : StarMath attendu sur des cas AUTO simples (à confirmer
    visuellement dans LibreOffice en P4 — c'est la seule vraie validation syntaxe).
"""
import json
import sys
from pathlib import Path

from mc_engine import culture
from mc_engine.engine import analyze
from mc_engine.starmath import to_starmath

FIX = Path(__file__).resolve().parents[1] / "engine" / "tests" / "MathCursor.Engine.Tests" / "fixtures.json"
fixtures = json.load(open(FIX, encoding="utf-8"))

# (1) couverture
converted = 0
errors = []
for f in fixtures:
    cu = culture.US if f.get("culture") == "us" else culture.FR
    r = analyze(f["in"], cu)
    for c in r.ranked:
        try:
            to_starmath(c.node, cu)
            converted += 1
        except Exception as e:
            errors.append((f["in"], c.latex, repr(e)))
print(f"(1) couverture : {converted} candidats -> StarMath, {len(errors)} erreur(s)")
for e in errors[:15]:
    print("   ERREUR", e)

# (2) corpus best-effort (cas auto, candidat de tête)
CORPUS = [
    ("cos x", "cos x"),
    ("1/2", "{1} over {2}"),
    ("x2", "x^2"),
    ("x_2", "x_2"),
    ("sqrt x", "sqrt x"),
    ("a+b", "a + b"),
    ("2x", "2 x"),
    ("pi", "%pi"),
    # non-régression : addition de fractions — la substitution du gabarit infixe
    # ne doit PAS corrompre le « {1} » interne d'une fraction (one-pass).
    ("1/2+1/3", "{{1} over {2}} + {{1} over {3}}"),
]
miss = 0
for inp, expected in CORPUS:
    r = analyze(inp, culture.FR)
    got = to_starmath(r.ranked[0].node, culture.FR) if r.ranked else "(erreur)"
    ok = got == expected
    miss += 0 if ok else 1
    print(f"   [{'OK ' if ok else 'XX '}] {inp:8} -> {got!r}" + ("" if ok else f"  ATTENDU {expected!r}"))

fail = len(errors) + miss
print(f"\n{'OK' if fail == 0 else 'FAIL'} — couverture {len(errors)} err, corpus {miss} écart")
sys.exit(0 if fail == 0 else 1)
