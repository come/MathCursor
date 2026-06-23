"""
Génère des exemples NER pour les MATRICES (et tuples/points), 0 occurrence
réelle dans tout le corpus avant v11 (sondé : 12 824 exemples → 0 vraie matrice
≥2 colonnes ; les seuls « ; » sont des intervalles `[0;1]`). Même logique que
v8 (`iint`) et v9 (`approx`) : un angle mort total → ajout explicite.

Syntaxe MathCursor (PAS de LaTeX) : séparateur de colonne `,` OU espace,
séparateur de rang `;`, délimiteurs `( )` ou `[ ]`.
  (a,b,c;d,e,f)   (a b c; d e f)   [1,2;3,4]
La zone math = la matrice ENTIÈRE (un seul span), même longue et à contenu
complexe (fractions, puissances, indices, fonctions, décimales, négatifs).

Couvre : 2×2, 3×3, 4×4, 5×5, vecteurs colonne (N×1), tuples/points (1×N —
traités comme math ambigu tuple/matrice par le moteur → positifs, pour ne pas
casser les coordonnées de v6), en isolé + prose + autocap Word, FR/EN.

Distractors (spans=[]) : parenthèses de PROSE (mots à l'intérieur : « voir
lignes 1, 2 et 3 », « (cahier, stylo, règle) ») — pour que le NER n'avale pas
toute parenthèse comme une matrice.

Sortie : data/ner-corpus/extension_v11_matrices.jsonl
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
DST = REPO / "data" / "ner-corpus" / "extension_v11_matrices.jsonl"

# Cellules à contenu COMPLEXE (le point de la v11).
CELLS = [
    "a", "b", "c", "d", "x", "y", "n", "0", "1", "2", "-1", "-2", "-3",
    "1/2", "3/4", "-1/2", "2/3", "x^2", "x^3", "a^2", "n^2", "2^n",
    "a_1", "a_2", "a_n", "u_n", "x_i", "-x", "2x", "3x", "x+1", "x-1",
    "1,5", "0,5", "2,5", "-0,5", "pi", "2pi", "cos(x)", "sin(x)",
    "sqrt(2)", "sqrt(x)", "f(x)", "1/n", "n+1", "k", "-a",
]

SIMPLE_CELLS = ["a", "b", "c", "d", "e", "f", "x", "y", "1", "2", "3", "0", "-1"]


def make_span(text, fragment):
    pos = text.find(fragment)
    if pos < 0:
        return None
    return {"start": pos, "end": pos + len(fragment), "label": "MATH"}


def cap_first(s):
    return s[:1].upper() + s[1:] if s else s


def build_matrix(r, c, cells, colsep, lb, rb):
    rows = []
    for _ in range(r):
        rows.append(colsep.join(random.choice(cells) for _ in range(c)))
    return lb + ";".join(rows) + rb


def gen_formula_only(expr, lang):
    return {"text": expr, "spans": [{"start": 0, "end": len(expr), "label": "MATH"}], "lang": lang}


FR_TEMPLATES = [
    "Soit {F}.", "On considère {F}.", "La matrice A = {F}.",
    "Calculer le déterminant de {F}.", "On pose M = {F}.",
    "Résoudre avec {F}.", "Le système s'écrit {F}.", "On a {F}.",
]
EN_TEMPLATES = [
    "Let {F}.", "Consider {F}.", "The matrix A = {F}.",
    "Compute the determinant of {F}.", "We set M = {F}.", "We have {F}.",
]


def gen_prose(expr, lang):
    tpl = random.choice(FR_TEMPLATES if lang == "fr" else EN_TEMPLATES)
    text = tpl.replace("{F}", expr)
    return {"text": text, "spans": [make_span(text, expr)], "lang": lang}


# Tuples / points (1×N) et matrices-équation « A = … » : math.
TUPLES = [
    "(1, 2)", "(x, y)", "(1, 2, 3)", "(a, b, c)", "(0, 0)", "(-1, 2)",
    "(x, y, z)", "(1,2)", "(a;b)", "(1;2;3)", "(x;y;z)", "(O, veci, vecj)",
    "(1/2, 3/4)", "(cos(x), sin(x))",
]


def generate_positives():
    ex = []
    sizes = [(2, 2), (3, 3), (3, 3), (4, 4), (4, 4), (5, 5), (5, 5),
             (2, 3), (3, 2), (4, 3), (3, 4), (2, 4), (4, 1), (3, 1), (5, 1)]
    for (r, c) in sizes:
        for _ in range(3):
            cells = CELLS if random.random() < 0.7 else SIMPLE_CELLS
            colsep = random.choice([",", " ", ", "])
            lb, rb = random.choice([("(", ")"), ("[", "]")])
            M = build_matrix(r, c, cells, colsep, lb, rb)
            ex.append(gen_formula_only(M, "fr"))
            roll = random.random()
            if roll < 0.35:
                ex.append(gen_prose(M, "fr"))
            elif roll < 0.5:
                ex.append(gen_prose(M, "en"))
            # autocap Word (matrice de lettres en début de ligne)
            if random.random() < 0.25:
                ex.append(gen_formula_only(cap_first(M), "fr"))
            # matrice-équation « A = M »
            if random.random() < 0.3:
                eq = f"{random.choice(['A','M','B','P'])} = {M}"
                ex.append(gen_formula_only(eq, "fr"))

    for t in TUPLES:
        ex.append(gen_formula_only(t, "fr"))
        if random.random() < 0.3:
            ex.append(gen_prose(t, "fr"))
    return ex


# Distractors : parenthèses de PROSE (mots dedans) — NON math.
FR_DISTRACTORS = [
    "Apporte tes affaires (cahier, stylo, règle).",
    "Réponds aux questions (a, b et c) de l'exercice.",
    "Les chapitres (1, 2 et 3) sont au programme.",
    "Relis la consigne (voir page 12) avant de commencer.",
    "Il a réussi (enfin presque) à finir l'exercice.",
    "Prends une feuille (grand format) pour le devoir.",
    "Le professeur (M. Durand) corrige les copies.",
    "On range le matériel (compas, équerre) dans la trousse.",
    "Coche la bonne réponse (parmi les quatre proposées).",
    "Note la date (lundi 3 mars) sur ton agenda.",
]
EN_DISTRACTORS = [
    "Bring your supplies (pen, ruler, notebook).",
    "Answer the questions (a, b and c) of the exercise.",
    "Read the instructions (see page 12) first.",
    "He almost (but not quite) solved it.",
    "The chapters (1, 2 and 3) are on the syllabus.",
    "Pick the right answer (among the four).",
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
