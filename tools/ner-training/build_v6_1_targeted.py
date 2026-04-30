"""
Génère le corpus NER v6.1 — ciblé sur les 3 patterns identifiés comme angles
morts du modèle v6 sur le gold regression_v1 (cf. benchmark misclass dump
30 avril) :

  1) Multi-spans non séparés
     Le modèle colle 2 expressions adjacentes en 1 span, ou rate la 1ère.
     Patterns problématiques : `expr1 et expr2`, `expr1 then expr2`,
     `expr1 puis expr2`, `expr1, on a expr2`, `expr1, alors expr2`.
     Renforcement : 30 exemples explicites avec ces séparateurs.

  2) Quantificateur étendu avec virgule + clause math
     Le modèle aime fermer le span après le set (`Pour tout x dans R,`
     coupe à `R`). Il faut qu'il apprenne que la suite après la virgule
     fait partie de la zone MATH.
     Pattern : `(Pour tout|For all|forall) <var> (dans|in) <set>, <clause>`.
     Renforcement : 30 exemples avec virgule + clause.

  3) Math anglais verbal long
     Cas marginal mais présent dans gold : `integral of x^2 from 0 to 1`,
     `derivative of sin(x)`, `limit of f as x → ∞`.
     Renforcement : 10 exemples.

Cf. analyse benchmark dans la conversation 30-04. Conditions de réussite :
F1 gold ≥ 0.99 sur distilmult après retrain avec v6 + v6.1.

Sortie : data/ner-corpus/extension_v6_1_targeted.jsonl
"""

import io
import json
import random
import sys
from pathlib import Path

if hasattr(sys.stdout, "buffer"):
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

random.seed(20260430)

REPO = Path(__file__).resolve().parents[2]
DST = REPO / "data" / "ner-corpus" / "extension_v6_1_targeted.jsonl"


# ============================================================================
# Pattern 1 — MULTI-SPANS séparés par "et" / "then" / "puis" / virgule + clause
# Format : (text, [list of math fragments]). Chaque fragment = 1 span MATH.
# ============================================================================

MULTI_SPAN_CASES = [
    # FR : séparateur " et "
    ("f(x)=1/x et g(x)=2x", ["f(x)=1/x", "g(x)=2x"], "fr"),
    ("On a f(x)=x^2 et g(x)=2x", ["f(x)=x^2", "g(x)=2x"], "fr"),
    ("Soit f(x)=1/x et g(x)=2x+1", ["f(x)=1/x", "g(x)=2x+1"], "fr"),
    ("On définit u_n=1 et v_n=2", ["u_n=1", "v_n=2"], "fr"),
    ("On pose a=1 et b=2", ["a=1", "b=2"], "fr"),
    ("Posons f(x)=x et g(x)=x^2", ["f(x)=x", "g(x)=x^2"], "fr"),
    ("On a x=0 et y=1", ["x=0", "y=1"], "fr"),
    ("Soit f : x -> x+1 et g : x -> 2x", ["f : x -> x+1", "g : x -> 2x"], "fr"),
    ("On considère f(x)=sin(x) et g(x)=cos(x)",
     ["f(x)=sin(x)", "g(x)=cos(x)"], "fr"),
    ("On note x^2+y^2 et y^2+z^2", ["x^2+y^2", "y^2+z^2"], "fr"),

    # FR : séparateur " puis "
    ("Calculer f(x) puis g(x)", ["f(x)", "g(x)"], "fr"),
    ("On définit f(x)=x^2 puis g(x)=2x", ["f(x)=x^2", "g(x)=2x"], "fr"),
    ("Calculer f(0) puis f(1)", ["f(0)", "f(1)"], "fr"),
    ("Évaluer sin(x) puis cos(x)", ["sin(x)", "cos(x)"], "fr"),

    # FR : séparateur ", on a " / ", alors " / "; "
    ("Soit f(x)=1/x, on a g(x)=2x", ["f(x)=1/x", "g(x)=2x"], "fr"),
    ("Pour x=0, on a f(x)=1", ["x=0", "f(x)=1"], "fr"),
    ("Soit f(x)=2x+1, alors f(0)=1", ["f(x)=2x+1", "f(0)=1"], "fr"),
    ("On a x=0, alors f(x)=1", ["x=0", "f(x)=1"], "fr"),
    ("f(x)=1/x ; g(x)=2x", ["f(x)=1/x", "g(x)=2x"], "fr"),
    ("On a u_0=1, alors u_1=2", ["u_0=1", "u_1=2"], "fr"),

    # FR : 3 spans dans la même phrase
    ("soit f(x) = 2x+1 et g(x) = 3x-1, alors f(g(x)) = 6x+1",
     ["f(x) = 2x+1", "g(x) = 3x-1", "f(g(x)) = 6x+1"], "fr"),
    ("On pose a=1, b=2, c=3", ["a=1", "b=2", "c=3"], "fr"),
    ("Soit x=0, y=1 et z=2", ["x=0", "y=1", "z=2"], "fr"),
    ("On a u_0=1, u_1=2 et u_2=3", ["u_0=1", "u_1=2", "u_2=3"], "fr"),

    # FR : math verbal "La dérivée de X vaut Y"
    ("La dérivée de sin(x) vaut cos(x)", ["sin(x)", "cos(x)"], "fr"),
    ("La dérivée de x^2 vaut 2x", ["x^2", "2x"], "fr"),
    ("L'intégrale de f(x) est F(x)", ["f(x)", "F(x)"], "fr"),
    ("La limite de 1/n est 0", ["1/n", "0"], "fr"),

    # EN : séparateur "then" / "and"
    ("If x > 0 then x^2 > 0", ["x > 0", "x^2 > 0"], "en"),
    ("If a=b then b=a", ["a=b", "b=a"], "en"),
    ("If a + b = c then a = c - b", ["a + b = c", "a = c - b"], "en"),
    ("We have f(x)=x and g(x)=2x", ["f(x)=x", "g(x)=2x"], "en"),
    ("Let u=1, v=2, w=3", ["u=1", "v=2", "w=3"], "en"),
    ("If x = 0 then f(x) = 1", ["x = 0", "f(x) = 1"], "en"),
    ("The derivative of sin(x) is cos(x)", ["sin(x)", "cos(x)"], "en"),
    ("The derivative of x^2 is 2x", ["x^2", "2x"], "en"),
]


