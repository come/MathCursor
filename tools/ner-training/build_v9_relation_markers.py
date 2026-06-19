"""
Génère des exemples d'entraînement pour les MARQUEURS DE RELATION en tête de
ligne : approx / environ / env / ≈ (et rappel de quelques symboles).

Contexte (2026-06-19, ADR Fix-environ-env-line-start-approx-marker) :
 - `approx` / `environ` / `env` ont **0 occurrence** dans TOUT le corpus
   (sondes offline : `grep` corpus → rien). Le détecteur distilmult-v6 les
   reconnaît quand même par généralisation (`environ f(x)` → zone [0,12]
   conf élevée), mais on veut des zones NETTES, indépendantes de la chance —
   même logique que la v8 pour `iint`/`iiint` (0 occurrence → ajout explicite).
 - Côté moteur+adapter : en TÊTE DE LIGNE ces mots sont des marqueurs de
   relation (`RelationMarkers`), équivalents à `=` : `environ f(x)` → `≈ f(x)`
   (le ≈ est mis en préfixe, le reste est analysé). Le NER doit donc voir la
   LIGNE ENTIÈRE comme UNE zone MATH (marqueur inclus), comme `= 2x` /
   `approx 3,14`.
 - Autocapitalisation Word (début de ligne/cellule) : `env`→`Env`,
   `environ`→`Environ`, `approx`→`Approx`. Le fix a rendu la reconnaissance
   insensible à la casse → le NER doit aussi voir les formes capitalisées.

ATTENTION — `environ`/`env` sont aussi de la PROSE courante :
 - « environ 50 personnes » (≈ « about »), « l'environnement », « l'enveloppe »,
   « env » comme dossier en anglais. Ces emplois ne sont PAS des zones math →
   distractors (spans=[]). La distinction tient au contexte (opérandes math vs
   nom commun) : exactement ce qu'on veut apprendre au modèle.

Mot-marqueur SEUL (`environ` tout seul) volontairement ABSENT des positifs :
en prod c'est « marqueur seul → on attend la suite » (pas de popup), et le mot
nu est de la prose fréquente. On ne veut pas que le modèle flague `environ` nu.

Même pattern que build_v8 (prose FR/EN + formules isolées + distractors), seed
dédiée.

Sortie : data/ner-corpus/extension_v9_relation_markers.jsonl
"""

import json
import random
import sys
from pathlib import Path

# Console Windows = cp1252 par défaut → les stats impriment « ≈ ». Forcer utf-8.
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

random.seed(20260619)

REPO = Path(__file__).resolve().parents[2]
DST = REPO / "data" / "ner-corpus" / "extension_v9_relation_markers.jsonl"


# ============================================================================
# MARQUEURS + QUEUES — syntaxe MathCursor (séparateur espace, pas de LaTeX)
# ============================================================================

# Mots-marqueurs de relation ≈ (frontière de mot exigée par l'adapter).
MARKER_WORDS = ["approx", "environ", "env"]

# Queue = expression math collée après le marqueur. La zone = marqueur + queue.
TAILS = [
    "f(x)", "g(x)", "3,14", "0,5", "x", "2x+1", "pi", "x^2", "1/n",
    "1/2", "n", "u_n", "5", "10^3", "x-1", "2pi", "sqrt 2", "e^x",
    "a", "f(x)+1", "1,5", "0", "100",
]

# Symbole ≈ direct (0 occurrence aussi) — rappel.
SYMBOL_TAILS = ["≈ 0,5", "≈ f(x)", "≈ 3,14", "≈ pi", "≈ x", "≈ 1/2"]

# Relation au MILIEU (infixe) : « a environ b » = a ≈ b, la zone = tout.
INFIX_EXPRESSIONS = [
    "a environ b", "x environ 3", "f(x) environ 3", "u_n environ 0",
    "pi environ 3,14", "x approx y", "f(x) approx g(x)", "n approx 100",
    "1/n approx 0", "x ≈ y", "f(x) ≈ 3",
]


# ============================================================================
# TEMPLATES PROSE (la zone = formule complète marqueur inclus)
# ============================================================================

FR_TEMPLATES = [
    "On a {F}.",
    "On obtient {F}.",
    "Donc {F}.",
    "On en déduit {F}.",
    "Le résultat est {F}.",
    "Numériquement, {F}.",
    "Ainsi {F}.",
]

EN_TEMPLATES = [
    "We get {F}.",
    "So {F}.",
    "Numerically, {F}.",
    "Hence {F}.",
    "The result is {F}.",
]


# ============================================================================
# DISTRACTORS — « environ »/« env »/« approx » en PROSE (NON math)
# ============================================================================

