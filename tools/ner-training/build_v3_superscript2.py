"""
Génère des exemples d'entraînement avec le caractère AZERTY '²' (U+00B2).

Contexte : le corpus v2 ne contient AUCUN '²' natif. Les élèves FR sur
clavier AZERTY tapent 'x²' (pas 'x^2'). XLM-R / SentencePiece tokenise '²'
sans contexte d'entraînement → détection aléatoire.

Ce script génère ~150 lignes FR + EN couvrant formules isolées, prose longue
et SMS avec le '²'.

Sortie : data/ner-corpus/extension_v3_superscript2.jsonl
"""

import json
import random
from pathlib import Path

random.seed(424242)

REPO = Path(__file__).resolve().parents[2]
DST = REPO / "data" / "ner-corpus" / "extension_v3_superscript2.jsonl"


FORMULAS_SQ = [
    "x²", "2x²", "3x²", "-x²", "x²+1", "x²-4", "x²+2x+1",
    "(x+1)²", "(x-1)²", "(a+b)²", "(a-b)²", "(2x+3)²",
    "x²+y²=1", "x²+y²=r²", "a²+b²=c²",
    "f(x)=x²", "f(x)=x²+1", "f(x)=x²-3x+2", "g(x)=(x+1)²",
    "πr²", "4πr²", "πd²/4",
    "sin²(x)+cos²(x)=1", "sin²(x)", "cos²(x)", "tan²(x)+1=1/cos²(x)",
    "σ²", "E(X²)", "V(X)=E(X²)-E(X)²",
    "|z|²", "||u||²",
    "n²", "(n+1)²", "n²-1=(n-1)(n+1)",
    "3²=9", "5²=25", "7²=49", "10²=100",
    "x²>=0", "x² > 0",
    "v²=2as", "E=mc²",
]


FR_PROSE_TEMPLATES = [
    "On a {F}.",
    "Soit {F}.",
    "On pose {F}.",
    "On en déduit que {F}.",
    "Il est clair que {F} est toujours vrai.",
    "Calculer {F}.",
    "Il faut résoudre {F}.",
    "Par identité remarquable, {F}.",
    "On remarque que {F} pour tout réel.",
    "L'aire d'un carré de côté a vaut {F}.",
    "L'aire du disque est {F}.",
    "Le volume d'une sphère utilise {F} dans la formule.",
    "Pour tout x réel, {F}.",
    "D'après le théorème de Pythagore, {F}.",
    "La variance se calcule par {F}.",
    "On sait que {F}.",
    "Montrer que {F} est positif.",
    "En développant, on obtient {F}.",
    "On factorise : {F}.",
    "Le professeur a insisté sur {F}.",
    "Il faut se rappeler que {F}.",
    "Le discriminant est {F}.",
    "Après simplification, {F}.",
    "On étudie la fonction définie par {F}.",
    "Dans cet exercice, on supposera {F}.",
]

EN_PROSE_TEMPLATES = [
    "We have {F}.",
    "Let {F}.",
    "Note that {F}.",
    "It is clear that {F}.",
    "Compute {F}.",
    "The area of a square of side a is {F}.",
    "The area of the disk is {F}.",
    "By the Pythagorean theorem, {F}.",
    "For all real x, {F}.",
    "We notice that {F}.",
    "The teacher emphasized {F}.",
    "Remember that {F}.",
    "The discriminant is {F}.",
    "After simplification, {F}.",
    "We study the function defined by {F}.",
    "In this exercise, assume {F}.",
    "Expanding gives {F}.",
    "Factoring : {F}.",
    "One shows that {F} holds.",
    "Variance is given by {F}.",
]


FR_SMS_PREFIXES = [
    "jpp", "tkt", "bref", "franchement", "genre", "frr",
    "jvois pas pk", "mdr c'est", "euh", "bon",
    "jsp si", "ouais bah",
]

FR_SMS_SUFFIXES = [
    "frr", "jpp", "mdr", "enfin je crois", "jsp trop",
    "bref", "c clair", "voila quoi", "c bon", "ptdr",
]

EN_SMS_PREFIXES = [
    "so like", "ok so", "basically", "lol", "wait",
    "idk but", "pretty sure", "tbh",
]

EN_SMS_SUFFIXES = [
    "right", "idk", "lol", "tbh", "fr fr", "no cap",
    "honestly", "i guess",
]


def make_span(text: str, fragment: str) -> dict | None:
    pos = text.find(fragment)
    if pos < 0:
        return None
    return {"start": pos, "end": pos + len(fragment), "label": "MATH"}


def gen_formula_only(lang: str) -> dict:
    f = random.choice(FORMULAS_SQ)
    return {"text": f, "spans": [{"start": 0, "end": len(f), "label": "MATH"}], "lang": lang}


def gen_prose(lang: str) -> dict:
    templates = FR_PROSE_TEMPLATES if lang == "fr" else EN_PROSE_TEMPLATES
    tpl = random.choice(templates)
    f = random.choice(FORMULAS_SQ)
    text = tpl.replace("{F}", f)
    span = make_span(text, f)
    return {"text": text, "spans": [span] if span else [], "lang": lang}


def gen_sms(lang: str) -> dict:
    if lang == "fr":
        prefixes, suffixes = FR_SMS_PREFIXES, FR_SMS_SUFFIXES
    else:
        prefixes, suffixes = EN_SMS_PREFIXES, EN_SMS_SUFFIXES

    f = random.choice(FORMULAS_SQ)
    p = random.choice(prefixes)
    s = random.choice(suffixes)
    text = f"{p} {f} {s}"
    span = make_span(text, f)
    return {"text": text, "spans": [span] if span else [], "lang": lang}


def validate(examples: list[dict]) -> int:
    errors = 0
    for i, ex in enumerate(examples):
        for span in ex["spans"]:
            if span["start"] < 0 or span["end"] > len(ex["text"]) or span["start"] >= span["end"]:
                print(f"BAD offsets line {i+1}: {span} in {ex['text']!r}")
                errors += 1
            if "²" not in ex["text"][span["start"]:span["end"]]:
                # pas bloquant (quelques formules n'ont pas de ² ex: "1=1/cos²(x)"
                # — mais la string globale oui) ; on juste log
                pass
    return errors


def main():
    examples = []

    for lang in ("fr", "en"):
        for _ in range(25):
            examples.append(gen_formula_only(lang))
        for _ in range(35):
            examples.append(gen_prose(lang))
        for _ in range(15):
            examples.append(gen_sms(lang))

    random.shuffle(examples)

    DST.parent.mkdir(parents=True, exist_ok=True)
    with DST.open("w", encoding="utf-8") as f:
        for ex in examples:
            ex["spans"] = [s for s in ex["spans"] if s is not None]
            f.write(json.dumps(ex, ensure_ascii=False) + "\n")

    errors = validate(examples)
    with_sq = sum(1 for e in examples if "²" in e["text"])

    print(f"\nGénérés     : {len(examples)} lignes")
    print(f"  avec '²'  : {with_sq}")
    print(f"  FR / EN   : {sum(1 for e in examples if e['lang']=='fr')} / {sum(1 for e in examples if e['lang']=='en')}")
    print(f"  erreurs   : {errors}")
    print(f"\nÉcrit : {DST.relative_to(REPO)}")


if __name__ == "__main__":
    main()