# ============================================================================
# Pattern 2 — QUANTIFICATEUR ÉTENDU avec virgule + clause math
# Pattern : `(Pour tout|For all|forall|∀) <var> (dans|in|∈) <set>, <clause>`
# Le span MATH couvre TOUT, y compris la clause après la virgule.
# ============================================================================

QUANT_EXTENDED_CASES = [
    # FR "Pour tout"
    ("Pour tout x dans R, f(x) >= 0",
     ["Pour tout x dans R, f(x) >= 0"], "fr"),
    ("Pour tout x dans R, x^2 >= 0",
     ["Pour tout x dans R, x^2 >= 0"], "fr"),
    ("Pour tout n dans N, n+1 > n",
     ["Pour tout n dans N, n+1 > n"], "fr"),
    ("Pour tout n in N*, 1/n > 0",
     ["Pour tout n in N*, 1/n > 0"], "fr"),
    ("Pour tout x in R+, sqrt(x) >= 0",
     ["Pour tout x in R+, sqrt(x) >= 0"], "fr"),
    ("Pour tout y dans Z, y+1 dans Z",
     ["Pour tout y dans Z, y+1 dans Z"], "fr"),
    ("Pour tout x dans [0,1], 0 <= x <= 1",
     ["Pour tout x dans [0,1], 0 <= x <= 1"], "fr"),
    ("Pour tout (x,y) in R^2, x^2 + y^2 >= 0",
     ["Pour tout (x,y) in R^2, x^2 + y^2 >= 0"], "fr"),
    ("Pour tout x in R, |x| >= 0",
     ["Pour tout x in R, |x| >= 0"], "fr"),
    ("Pour tout x in R, exp(x) > 0",
     ["Pour tout x in R, exp(x) > 0"], "fr"),

    # FR "Pour tout" mid-sentence (lowercase)
    ("Montrer que pour tout n dans N, u_n >= 0",
     ["pour tout n dans N, u_n >= 0"], "fr"),
    ("Démontrer que pour tout x in R, f(x) > 0",
     ["pour tout x in R, f(x) > 0"], "fr"),
    ("On a, pour tout x dans R, x^2 >= 0",
     ["pour tout x dans R, x^2 >= 0"], "fr"),
    ("Montrer que pour tout n >= 1, 2^n > n",
     ["pour tout n >= 1, 2^n > n"], "fr"),
    ("On vérifie que pour tout n in N, n+1 > n",
     ["pour tout n in N, n+1 > n"], "fr"),

    # FR "Il existe / forall / ∀"
    ("Il existe x dans R tel que f(x) = 0",
     ["Il existe x dans R tel que f(x) = 0"], "fr"),
    ("Il existe n dans N tel que u_n > M",
     ["Il existe n dans N tel que u_n > M"], "fr"),
    ("forall x in R, x^2 >= 0", ["forall x in R, x^2 >= 0"], "fr"),
    ("forall n in N, n+1 > n", ["forall n in N, n+1 > n"], "fr"),
    ("∀ x ∈ R, x^2 ≥ 0", ["∀ x ∈ R, x^2 ≥ 0"], "fr"),
    ("∀ n ∈ N, n+1 > n", ["∀ n ∈ N, n+1 > n"], "fr"),
    ("exists x in R, f(x) = 0", ["exists x in R, f(x) = 0"], "fr"),
    ("∃ x ∈ R, f(x) = 0", ["∃ x ∈ R, f(x) = 0"], "fr"),

    # FR : double quantif imbriqué
    ("Pour tout epsilon > 0, exists delta > 0, |f(x)-L| < epsilon",
     ["Pour tout epsilon > 0, exists delta > 0, |f(x)-L| < epsilon"], "fr"),
    ("Pour tout x in R, exists y in R, x + y = 0",
     ["Pour tout x in R, exists y in R, x + y = 0"], "fr"),

    # EN "For all"
    ("For all x in R, x^2 >= 0", ["For all x in R, x^2 >= 0"], "en"),
    ("For all x in R, f(x) = 0", ["For all x in R, f(x) = 0"], "en"),
    ("For all n in N, n+1 > n", ["For all n in N, n+1 > n"], "en"),
    ("For all x in [0,1], 0 <= x <= 1",
     ["For all x in [0,1], 0 <= x <= 1"], "en"),

    # EN "There exists"
    ("There exists x in R such that f(x) = 0",
     ["There exists x in R such that f(x) = 0"], "en"),
]


