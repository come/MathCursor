#!/usr/bin/env python3
"""Runner de conformité P2 : exécute les 434 fixtures (engine/tests/.../fixtures.json)
contre le moteur Python et compare décision + candidats (LaTeX) + note au contrat.
Vise 434/434 (anti-drift C#/Python). Cf. ADR portable-engine-universal-vocab."""
import json
import sys
from pathlib import Path

from mc_engine import culture
from mc_engine.engine import analyze

FIX = Path(__file__).resolve().parents[1] / "engine" / "tests" / "MathCursor.Engine.Tests" / "fixtures.json"
fixtures = json.load(open(FIX, encoding="utf-8"))

fails = []
for f in fixtures:
    cu = culture.US if f.get("culture") == "us" else culture.FR
    exp_dec = f["decision"]
    exp_cands = f["cands"]
    exp_note = f.get("note", False)
    try:
        r = analyze(f["in"], cu)
        got_dec = r.decision
        got_cands = [c.latex for c in r.ranked]
        got_note = r.has_note
    except Exception as e:
        got_dec = "EXCEPTION"
        got_cands = [repr(e)]
        got_note = False
    if got_dec != exp_dec or got_cands != exp_cands or got_note != exp_note:
        fails.append((f["in"], f.get("culture"), exp_dec, exp_cands, exp_note, got_dec, got_cands, got_note))

total = len(fixtures)
print(f"{total - len(fails)}/{total} OK ({len(fails)} échec(s))")
for x in fails[:25]:
    print(f"  IN {x[0]!r} [{x[1] or 'fr'}]")
    print(f"     attendu {x[2]} {x[3]} note={x[4]}")
    print(f"     obtenu  {x[5]} {x[6]} note={x[7]}")
sys.exit(0 if not fails else 1)
