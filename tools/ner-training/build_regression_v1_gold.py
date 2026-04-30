"""
Génère le regression set "gold" pour évaluation NER — corpus curé manuellement,
représentatif de la doctrine actuelle (post-v6, fin avril 2026).

Pourquoi un nouveau gold :
  Avant, le gold était `extension_v3_fixtures.jsonl`, généré depuis
  `specs/test-fixtures/phase1-zone-detection.json` (commit initial pivot VSTO,
  17 avril). Ces fixtures n'avaient jamais bougé alors que la doctrine a
  évolué (phase 5 multi-spans, v4 distilmult, v5 quantificateurs étendus,
  v6 briques 0.5.x). Le critère F1 ≥ 0.99 sur un gold obsolète ne mesurait
  plus rien d'utile.

Ce nouveau gold :
  - ~70 cas, annotés à la main, alignés avec la doctrine **actuelle**
  - Couvre tous les axes : function defs, multi-spans, quantificateurs,
    implications, vecteurs, intervalles, ensembles, trous des logs,
    négatifs/distracteurs, multi-langues
  - Petit volume = cycle de bench rapide
  - Critère d'adoption : F1 ≥ 0.99 sur ce gold = "le modèle respecte la
    doctrine sur les cas qu'on tient à ne pas régresser"

Maintenance :
  À chaque évolution doctrine majeure (nouveau brief feat), ajouter 2-3
  fixtures représentatives ici. C'est le contrat de non-régression.

Sortie : data/ner-corpus/regression_v1_gold.jsonl
"""

import io
import json
import sys
from pathlib import Path

if hasattr(sys.stdout, "buffer"):
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

REPO = Path(__file__).resolve().parents[2]
DST = REPO / "data" / "ner-corpus" / "regression_v1_gold.jsonl"


# ============================================================================
# Cas curés — chaque tuple (text, list_of_math_fragments, lang).
# Les fragments math doivent apparaître exactement comme dans le texte.
# Une liste vide signifie "aucune zone MATH attendue" (négatif).
# ============================================================================

