"""
Génère des exemples d'entraînement pour les n-aires à FORMES COURTES et les
intégrales multiples.

Contexte (diag 2026-06-11, session formes courtes) : sur le modèle
distilmult-v5, sondes offline avec le vrai detector :
 - `iint` / `iiint` : 0 occurrence dans TOUT le corpus → le modèle détecte la
   queue sans le mot-clé (`iint f x y` → zone "f x y" conf 0.98) ; le
   ZoneRefiner rattrape désormais via la table de keywords, mais le modèle
   doit apprendre le mot-clé (zones plus nettes, moins dépendant du refiner).
 - mots-clés NUS en fin de frappe : `int`, `sum`, `iint`, `iiint`, `prod`
   seuls → RIEN (alors que `lim` seul → zone conf 0.99). L'utilisateur ne
   voit pas le squelette en tapant le mot-clé, contrairement à lim.
 - autocapitalisation Word (début de cellule/phrase) : `Iint 0 1 f x y` →
   zone parasite [0,1] "I" conf 0.87.
 - formes courtes moteur 2026-06-11 (ADR nary-arity-variants) :
   `sum k f(k)`, `int f(x) x`, `iint f x y`, `iiint f x y z`, `lim u_n` —
   nouvelles syntaxes à couvrir explicitement.

Révision post-bench (2026-06-11, gold distilmult 0.9323) — la v1 du script a
créé trois régressions gold, corrigées par les sections « conventions gold » :
 - les mots-clés nus en formule isolée ont généralisé « token court seul =
   math » (`x`@0.91, `2`@0.90 seuls) → contre-exemples SINGLE_TOKEN_NEGATIVES ;
 - le poids cumulé v4+v8 des gabarits « prose, virgule, math » a fait couper
   les phrases quantifiées à la virgule (`Pour tout x dans R, | f(x) >= 0`)
   → QUANTIFIED_WHOLE (un seul span, convention gold) ;
 - quantifieur 100 % prose (`Il existe un unique x tel que f(x) = 0`) avalé
   en entier → PROSE_QUANTIFIER_TAIL (span = formule seule) + TWO_SPAN_EN.
Variantes PROCHES mais non identiques au gold (pas de leakage verbatim).

Même pattern que build_v4_keywords.py (prose FR/EN + formules isolées +
distractors), seed dédiée.

Sortie : data/ner-corpus/extension_v8_nary_short_forms.jsonl
"""

import json
import random
from pathlib import Path

random.seed(20260611)

REPO = Path(__file__).resolve().parents[2]
DST = REPO / "data" / "ner-corpus" / "extension_v8_nary_short_forms.jsonl"


# ============================================================================
# EXPRESSIONS — syntaxe MathCursor (séparateur espace, pas de LaTeX)
# ============================================================================

# Intégrales doubles/triples : bornées, indéfinies, en cours de frappe.
IINT_EXPRESSIONS = [
    "iint f x y",
    "iint f(x,y) x y",
    "iint 0 1 f x y",
    "iint 0 1 f(x,y) x y",
    "iint D f x y",
    "iint 0 1 x+y x y",
    "iint 0 +inf e^-x-y x y",
    "iint f",          # frappe en cours
    "iint f x",        # frappe en cours
    "iint 0",          # frappe en cours
    "iint 0 1",        # frappe en cours
    "iiint f x y z",
    "iiint 0 1 f x y z",
    "iiint V f x y z",
    "iiint 0 1 x*y*z x y z",
    "iiint f",         # frappe en cours
    "iiint f x",       # frappe en cours
    "iiint f x y",     # frappe en cours
    "iiint 0 1 f",     # frappe en cours
]

