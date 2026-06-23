"""
Génère des exemples NER pour les MOTS-CLÉS math EN TÊTE DE LIGNE sous-exploités
(analyse de couverture 2026-06-23 : tous à 0 ou quasi-0 dans le corpus, et
surtout SANS distracteurs homonymes → risque de faux positifs au retrain).

Familles (syntaxe MathCursor, séparateur espace, PAS de LaTeX) :
 - Décorations/préfixes : bar, conj, abs, module, hat, racine, rac, floor,
   ceil, plafond, norme, norm, det, dim, nabla, partial.
 - Lycée : pgcd, gcd, ppcm, parmi (binôme), pourtout, ilexiste (formes FR
   pleines — `forall`/`exists` ASCII sont déjà bien couverts).
 - Marqueurs relation : rond (composition), congru (… mod n), plusmoins / ±.

La zone math = le mot-clé + son opérande (le mot-clé est un préfixe/marqueur,
comme `environ`/`approx` en v9). Isolé + autocap Word + un peu de prose, FR/EN.

DISTRACTORS (spans=[]) — CRITIQUES, ces mots ont un sens commun fort :
 bar (comptoir), module (module de cours), conj (conjugaison), racine (du mot/
 d'arbre), angle (de la pièce), norme (de sécurité), floor (étage), parmi
 (préposition), rond (rond-point), dim (dimanche), hat (chapeau / « that »).

Sortie : data/ner-corpus/extension_v11_keywords.jsonl
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
DST = REPO / "data" / "ner-corpus" / "extension_v11_keywords.jsonl"


def make_span(text, fragment):
    pos = text.find(fragment)
    if pos < 0:
        return None
    return {"start": pos, "end": pos + len(fragment), "label": "MATH"}


def cap_first(s):
    return s[:1].upper() + s[1:] if s else s


def formula(expr, lang="fr"):
    return {"text": expr, "spans": [{"start": 0, "end": len(expr), "label": "MATH"}], "lang": lang}


FR_TEMPLATES = ["On a {F}.", "Donc {F}.", "On calcule {F}.", "Soit {F}.",
                "On obtient {F}.", "Ainsi {F}."]
EN_TEMPLATES = ["We have {F}.", "So {F}.", "We compute {F}.", "Let {F}."]


def prose(expr, lang="fr"):
    tpl = random.choice(FR_TEMPLATES if lang == "fr" else EN_TEMPLATES)
    text = tpl.replace("{F}", expr)
    return {"text": text, "spans": [make_span(text, expr)], "lang": lang}


# (forme tapée du mot-clé, opérandes math collées) — la zone = forme + opérande.
POSITIVES = [
    ("bar", ["z", "z+1", "u", "w", "z_1"]),
    ("conj", ["z", "w", "z+1"]),
    ("conj de", ["z", "w"]),
    ("abs", ["x", "x-1", "z", "-3", "2x"]),
    ("module de", ["z", "z-1", "w"]),
    ("hat", ["a", "u", "x", "ABC"]),
    ("racine de", ["x", "x+1", "2", "delta"]),
    ("racine", ["x", "x+1", "delta"]),
    ("rac", ["2", "3", "x", "5"]),
    ("floor", ["x", "x/2", "3,7", "n/2"]),
    ("ceil", ["x", "x/2", "n/2"]),
    ("plafond de", ["x", "n/2"]),
    ("norme de", ["v", "u", "AB"]),
    ("norme", ["v", "u"]),
    ("norm", ["v", "u", "AB"]),
    ("det", ["A", "M", "B", "(A B ; C D)"]),
    ("dim", ["E", "F", "V", "Ker f"]),
    ("nabla", ["f", "g"]),
    ("partial", ["f", "f / partial x"]),
    ("pgcd", ["(12,18)", "(a,b)", "12 18"]),
    ("gcd", ["(12,18)", "(a,b)"]),
    ("ppcm", ["(4,6)", "(a,b)", "4 6"]),
    ("parmi", ["3 parmi 10", "k parmi n", "2 parmi 5"]),  # « k parmi n » : forme entière
    ("pourtout", ["x", "n", "x dans R", "x > 0", "epsilon > 0"]),
    ("ilexiste", ["x", "n", "x dans R", "y tel que"]),
    ("rond", ["f rond g", "g rond f"]),     # composition : zone = tout
    ("congru", ["a congru b mod n", "x congru 0 mod 2", "7 congru 1 mod 3"]),
    ("plusmoins", ["x plusmoins 1", "a plusmoins b", "-b plusmoins racine delta"]),
    ("±", ["± 2", "x ± 1", "a ± b"]),
]

# Familles où l'opérande est DÉJÀ une expression complète (rond/congru/parmi/±)
WHOLE = {"parmi", "rond", "congru", "±"}


def generate_positives():
    ex = []
    for kw, tails in POSITIVES:
        for t in tails:
            expr = t if (kw in WHOLE or " " in kw and kw.split()[0] in WHOLE) else f"{kw} {t}"
            if kw in WHOLE:
                expr = t  # t est déjà « k parmi n », « f rond g », « x ± 1 »…
            else:
                expr = f"{kw} {t}"
            ex.append(formula(expr, "fr"))
            ex.append(formula(cap_first(expr), "fr"))   # autocap Word (début de ligne)
            if random.random() < 0.3:
                ex.append(prose(expr, "fr"))
            if random.random() < 0.15:
                ex.append(prose(expr, "en"))
    return ex


# Distracteurs homonymes — sens commun, spans=[].
FR_DISTRACTORS = [
    "On se retrouve au bar à dix-huit heures.",
    "Le bar de la gare est fermé le dimanche.",
    "Il commande un jus au bar du coin.",
    "Le module 3 du cours porte sur les fonctions.",
    "Il a validé son module d'option en physique.",
    "Inscris-toi au module avant la fin de la semaine.",
    "Révise la conjugaison du verbe avoir.",
    "La table de conjugaison est au dos du cahier.",
    "La racine du problème est mal posée.",
    "Les racines de l'arbre soulèvent le trottoir.",
    "Cherche la racine latine de ce mot.",
    "L'angle de la pièce était plongé dans le noir.",
    "Vu sous cet angle, l'histoire change.",
    "Le respect des normes de sécurité est obligatoire.",
    "Cette norme impose un format de fichier précis.",
    "On s'est assis en rond autour du feu.",
    "Le rond-point est en travaux depuis lundi.",
    "On se voit dim après-midi pour réviser.",
    "Choisis un livre parmi ceux de la liste.",
    "Parmi les élèves, trois étaient absents aujourd'hui.",
    "Il range les copies parmi ses affaires.",
    "Elle porte un joli chapeau de paille.",
]
EN_DISTRACTORS = [
    "We met at the bar after the lecture.",
    "She lives on the third floor of the building.",
    "The ground floor was closed for repairs.",
    "Dim the lights before the movie starts.",
    "He wore a red hat to the party.",
    "Put on your hat, it is cold outside.",
    "From a different angle, the problem looks easy.",
    "Pick one among the four options below.",
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