FR_DISTRACTORS = [
    "Il y avait environ cinquante personnes dans la salle.",
    "Nous arriverons dans environ une heure.",
    "Le trajet dure environ vingt minutes.",
    "Il reste environ la moitié du gâteau.",
    "Elle a dépensé environ deux cents euros.",
    "L'environnement de travail est très agréable.",
    "Il faut protéger l'environnement.",
    "Range le courrier dans l'enveloppe.",
    "Une enveloppe timbrée est jointe au dossier.",
    "Il a une grande envie de réussir son examen.",
    "Les environs du village sont magnifiques.",
    "Environ un tiers des élèves étaient absents.",
]

EN_DISTRACTORS = [
    "There were about fifty people in the room.",
    "Save the config in the env folder.",
    "The development environment is ready.",
    "The approximate cost is still unknown.",
    "We will arrive in approximately one hour.",
    "He set the env variable before running it.",
    "The surroundings of the lake are beautiful.",
]


# ============================================================================
# UTILITAIRES / GÉNÉRATEURS (pattern build_v8)
# ============================================================================

def make_span(text: str, fragment: str) -> dict | None:
    pos = text.find(fragment)
    if pos < 0:
        return None
    return {"start": pos, "end": pos + len(fragment), "label": "MATH"}


def gen_formula_only(expr: str, lang: str) -> dict:
    return {
        "text": expr,
        "spans": [{"start": 0, "end": len(expr), "label": "MATH"}],
        "lang": lang,
    }


def gen_prose(expr: str, lang: str) -> dict:
    templates = FR_TEMPLATES if lang == "fr" else EN_TEMPLATES
    text = random.choice(templates).replace("{F}", expr)
    span = make_span(text, expr)
    return {"text": text, "spans": [span] if span else [], "lang": lang}


def cap_first(s: str) -> str:
    return s[:1].upper() + s[1:] if s else s


def generate_positives() -> list[dict]:
    examples = []

    # Marqueur-mot + queue, en TÊTE de ligne (cas frappe directe Word).
    for w in MARKER_WORDS:
        for tail in TAILS:
            expr = f"{w} {tail}"
            examples.append(gen_formula_only(expr, "fr"))
            # autocap Word (début de ligne) : « Env … » / « Environ … »
            examples.append(gen_formula_only(cap_first(expr), "fr"))
            # parfois en prose (zone = formule complète, marqueur inclus)
            if random.random() < 0.3:
                examples.append(gen_prose(expr, "fr"))

    # Symbole ≈ direct.
    for expr in SYMBOL_TAILS:
        examples.append(gen_formula_only(expr, "fr"))
        if random.random() < 0.3:
            examples.append(gen_prose(expr, "fr"))

    # Relation infixe (a ≈ b) : zone = tout, isolé + prose + autocap.
    for expr in INFIX_EXPRESSIONS:
        examples.append(gen_formula_only(expr, "fr"))
        examples.append(gen_formula_only(cap_first(expr), "fr"))
        if random.random() < 0.4:
            examples.append(gen_prose(expr, "fr"))
        if random.random() < 0.3:
            examples.append(gen_prose(expr, "en"))

    return examples


def generate_distractors() -> list[dict]:
    examples = []
    for text in FR_DISTRACTORS:
        examples.append({"text": text, "spans": [], "lang": "fr"})
        # autocap n'change rien (déjà capitalisés) ; variante bas-de-casse
        # pour « environ »/« env » en milieu de phrase déjà couverte.
    for text in EN_DISTRACTORS:
        examples.append({"text": text, "spans": [], "lang": "en"})
    return examples


# ============================================================================
# VALIDATION / STATS
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
                print(f"BAD offsets line {i+1}: {span} in {ex['text']!r}")
                errors += 1
    return errors


def stats(examples: list[dict]) -> None:
    n = len(examples)
    n_with = sum(1 for e in examples if e["spans"])
    by_lang = {}
    for e in examples:
        by_lang[e["lang"]] = by_lang.get(e["lang"], 0) + 1

    print(f"\nTotal       : {n} lignes")
    print(f"  positifs  : {n_with}")
    print(f"  spans=[]  : {n - n_with}")
    print(f"  par lang  : {by_lang}")

    print("\nCouverture par marqueur (span contenant le marqueur en TOKEN entier) :")
    for kw in ["approx", "environ", "env", "≈"]:
        count = sum(
            1
            for e in examples
            for s in e["spans"]
            if kw in e["text"][s["start"]:s["end"]].lower().split()
        )
        print(f"  {kw:<10} {count}")


# ============================================================================
# MAIN
# ============================================================================

def main() -> None:
    examples = generate_positives() + generate_distractors()
    random.shuffle(examples)

    DST.parent.mkdir(parents=True, exist_ok=True)
    with DST.open("w", encoding="utf-8") as f:
        for ex in examples:
            ex["spans"] = [s for s in ex["spans"] if s is not None]
            f.write(json.dumps(ex, ensure_ascii=False) + "\n")

    errors = validate(examples)
    stats(examples)
    print(f"\nErreurs offsets : {errors}")
    print(f"Écrit : {DST.relative_to(REPO)}")


if __name__ == "__main__":
    main()