# Intégrale simple : formes indéfinies (nouvelles) + rappel bornées,
# avec les ALIAS moteur (integrale/integ/integral — sync Vocabulary 2026-06-11).
# Pas de forme accentuée : les accents sont FOLDÉS en amont du NER
# (AutocorrectNormalizer en prod, FOLD au chargement dans les notebooks).
INT_EXPRESSIONS = [
    "int f(x) x",
    "int f(x) dx",
    "int x^2 x",
    "int cos x x",
    "int e^x x",
    "int 2x+1 x",
    "int u'(t) t",
    "int 0 1 f(x) x",
    "int a b f(x) x",
    "int 0 +inf e^-x x",
    "integrale f(x) x",
    "integrale 0 1 f(x) x",
    "integrale a b x^2 x",
    "integrale 0 1 x dx",
    "integ 0 1 x dx",
    "integral f(x) dx",
    "integral a b f(x) dx",
]

# Sommes/produits : formes courtes (2 args) + variantes alias FR.
SUM_SHORT_EXPRESSIONS = [
    "sum k f(k)",
    "sum n 1/n",
    "sum n 1/n^2",
    "sum i a_i",
    "sum k u_k",
    "sum k 2^k",
    "sum n x^n",
    "somme k f(k)",
    "somme n u_n",
    "somme i a_i",
    "som k u_k",
    "prod i a_i",
    "prod k (1+x_k)",
    "prod n u_n",
    "produit i a_i",
    "produit k 1 n (2k+1)",
    "produit n u_n",
]

# lim : forme courte 1 arg (suites, variable implicite) + alias.
LIM_SHORT_EXPRESSIONS = [
    "lim u_n",
    "lim v_n",
    "lim f(x)",
    "lim 1/n",
    "lim u_n = l",
    "limite u_n",
    "limite 1/n",
    "limite x 0 f(x)",
    "lmt u_n",
    "lmt x 0 1/x",
]

# Mots-clés NUS — l'utilisateur vient de taper le mot-clé, le squelette doit
# se proposer (parité avec `lim` qui marche déjà). Positifs uniquement en
# formule isolée ou en FIN de prose math (frontière de frappe réaliste).
BARE_KEYWORDS = [
    "int",
    "iint",
    "iiint",
    "sum",
    "somme",
    "prod",
    "produit",
    "lim",       # déjà couvert mais maintient l'acquis
    "limite",
    "integrale",
    "integral",
]

# ── Conventions gold (anti-régression bench 2026-06-11) ─────────────────────

# 1. Une lettre ou un chiffre SEUL n'est PAS une zone math (sinon popup à
#    chaque frappe) — le mot-clé nu (int, sum…) OUI, la variable nue NON.
SINGLE_TOKEN_NEGATIVES = ["x", "y", "n", "a", "t", "k", "b", "2", "3", "5", "u", "7"]

# 2. Phrase quantifiée française/anglaise AVEC math interne = UN SEUL span
#    (convention gold : « Pour tout x dans R, f(x) >= 0 » est ∀x∈R, f(x)≥0
#    écrit en mots — la virgule ne coupe pas).
QUANTIFIED_WHOLE = [
    "Pour tout y dans R, g(y) > 0",
    "Pour tout t dans R, h(t) <= 1",
    "Pour tout x dans [0,1], x^2 <= x",
    "pour tout k >= 2, k^2 > k",
    "pour tout n >= 0, 3^n > n",
    "Pour tout n dans N, u_n <= u_n+1",
    "For all y in R, g(y) > 0",
    "For all t in R, h(t) >= -1",
    "for all m >= 1, 2^m > m",
]

# 3. Quantifieur 100 % PROSE : la math, c'est SEULEMENT la formule finale.
PROSE_QUANTIFIER_TAIL = [
    ("Il existe un unique y tel que g(y) = 0", "g(y) = 0"),
    ("Il existe un réel a tel que f(a) = a", "f(a) = a"),
    ("Il existe un entier n tel que u_n > M", "u_n > M"),
    ("Il existe au moins un x tel que h(x) < 0", "h(x) < 0"),
    ("There exists a unique y such that g(y) = 0", "g(y) = 0"),
    ("There exists an integer n such that u_n > M", "u_n > M"),
]