CASES = [
    # ------------------------------------------------------------
    # A. Function definitions classiques (équations f(x) = ...)
    # ------------------------------------------------------------
    ("On a f(x) = 2x + 1", ["f(x) = 2x + 1"], "fr"),
    ("Soit f(x) = 1/x", ["f(x) = 1/x"], "fr"),
    ("On pose g(x) = x^2 + 3x - 1", ["g(x) = x^2 + 3x - 1"], "fr"),
    ("La fonction est définie sur R par f(x) = (2x+1)/(x-3)",
     ["f(x) = (2x+1)/(x-3)"], "fr"),
    ("f(x,y) = x^2 + y^2", ["f(x,y) = x^2 + y^2"], "fr"),
    ("We have f(x) = 2x + 1", ["f(x) = 2x + 1"], "en"),
    ("Let g(x) = x^2 - 4", ["g(x) = x^2 - 4"], "en"),
    ("Sei f(x) = 2x + 1", ["f(x) = 2x + 1"], "de"),
    ("Sea f(x) = x^2 - 1", ["f(x) = x^2 - 1"], "es"),

    # ------------------------------------------------------------
    # B. Function definitions modernes (f : x -> expr, f : R -> R)
    # ------------------------------------------------------------
    ("Soit f : x -> x+1", ["f : x -> x+1"], "fr"),
    ("On définit f : x -> x^2", ["f : x -> x^2"], "fr"),
    ("Considérons g : R -> R, x -> sin(x)",
     ["g : R -> R, x -> sin(x)"], "fr"),
    ("h : N* -> R, n -> 1/n", ["h : N* -> R, n -> 1/n"], "fr"),

    # ------------------------------------------------------------
    # C. Multi-spans dans une même phrase (cas réaliste, plusieurs OMaths)
    # ------------------------------------------------------------
    ("f(x)=1/x et g(x)=2x", ["f(x)=1/x", "g(x)=2x"], "fr"),
    ("Soit f(x)=1/x, on a g(x)=2x", ["f(x)=1/x", "g(x)=2x"], "fr"),
    ("If a + b = c then a = c - b",
     ["a + b = c", "a = c - b"], "en"),
    ("The derivative of sin(x) is cos(x)",
     ["sin(x)", "cos(x)"], "en"),
    ("soit f(x) = 2x+1 et g(x) = 3x-1, alors f(g(x)) = 6x+1",
     ["f(x) = 2x+1", "g(x) = 3x-1", "f(g(x)) = 6x+1"], "fr"),

    # ------------------------------------------------------------
    # D. Quantificateurs étendus (forall/exists avec scope explicite)
    # ------------------------------------------------------------
    ("Pour tout x dans R, f(x) >= 0",
     ["Pour tout x dans R, f(x) >= 0"], "fr"),
    ("For all x in R, f(x) >= 0",
     ["For all x in R, f(x) >= 0"], "en"),
    ("forall x in R, x^2 >= 0", ["forall x in R, x^2 >= 0"], "fr"),
    ("Montrer que pour tout n >= 1, 2^n > n",
     ["pour tout n >= 1, 2^n > n"], "fr"),
    ("Il existe un unique x tel que f(x) = 0",
     ["f(x) = 0"], "fr"),
    ("∀ x ∈ R, x^2 ≥ 0", ["∀ x ∈ R, x^2 ≥ 0"], "fr"),

    # ------------------------------------------------------------
    # E. Implications / équivalences
    # ------------------------------------------------------------
    ("x > 0 => x^2 > 0", ["x > 0 => x^2 > 0"], "fr"),
    ("n pair <=> n = 2k", ["n pair <=> n = 2k"], "fr"),
    ("On a A => B", ["A => B"], "fr"),
    ("x = y <=> y = x", ["x = y <=> y = x"], "fr"),
    ("ab = 0 <=> a = 0 ou b = 0",
     ["ab = 0 <=> a = 0 ou b = 0"], "fr"),

    # ------------------------------------------------------------
    # F. Vecteurs + coordonnées
    # ------------------------------------------------------------
    ("V(1,2,3)", ["V(1,2,3)"], "fr"),
    ("Le vecteur AB a pour coordonnées (3, -1)",
     ["(3, -1)"], "fr"),
    ("point M(1,2)", ["point M(1,2)"], "fr"),
    ("u = (1,2,3)", ["u = (1,2,3)"], "fr"),

    # ------------------------------------------------------------
    # G. Intervalles + ensembles canoniques
    # ------------------------------------------------------------
    ("Soit l'intervalle [0,1]", ["[0,1]"], "fr"),
    ("x in [0,+inf[", ["x in [0,+inf["], "fr"),
    ("Posons ]a;b[.", ["]a;b["], "fr"),
    ("Soit x dans R*", ["x dans R*"], "fr"),
    ("forall x in R \\ {0}", ["forall x in R \\ {0}"], "fr"),
    ("[0,1] union [2,3]", ["[0,1] union [2,3]"], "fr"),

    # ------------------------------------------------------------
    # H. Trous des logs — longues expressions (bug 29-04)
    # ------------------------------------------------------------
    ("Somme k 1 n f(k) = 1/x^2 + sin(x)^2",
     ["Somme k 1 n f(k) = 1/x^2 + sin(x)^2"], "fr"),
    ("1/x^2 + tan^2(x) / sqrt(4+1)",
     ["1/x^2 + tan^2(x) / sqrt(4+1)"], "fr"),
    ("f(x) = x^2 + 2x + 1", ["f(x) = x^2 + 2x + 1"], "fr"),
    ("u_n = u_(n-1) + 1", ["u_n = u_(n-1) + 1"], "fr"),

    # ------------------------------------------------------------
    # I. Math basique (formules nues, isolées)
    # ------------------------------------------------------------
    ("3 + 5 = 8", ["3 + 5 = 8"], "fr"),
    ("a + b", ["a + b"], "fr"),
    ("(a + b + c) / (d + 1)", ["(a + b + c) / (d + 1)"], "fr"),
    ("alpha + beta = gamma", ["alpha + beta = gamma"], "fr"),
    ("On en déduit que x = 3", ["x = 3"], "fr"),
    ("Donc (a+b)^2 = a^2 + 2ab + b^2",
     ["(a+b)^2 = a^2 + 2ab + b^2"], "fr"),
    ("On a alors 1/2 + 1/3 = 5/6", ["1/2 + 1/3 = 5/6"], "fr"),
    ("On sait que sin(x)^2 + cos(x)^2 = 1",
     ["sin(x)^2 + cos(x)^2 = 1"], "fr"),
    ("Soit n un entier, on a n! = n*(n-1)!",
     ["n! = n*(n-1)!"], "fr"),
    ("D'après le théorème, lim x -> +inf f(x) = 0",
     ["lim x -> +inf f(x) = 0"], "fr"),
    ("Es gilt x^2 + y^2 = r^2", ["x^2 + y^2 = r^2"], "de"),
    ("Therefore x = (-b + sqrt(b^2 - 4ac)) / (2a)",
     ["x = (-b + sqrt(b^2 - 4ac)) / (2a)"], "en"),
    ("Note that (a+b)(a-b) = a^2 - b^2",
     ["(a+b)(a-b) = a^2 - b^2"], "en"),
    ("The area is pi*r^2", ["pi*r^2"], "en"),

    # ------------------------------------------------------------
    # J. Cas piège — math en anglais verbal, math décapité
    # ------------------------------------------------------------
    ("Compute the integral of x^2 from 0 to 1",
     ["integral of x^2 from 0 to 1"], "en"),
    ("= 3x + 1", ["= 3x + 1"], "fr"),

    # ------------------------------------------------------------
    # K. Négatifs — texte sans math (basiques)
    # ------------------------------------------------------------
    ("Bonjour tout le monde", [], "fr"),
    ("Le chat est sur le tapis", [], "fr"),
    ("Hello world", [], "en"),
    ("x", [], "fr"),
    ("2", [], "fr"),

    # ------------------------------------------------------------
    # L. Négatifs — distracteurs (mots ambigus en sens commun)
    # ------------------------------------------------------------
    ("L'intersection des deux rues est dangereuse.", [], "fr"),
    ("Le vecteur de la croissance, c'est l'innovation.", [], "fr"),
    ("Mon frère Sinane est content", [], "fr"),
    ("Plan F en cas d'urgence.", [], "fr"),
    ("Le V de la victoire est emblématique.", [], "fr"),
    ("La fonction publique recrute.", [], "fr"),
    ("L'implication politique est forte.", [], "fr"),

    # ------------------------------------------------------------
    # M. Négatifs — bullets/puces de listes (bug observé en prod)
    # ------------------------------------------------------------
    ("* Item 1", [], "fr"),
    ("* Premier exercice", [], "fr"),
    ("- Réviser le chapitre 3", [], "fr"),
    ("• Préparer le contrôle", [], "fr"),

    # ------------------------------------------------------------
    # N. Négatifs — n°/4° (pas math, Word AutoCorrect issue)
    # ------------------------------------------------------------
    ("n°5 sur la liste des élèves.", [], "fr"),
    ("C'est mon 4° anniversaire ce week-end.", [], "fr"),
]


