"""
Génère des exemples NER pour les INTERVALLES (renfort : sous-représentés, et
distincts des matrices alors qu'ils partagent le `;`).

Syntaxe MathCursor : crochets ouverts/fermés, séparateur `;`.
  [a;b]  ]a;b[  [a;b[  ]a;b]   bornes : nombres, décimales (1,5), lettres,
  fractions, ±inf, expressions. + appartenance (`x in [0;1]`, `x dans [a;b[`)
  + unions (`[0;1] U [2;3]`). Zone math = l'intervalle (ou l'expr complète).

Distractors (spans=[]) : `;` de PROSE (« ; donc » d'une phrase), et crochets de
CITATION (« voir [3] », « cf. [2] ») — pour ne pas confondre avec un intervalle.

Sortie : data/ner-corpus/extension_v11_intervals.jsonl
"""

import json
import random
import sys
from pathlib import Path

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

random.seed(20260623)

REPO = Path(__file__).resolve().parents[2]
DST = REPO / "data" / "ner-corpus" / "extension_v11_intervals.jsonl"

LO = ["0", "1", "-1", "-2", "a", "x", "0,5", "1,5", "-inf", "1/2", "-3", "n", "-pi"]
HI = ["1", "2", "5", "10", "b", "y", "1,5", "2,5", "+inf", "3/2", "n+1", "pi"]
LB = ["[", "]"]
RB = ["]", "["]


def make_span(text, fragment):
    pos = text.find(fragment)
    if pos < 0:
        return None
    return {"start": pos, "end": pos + len(fragment), "label": "MATH"}


def formula(expr, lang="fr"):
    return {"text": expr, "spans": [{"start": 0, "end": len(expr), "label": "MATH"}], "lang": lang}


FR_TEMPLATES = ["On a {F}.", "Soit {F}.", "x varie dans {F}.",
                "L'ensemble des solutions est {F}.", "Donc {F}."]
EN_TEMPLATES = ["We have {F}.", "Let {F}.", "x lies in {F}.", "So {F}."]


def prose(expr, lang="fr"):
    tpl = random.choice(FR_TEMPLATES if lang == "fr" else EN_TEMPLATES)
    text = tpl.replace("{F}", expr)
    return {"text": text, "spans": [make_span(text, expr)], "lang": lang}


def interval():
    lo, hi = random.choice(LO), random.choice(HI)
    lb, rb = random.choice(LB), random.choice(RB)
    return f"{lb}{lo};{hi}{rb}"


def generate_positives():
    ex = []
    for _ in range(45):
        iv = interval()
        ex.append(formula(iv, "fr"))
        roll = random.random()
        if roll < 0.3:
            ex.append(prose(iv, "fr"))
        elif roll < 0.42:
            ex.append(prose(iv, "en"))
    # appartenance
    for _ in range(18):
        iv = interval()
        kw = random.choice(["x in", "x dans", "x appartient", "t in", "x ∈"])
        expr = f"{kw} {iv}"
        ex.append(formula(expr, "fr"))
        if random.random() < 0.3:
            ex.append(prose(expr, "fr"))
    # unions
    for _ in range(14):
        expr = f"{interval()} U {interval()}"
        ex.append(formula(expr, "fr"))
        if random.random() < 0.3:
            ex.append(prose(expr, "fr"))
    return ex


FR_DISTRACTORS = [
    "Il pleuvait ; pourtant nous sommes sortis.",
    "Range tes affaires ; ensuite on commence.",
    "Trois élèves étaient absents ; les autres présents.",
    "Voir le théorème [3] du chapitre précédent.",
    "La preuve est détaillée dans [2].",
    "Reporte-toi à l'exercice [12] page 40.",
    "Liste des fournitures : cahier ; stylo ; règle.",
]
EN_DISTRACTORS = [
    "It was raining ; we went out anyway.",
    "See theorem [3] in the previous chapter.",
    "The proof is given in [2].",
]


def generate_distractors():
    ex = [{"text": t, "spans": [], "lang": "fr"} for t in FR_DISTRACTORS]
    ex += [{"text": t, "spans": [], "lang": "en"} for t in EN_DISTRACTORS]
    return ex


def validate(examples):
    errors = 0
    for i, ex in enumerate(examples):
        for s in ex["spans"]:
            if s is None or s["start"] < 0 or s["end"] > len(ex["text"]) or s["start"] >= s["end"]:
                print(f"BAD offsets line {i+1}: {s} in {ex['text']!r}")
                errors += 1
    return errors


def stats(examples):
    n = len(examples)
    nw = sum(1 for e in examples if e["spans"])
    by = {}
    for e in examples:
        by[e["lang"]] = by.get(e["lang"], 0) + 1
    print(f"\nTotal      : {n} lignes")
    print(f"  positifs : {nw}")
    print(f"  spans=[] : {n - nw}")
    print(f"  par lang : {by}")


def main():
    examples = generate_positives() + generate_distractors()
    examples = [e for e in examples if all(s is not None for s in e["spans"])]
    random.shuffle(examples)
    DST.parent.mkdir(parents=True, exist_ok=True)
    with DST.open("w", encoding="utf-8") as f:
        for ex in examples:
            f.write(json.dumps(ex, ensure_ascii=False) + "\n")
    errors = validate(examples)
    stats(examples)
    print(f"\nErreurs offsets : {errors}")
    print(f"Écrit : {DST.relative_to(REPO)}")


if __name__ == "__main__":
    main()