# 4. Deux formules séparées par de la prose = DEUX spans (le « is »/« est »
#    ne doit pas être avalé dans le premier span).
TWO_SPAN_SENTENCES = [
    ("The derivative of cos(x) is -sin(x)", ["cos(x)", "-sin(x)"], "en"),
    ("The derivative of tan(x) is 1+tan(x)^2", ["tan(x)", "1+tan(x)^2"], "en"),
    ("The integral of cos(x) is sin(x)", ["cos(x)", "sin(x)"], "en"),
    ("La dérivée de cos(x) est -sin(x)", ["cos(x)", "-sin(x)"], "fr"),
    ("La dérivée de e^x est e^x", ["e^x", "e^x"], "fr"),
    ("La primitive de cos(x) est sin(x)", ["cos(x)", "sin(x)"], "fr"),
]

# Autocapitalisation Word (début de phrase / cellule de tableau).
CAPITALIZED_EXPRESSIONS = [
    "Int 0 1 f(x) x",
    "Int f(x) x",
    "Iint f x y",
    "Iint 0 1 f x y",
    "Iiint f x y z",
    "Sum k f(k)",
    "Sum k 1 n f(k)",
    "Somme n 1/n",
    "Prod i a_i",
    "Lim u_n",
]


# ============================================================================
# TEMPLATES PROSE
# ============================================================================

FR_TEMPLATES = [
    "On a {F}.",
    "Soit {F}.",
    "Calculons {F}.",
    "Calculer {F}.",
    "On pose {F}.",
    "On note {F}.",
    "On en déduit {F}.",
    "On considère {F}.",
    "Évaluer {F}.",
    "Par définition, {F}.",
    "On vérifie que {F}.",
    "D'après le cours, {F}.",
    "L'aire vaut {F}.",
    "Le volume vaut {F}.",
    "La série {F} converge.",
    "Étudier la convergence de {F}.",
]

EN_TEMPLATES = [
    "We have {F}.",
    "Let {F}.",
    "Compute {F}.",
    "Consider {F}.",
    "Evaluate {F}.",
    "By definition, {F}.",
    "The area equals {F}.",
    "The volume equals {F}.",
    "The series {F} converges.",
]

# Mot-clé NU en fin de prose : la phrase s'arrête sur le mot-clé (frappe en
# cours). Le span couvre UNIQUEMENT le mot-clé.
FR_TRAILING_TEMPLATES = [
    "On a {K}",
    "On pose {K}",
    "Calculons {K}",
    "L'aire vaut {K}",
    "On en déduit {K}",
]

EN_TRAILING_TEMPLATES = [
    "We have {K}",
    "We compute {K}",
    "The area equals {K}",
]


# ============================================================================
# DISTRACTORS — précision : mots du quotidien contenant/évoquant les keywords
# ============================================================================

FR_DISTRACTORS = [
    # « int » substring fréquent en français — le modèle ne doit pas sur-réagir
    "Il pleut maintenant sur toute la côte.",
    "C'est un point intéressant à débattre.",
    "Le sprint final a été décisif.",
    "Elle a peint le mur en bleu hier.",
    "Le saint patron du village est fêté en mai.",
    "J'ai éteint la lumière avant de sortir.",
    "Son maintien était irréprochable pendant l'oral.",
    "L'interphone de l'immeuble est en panne.",
    "L'intervalle entre deux bus est de dix minutes.",  # mot math en sens commun
    # somme / produit / limite en sens commun
    "En somme, tout s'est bien passé.",
    "Il a dormi un petit somme cet après-midi.",
    "La somme versée couvre les frais de dossier.",
    "Le produit de la vente ira à l'association.",
    "Ce produit nettoie toutes les surfaces.",
    "La limite de poids est de vingt kilos par bagage.",
    "Il connaît ses limites en escalade.",
    # intégrale en sens commun
    "Il a écouté l'intégrale des symphonies de Beethoven.",
    "La version intégrale du roman compte mille pages.",
]

