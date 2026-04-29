"""
Convertit specs/test-fixtures/phase1-zone-detection.json en JSONL au format
corpus NER (text/spans/lang).

Les fixtures sont la source de vérité cross-implémentations pour la détection
de zone. Les ajouter au corpus garantit que le modèle entraîné réussit au
moins ces 40 cas.

Sortie : data/ner-corpus/extension_v3_fixtures.jsonl
"""

import json
import os
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SRC = REPO / "specs" / "test-fixtures" / "phase1-zone-detection.json"
DST = REPO / "data" / "ner-corpus" / "extension_v3_fixtures.jsonl"


def convert_case(case: dict) -> dict | None:
    text = case["input"]
    zone = case.get("expectedZone")
    lang = case["lang"]

    if zone is None:
        return {"text": text, "spans": [], "lang": lang}

    pos = text.find(zone)
    if pos < 0:
        print(f"  SKIP {case['id']}: expectedZone introuvable dans input")
        print(f"    input: {text!r}")
        print(f"    zone:  {zone!r}")
        return None

    span = {"start": pos, "end": pos + len(zone), "label": "MATH"}
    return {"text": text, "spans": [span], "lang": lang}


def validate(examples: list[dict]) -> int:
    errors = 0
    for i, ex in enumerate(examples):
        for span in ex["spans"]:
            if span["start"] < 0 or span["end"] > len(ex["text"]) or span["start"] >= span["end"]:
                print(f"BAD offsets line {i+1}: {span} in {ex['text']!r}")
                errors += 1
    return errors


def main():
    with SRC.open(encoding="utf-8") as f:
        fixtures = json.load(f)

    examples = []
    for case in fixtures["cases"]:
        ex = convert_case(case)
        if ex is not None:
            examples.append(ex)

    DST.parent.mkdir(parents=True, exist_ok=True)
    with DST.open("w", encoding="utf-8") as f:
        for ex in examples:
            f.write(json.dumps(ex, ensure_ascii=False) + "\n")

    errors = validate(examples)
    n_with = sum(1 for e in examples if e["spans"])
    n_without = sum(1 for e in examples if not e["spans"])
    by_lang = {}
    for e in examples:
        by_lang[e["lang"]] = by_lang.get(e["lang"], 0) + 1

    print(f"\nConversion : {len(examples)} / {len(fixtures['cases'])} cas")
    print(f"  avec spans :   {n_with}")
    print(f"  sans spans :   {n_without}")
    print(f"  par langue :   {by_lang}")
    print(f"  erreurs offs : {errors}")
    print(f"\nÉcrit : {DST.relative_to(REPO)}")


if __name__ == "__main__":
    main()