def make_example(text: str, fragments: list, lang: str) -> dict:
    """Construit une entrée corpus à partir du texte et des fragments math
    attendus. Les offsets sont calculés via str.find — on assume que chaque
    fragment apparaît UNE fois dans le texte (à valider sur l'output)."""
    spans = []
    for frag in fragments:
        pos = text.find(frag)
        if pos < 0:
            raise ValueError(f"Fragment {frag!r} introuvable dans {text!r}")
        spans.append({"start": pos, "end": pos + len(frag), "label": "MATH"})
    return {"text": text, "spans": spans, "lang": lang}


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


def stats(examples: list[dict]) -> None:
    n = len(examples)
    n_with = sum(1 for e in examples if e["spans"])
    n_without = n - n_with
    by_lang = {}
    for e in examples:
        by_lang[e["lang"]] = by_lang.get(e["lang"], 0) + 1

    print(f"\nGold regression v1 — {n} cas")
    print(f"  positifs  : {n_with} ({100*n_with/n:.0f} %)")
    print(f"  négatifs  : {n_without} ({100*n_without/n:.0f} %)")
    print(f"  par lang  : {by_lang}")

    # Couverture par axe doctrine
    axes = [
        ("function defs (=)", lambda e: any("(x) =" in t or "(x) =" in t
                                            for t in [e["text"]])),
        ("function defs (->)", lambda e: ":" in e["text"] and "->" in e["text"]),
        ("multi-spans", lambda e: len(e["spans"]) >= 2),
        ("quantif étendus", lambda e: any(kw in e["text"]
                                          for kw in ["Pour tout", "For all",
                                                     "forall", "∀"])),
        ("implications", lambda e: any(kw in e["text"]
                                       for kw in ["=>", "<=>", "⇒", "⇔"])),
        ("vecteurs/coords", lambda e: any(kw in e["text"]
                                          for kw in ["V(", "vecteur", "point M"])),
        ("intervalles", lambda e: any(c in e["text"] for c in ["[0,1]", "]a;b[", "[0,+inf["])),
        ("ensembles canon", lambda e: any(s in e["text"] for s in ["R*", "R \\ {0}"])),
        ("multi-langues",  lambda e: e["lang"] in {"en", "de", "es"}),
        ("négatifs",       lambda e: not e["spans"]),
    ]
    print("\nCouverture axes doctrine :")
    for name, pred in axes:
        count = sum(1 for e in examples if pred(e))
        print(f"  {name:<22} {count}")


def main() -> None:
    examples = [make_example(text, frags, lang) for (text, frags, lang) in CASES]

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