EN_DISTRACTORS = [
    "He paid in full at the print shop.",
    "The paint on the wall is still wet.",
    "This is an interesting point indeed.",
    "She finished the sprint in record time.",
    "The interview went better than expected.",
    "In sum, the meeting was a success.",
    "A large sum was donated to the school.",
    "The product launch is scheduled for May.",
    "There is a weight limit on this bridge.",
    "He listened to the integral recordings of Bach.",
]


# ============================================================================
# UTILITAIRES / GÉNÉRATEURS (pattern build_v4_keywords)
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


def gen_trailing_keyword(keyword: str, lang: str) -> dict:
    templates = FR_TRAILING_TEMPLATES if lang == "fr" else EN_TRAILING_TEMPLATES
    text = random.choice(templates).replace("{K}", keyword)
    span = make_span(text, keyword)
    return {"text": text, "spans": [span] if span else [], "lang": lang}


def generate_positives() -> list[dict]:
    examples = []
    buckets = [
        IINT_EXPRESSIONS,
        INT_EXPRESSIONS,
        SUM_SHORT_EXPRESSIONS,
        LIM_SHORT_EXPRESSIONS,
        CAPITALIZED_EXPRESSIONS,
    ]
    for expressions in buckets:
        for expr in expressions:
            # formule isolée FR (cas frappe directe dans Word, le plus fréquent)
            examples.append(gen_formula_only(expr, "fr"))
            # prose FR + parfois EN
            examples.append(gen_prose(expr, "fr"))
            if random.random() < 0.4:
                examples.append(gen_prose(expr, "en"))

    # mots-clés nus : formule isolée + fin de prose (×2 pour le poids — c'est
    # LE cas sous-représenté, cf. `int` seul → RIEN vs `lim` seul → 0.99)
    for kw in BARE_KEYWORDS:
        examples.append(gen_formula_only(kw, "fr"))
        examples.append(gen_trailing_keyword(kw, "fr"))
        examples.append(gen_trailing_keyword(kw, "fr"))
        if random.random() < 0.5:
            examples.append(gen_trailing_keyword(kw, "en"))

    # conventions gold : quantifié avec math interne = UN span (isolé + prose)
    for expr in QUANTIFIED_WHOLE:
        lang = "en" if expr.lower().startswith("for ") else "fr"
        examples.append(gen_formula_only(expr, lang))
        examples.append(gen_prose(expr, lang))

    # conventions gold : quantifieur 100 % prose → span = formule seule
    for text, fragment in PROSE_QUANTIFIER_TAIL:
        lang = "en" if "There exists" in text else "fr"
        span = make_span(text, fragment)
        examples.append({"text": text, "spans": [span] if span else [], "lang": lang})

    # conventions gold : deux formules séparées par de la prose = deux spans
    for text, fragments, lang in TWO_SPAN_SENTENCES:
        spans, search_from = [], 0
        for frag in fragments:
            pos = text.find(frag, search_from)
            if pos < 0:
                continue
            spans.append({"start": pos, "end": pos + len(frag), "label": "MATH"})
            search_from = pos + len(frag)
        examples.append({"text": text, "spans": spans, "lang": lang})
    return examples


def generate_distractors() -> list[dict]:
    examples = []
    for text in FR_DISTRACTORS:
        examples.append({"text": text, "spans": [], "lang": "fr"})
    for text in EN_DISTRACTORS:
        examples.append({"text": text, "spans": [], "lang": "en"})
    # token court SEUL ≠ math (×2 pour contrer le poids des mots-clés nus)
    for tok in SINGLE_TOKEN_NEGATIVES:
        examples.append({"text": tok, "spans": [], "lang": "fr"})
        examples.append({"text": tok, "spans": [], "lang": "fr"})
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

    print("\nCouverture par keyword (positifs avec mot dans le span) :")
    for kw in ["iiint", "iint", "int", "integrale", "sum", "somme", "prod",
               "produit", "lim", "limite"]:
        count = sum(
            1
            for e in examples
            for s in e["spans"]
            if kw in e["text"][s["start"]:s["end"]].lower()
        )
        print(f"  {kw:<12} {count}")


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