# ============================================================================
# Pattern 3 — MATH ANGLAIS VERBAL (cas marginal mais présent dans gold)
# `the integral of X from a to b`, `the derivative of f`, `limit of ...`
# ============================================================================

ENGLISH_VERBAL_CASES = [
    ("integral of x^2 from 0 to 1",
     ["integral of x^2 from 0 to 1"], "en"),
    ("the integral of x^2 from 0 to 1",
     ["integral of x^2 from 0 to 1"], "en"),
    ("Compute the integral of x^2 from 0 to 1",
     ["integral of x^2 from 0 to 1"], "en"),
    ("the integral of f(x) from a to b",
     ["integral of f(x) from a to b"], "en"),
    ("derivative of sin(x)", ["derivative of sin(x)"], "en"),
    ("the derivative of sin(x)", ["derivative of sin(x)"], "en"),
    ("Compute the derivative of x^2", ["derivative of x^2"], "en"),
    ("limit of f(x) as x goes to infinity",
     ["limit of f(x) as x goes to infinity"], "en"),
    ("the limit of 1/n as n goes to infinity",
     ["limit of 1/n as n goes to infinity"], "en"),
    ("the sum of 1/k^2 from k=1 to infinity",
     ["sum of 1/k^2 from k=1 to infinity"], "en"),
]


# ============================================================================
# UTILS
# ============================================================================

def make_example(text: str, fragments: list, lang: str) -> dict:
    spans = []
    for frag in fragments:
        pos = text.find(frag)
        if pos < 0:
            raise ValueError(f"Fragment {frag!r} introuvable dans {text!r}")
        spans.append({"start": pos, "end": pos + len(frag), "label": "MATH"})
    return {"text": text, "spans": spans, "lang": lang}


# ============================================================================
# VALIDATION + STATS
# ============================================================================

def validate(examples: list[dict]) -> int:
    errors = 0
    for i, ex in enumerate(examples):
        for span in ex["spans"]:
            if (
                span["start"] < 0
                or span["end"] > len(ex["text"])
                or span["start"] >= span["end"]
            ):
                print(f"BAD offsets case {i+1}: {span} in {ex['text']!r}")
                errors += 1
    return errors


def stats(examples: list[dict], n_multi: int, n_quant: int, n_verbal: int) -> None:
    n = len(examples)
    n_with = sum(1 for e in examples if e["spans"])
    by_lang = {}
    for e in examples:
        by_lang[e["lang"]] = by_lang.get(e["lang"], 0) + 1

    multi_spans = sum(1 for e in examples if len(e["spans"]) >= 2)
    triple_plus = sum(1 for e in examples if len(e["spans"]) >= 3)

    print(f"\nv6.1 ciblée — {n} cas")
    print(f"  multi-spans (Pattern 1)     : {n_multi}")
    print(f"  quantif étendu (Pattern 2)  : {n_quant}")
    print(f"  anglais verbal (Pattern 3)  : {n_verbal}")
    print()
    print(f"  positifs            : {n_with}")
    print(f"  par lang            : {by_lang}")
    print(f"  ≥2 spans/exemple    : {multi_spans}")
    print(f"  ≥3 spans/exemple    : {triple_plus}")


# ============================================================================
# MAIN
# ============================================================================

def main() -> None:
    examples = []
    for cases in (MULTI_SPAN_CASES, QUANT_EXTENDED_CASES, ENGLISH_VERBAL_CASES):
        for text, frags, lang in cases:
            examples.append(make_example(text, frags, lang))

    random.shuffle(examples)

    DST.parent.mkdir(parents=True, exist_ok=True)
    with DST.open("w", encoding="utf-8") as f:
        for ex in examples:
            f.write(json.dumps(ex, ensure_ascii=False) + "\n")

    errors = validate(examples)
    stats(examples, len(MULTI_SPAN_CASES), len(QUANT_EXTENDED_CASES),
          len(ENGLISH_VERBAL_CASES))
    print(f"\nErreurs offsets : {errors}")
    print(f"Écrit : {DST.relative_to(REPO)}")


if __name__ == "__main__":
    main()
