"""
Génère exemples synthétiques pour les quantificateurs ∀/∃ (forall/exists) avec
scope explicite, et pour les lettres-quantificateurs isolées V/E (raccourcis
clavier AZERTY pour ∀/∃).

Cause : le moteur lattice (cf. ADR
docs/dev/decisions/2026-04-28-Feat-forall-scope-source-mutation.md) gère
désormais ces patterns mais le NER ne les détecte pas comme zone MATH —
"V x R" et "forall x R" renvoient ∅ en production.

Cf. brief docs/dev/briefs/2026-04-28-ner-retraining-v5-quant-letters.md.

Sortie : data/ner-corpus/extension_v5_quant_letters.jsonl
"""

import io
import json
import random
import sys
from pathlib import Path

# Force UTF-8 sur stdout pour éviter UnicodeEncodeError sur les caractères
# ∀/∃ etc. quand le terminal Windows est en cp1252.
if hasattr(sys.stdout, "buffer"):
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

random.seed(20260428)

REPO = Path(__file__).resolve().parents[2]
DST = REPO / "data" / "ner-corpus" / "extension_v5_quant_letters.jsonl"


# ============================================================================
# EXPRESSIONS — quantificateurs forall/exists avec scope explicite
# Cf. brief §2.2 et §4.2. Variations : avec/sans `in`/`dans`, set simple ou
# composé (R, R+, [0,1]), suivi optionnel de relation (`, x ≥ 0`).
# ============================================================================

FORALL_EXPRESSIONS = [
    "forall x R",
    "forall x in R",
    "forall x dans R",
    "forall x appartient R",
    "forall y R",
    "forall y N",
    "forall y in N",
    "forall y dans N",
    "forall n Z",
    "forall n in Z",
    "forall n N",
    "forall n N*",
    "forall n in N*",
    "forall epsilon > 0",
    "forall epsilon in R+",
    "forall x R+",
    "forall x R*",
    "forall x in R+",
    "forall x in R*",
    "forall x [0,1]",
    "forall x [0;1]",
    "forall x [0,1[",
    "forall x [0;1[",
    "forall x ]0,1[",
    "forall x ]0;1[",
    "forall x ]0,1]",
    "forall x ]0;1]",
    "forall x [0,+inf[",
    "forall x [0;+inf[",
    "forall x ]-inf,0]",
    "forall x in [0,1]",
    "forall x in [a,b]",
    "forall x in [0;1[",
    "forall x dans [0,1]",
    "forall x dans [0;1[",
    "forall x [0,1] U [2,3]",
    "forall x [0,1[ U [2,3]",
    "forall x [0;1] U [2;3]",
    "forall x [0,1] inter [2,3]",
    "forall (x,y) R^2",
    "forall x R, x^2 ≥ 0",
    "forall x R, x ≥ 0",
    "forall x in R, x + 0 = x",
    "forall x R+, sqrt(x) ≥ 0",
    "forall x R, |x| ≥ 0",
    "forall n N, n+1 > n",
    "forall n N*, 1/n > 0",
    "forall x R, exp(x) > 0",
    "forall x dans R, x = x",
    "forall x in [0,1], x^2 ≤ x",
    "forall n N, u_n ≤ M",
    "∀ x ∈ R",
    "∀ x ∈ N",
    "∀ y ∈ Z",
    "∀ n ∈ N*",
    "∀ x ∈ R, x^2 ≥ 0",
    "∀ n ∈ Z, n+1 ∈ Z",
]

EXISTS_EXPRESSIONS = [
    "exists y N",
    "exists y in N",
    "exists y dans N",
    "exists y appartient N",
    "exists x R",
    "exists x in R",
    "exists x dans R",
    "exists n N",
    "exists n N*",
    "exists n in N*",
    "exists x R+",
    "exists x in R+",
    "exists x in [0,1]",
    "exists x [0,1]",
    "exists x [0;1]",
    "exists x [0,1[",
    "exists x [0;1[",
    "exists x ]0,1[",
    "exists x [0,+inf[",
    "exists x ]-inf,0]",
    "exists x [0,1] U [2,3]",
    "exists x [0,1[ U [2,3]",
    "exists ! x R",
    "exists ! x in R",
    "exists ! x [0,1]",
    "exists y N tel que y > 5",
    "exists y in N tel que y > 0",
    "exists x R, f(x) = 0",
    "exists x R+, x^2 = 2",
    "exists n N, u_n > M",
    "exists y in R, x + y = 0",
    "exists n N tel que 2^n > n^2",
    "∃ y ∈ N",
    "∃ x ∈ R",
    "∃ n ∈ N*",
    "∃ x ∈ R, f(x) = 0",
    "∃ ! x ∈ R",
    "∃ y ∈ N tel que y > 5",
]

# Lettres-quantificateurs AZERTY isolées : V (raccourci forall), E (exists).
# Suivies d'un pattern math var+set, doivent être MATH avec V/E inclus.
V_EXPRESSIONS = [
    "V x R",
    "V x in R",
    "V x dans R",
    "V x N",
    "V x in N",
    "V y R",
    "V y N",
    "V n N",
    "V n in N",
    "V epsilon > 0",
    "V x R+",
    "V x R*",
    "V x in R+",
    "V x [0,1]",
    "V x [0;1]",
    "V x [0,1[",
    "V x [0;1[",
    "V x ]0,1[",
    "V x ]0;1[",
    "V x ]0,1]",
    "V x ]0;1]",
    "V x [a,b]",
    "V x [a;b]",
    "V x [0,+inf[",
    "V x [0;+inf[",
    "V x [0;+∞[",
    "V x ]-inf,0]",
    "V x ]-∞;0]",
    "V x [0,1[ U [2,3]",
    "V x [0;1[ U [2;3]",
    "V x [0,1] inter [2,3]",
    "V x dans [0,1]",
    "V x dans [0;1[",
    "V x R, x ≥ 0",
    "V x R, x^2 ≥ 0",
    "V x R+, sqrt(x) ≥ 0",
    "V n N, n+1 > n",
    "V n N*, 1/n > 0",
    "V x in R, |x| ≥ 0",
    "V x [0,1], x^2 ≤ x",
    "V x [0;1], 0 ≤ x ≤ 1",
]

E_EXPRESSIONS = [
    "E y N",
    "E y in N",
    "E y dans N",
    "E x R",
    "E x in R",
    "E n N",
    "E n N*",
    "E x R+",
    "E ! x R",
    "E x [0,1]",
    "E x [0;1]",
    "E x [0,1[",
    "E x [0;1[",
    "E x ]0,1[",
    "E x [0,+inf[",
    "E y N tel que y > 5",
    "E y in N tel que y > 0",
    "E x R, f(x) = 0",
    "E n N, u_n > M",
    "E x R+, x^2 = 2",
    "E x [0,1], f(x) = 0",
    "E x [0;1[, x^2 = 1/2",
]

# V/E utilisés en relation seule (V > 0, V = 5, E ≥ 0...) — V/E doit être MATH
# avec la relation. Brief §1 cas observé : "Soit V > 0" → V > 0 comme MATH.
V_RELATION_EXPRESSIONS = [
    "V > 0",
    "V ≥ 0",
    "V > x",
    "V = 5",
    "V < 10",
    "V ≤ M",
    "V ≠ 0",
]

E_RELATION_EXPRESSIONS = [
    "E > 0",
    "E ≥ 0",
    "E ≠ 0",
]


KEYWORD_BUCKETS = [
    ("forall", FORALL_EXPRESSIONS),
    ("exists", EXISTS_EXPRESSIONS),
    ("V", V_EXPRESSIONS),
    ("E", E_EXPRESSIONS),
]


# Intervalles français autonomes (standalone) — les élèves les tapent
# directement comme zone math, avec ou sans union/intersection.
# Variations : virgule / point-virgule, crochets ouverts/fermés, infini.
INTERVAL_EXPRESSIONS = [
    "[0,1]",
    "[0;1]",
    "[0,1[",
    "[0;1[",
    "]0,1[",
    "]0;1[",
    "]0,1]",
    "]0;1]",
    "[a,b]",
    "[a;b]",
    "[a,b[",
    "[a;b[",
    "]a,b[",
    "]a;b[",
    "[-1,1]",
    "[-1;1]",
    "[-2,2]",
    "[0,+inf[",
    "[0;+inf[",
    "[0;+∞[",
    "]-inf,0]",
    "]-∞;0]",
    "]-inf,+inf[",
    "]-∞;+∞[",
    # Unions / intersections
    "[0,1] U [2,3]",
    "[0;1] U [2;3]",
    "[0,1[ U ]2,3]",
    "[0;1[ U ]2;3]",
    "]-inf,0] U [1,+inf[",
    "]-∞;0] U [1;+∞[",
    "[0,1] inter [0.5,2]",
    "[0;1] inter [0.5;2]",
    "[a,b] U [c,d]",
    "[a;b] U [c;d]",
    "A U B",
    "A inter B",
    "A U B inter C",
    "(A U B) inter C",
]


# ============================================================================
# TEMPLATES PROSE
# ============================================================================

FR_TEMPLATES = [
    "On a {F}.",
    "Soit {F}.",
    "On note {F}.",
    "On suppose {F}.",
    "On en déduit {F}.",
    "Démontrer {F}.",
    "Démontrer que {F}.",
    "Montrer {F}.",
    "Montrer que {F}.",
    "Théorème : {F}.",
    "Lemme : {F}.",
    "Proposition : {F}.",
    "Corollaire : {F}.",
    "Considérons {F}.",
    "On considère {F}.",
    "D'après le cours, {F}.",
    "Il est clair que {F}.",
    "On vérifie {F}.",
    "Par définition, {F}.",
    "Rappelons que {F}.",
    "Posons {F}.",
    "On pose {F}.",
]

EN_TEMPLATES = [
    "We have {F}.",
    "Let {F}.",
    "We note {F}.",
    "Note that {F}.",
    "Theorem: {F}.",
    "Lemma: {F}.",
    "Proposition: {F}.",
    "Show that {F}.",
    "Prove that {F}.",
    "We assume {F}.",
    "Consider {F}.",
    "By definition, {F}.",
    "Suppose {F}.",
    "Recall that {F}.",
]

# Templates dédiés pour V/E en relation seule (V > 0, E ≥ 0...) — point ou
# virgule en fin pour fermer proprement la zone math.
FR_V_E_RELATION_TEMPLATES = [
    "Soit {F} un réel.",
    "Soit {F} un nombre.",
    "Soit {F}.",
    "On pose {F}.",
    "On considère {F}.",
    "Supposons {F}.",
    "Démontrer que {F}.",
    "Pour tout x, {F}.",
]


# ============================================================================
# DISTRACTORS — V/E/forall/exists en sens commun, span=[]
# Cf. brief §6 négatifs + élargissement pour équilibrer.
# ============================================================================

FR_DISTRACTORS = [
    # V sens commun (volume, voiture, lettre, victoire...)
    "Le volume V est constant dans cette pièce.",
    "Soit V un volume connu de la cuve.",
    "Voiture V12 super puissante en course.",
    "Le V de la victoire est emblématique.",
    "Le volume V augmente avec la température.",
    "La lettre V de l'alphabet vient après U.",
    "Vélo V de course en compétition.",
    "Symbole V pour la vitesse en physique.",
    "Une berline V8 décapotable.",
    "Vol AF V123 retardé d'une heure.",
    "V comme victoire, en signe de paix.",
    "Le moteur V6 développe 250 chevaux.",
    "Robot V de la série télé culte.",
    "Soit V un volume sphérique.",
    # E sens commun
    "E pour effectif total de la classe.",
    "E comme énergie en mécanique.",
    "Il a obtenu un E en physique l'an dernier.",
    "Note E en mathématiques au bac.",
    "E.g. ce cas particulier nous intéresse.",
    "Vitamine E dans les noix et amandes.",
    "Le E de l'équation est implicite.",
    "Échelle E du laboratoire de physique.",
    "Mélodie en E mineur.",
    "Train E45 direction Lyon.",
    "Note E rendue en cours de SVT.",
    # forall/exists/quantificateurs sens commun
    "Il existe une solution unique au problème.",
    "Il existe encore des classes vides.",
    "Pour tous les exercices, fais le 1 et le 3.",
    "Toutes les voies ferrées ont été électrifiées.",
    "Pour toutes ces raisons, on arrête.",
    "Il n'existe aucun moyen de tricher.",
    "Toutes les filières scientifiques sont concernées.",
    "Pour tous renseignements, voir l'accueil.",
    "Existe-t-il une réponse à cette question ?",
    "Cette possibilité existe encore.",
    "Tous les élèves doivent rendre ce devoir.",
    "L'existence de cette suite est démontrée plus loin.",
    # U / inter / intervalles en sens commun
    "U comme la lettre ouverte de l'alphabet.",
    "Le métro ligne U passe par ici.",
    "Section U des étudiants en première année.",
    "Internet a changé nos vies.",
    "International, intermédiaire, intersection routière.",
    "L'intermède musical dure cinq minutes.",
    "Une intervention immédiate était nécessaire.",
    "Sur l'intervalle de temps qui nous reste, on récapitule.",
    "Pause d'un quart d'heure entre les deux cours.",
]

EN_DISTRACTORS = [
    "Volume V remains constant in this room.",
    "Vitamin E is found in nuts and almonds.",
    "Grade E in math last term was disappointing.",
    "There exists a unique solution to this problem.",
    "For all practical purposes this is fine.",
    "For example, consider the case x = 0.",
    "Vehicle V12 luxury car at the show.",
    "Letter V in the alphabet comes after U.",
    "Volume E in the encyclopedia covers physics.",
    "Mr. E asked the question yesterday.",
    "All things considered, we move on.",
    "All hands on deck for the cleanup.",
    "Existence of solutions is proven below.",
    "For everyone in the class, please listen.",
]


# ============================================================================
# UTILITAIRES
# ============================================================================

def make_span(text: str, fragment: str) -> dict | None:
    pos = text.find(fragment)
    if pos < 0:
        return None
    return {"start": pos, "end": pos + len(fragment), "label": "MATH"}


# ============================================================================
# GÉNÉRATEURS
# ============================================================================

def gen_formula_only(expr: str, lang: str) -> dict:
    """Expression math toute seule sur la ligne (cas Ctrl+Espace)."""
    return {
        "text": expr,
        "spans": [{"start": 0, "end": len(expr), "label": "MATH"}],
        "lang": lang,
    }


def gen_prose_with_quant(expr: str, lang: str) -> dict:
    """Insère l'expression dans un template prose FR ou EN."""
    templates = FR_TEMPLATES if lang == "fr" else EN_TEMPLATES
    tpl = random.choice(templates)
    text = tpl.replace("{F}", expr)
    span = make_span(text, expr)
    return {"text": text, "spans": [span] if span else [], "lang": lang}


def gen_v_e_relation(expr: str) -> dict:
    """V/E en relation seule (V > 0...) inséré dans template prose FR."""
    tpl = random.choice(FR_V_E_RELATION_TEMPLATES)
    text = tpl.replace("{F}", expr)
    span = make_span(text, expr)
    return {"text": text, "spans": [span] if span else [], "lang": "fr"}


def generate_quant_positives() -> list[dict]:
    """forall/exists/V/E avec leurs patterns var+set, prose + formule isolée."""
    examples = []
    for _, expressions in KEYWORD_BUCKETS:
        for expr in expressions:
            # FR : prose toujours
            examples.append(gen_prose_with_quant(expr, "fr"))
            # Formule isolée (Ctrl+Espace scenario) — toujours pour V/E,
            # 0.4 sinon
            if expr.startswith(("V ", "E ")) or random.random() < 0.4:
                examples.append(gen_formula_only(expr, "fr"))
            # FR autre template (varier le contexte) — 0.4
            if random.random() < 0.4:
                examples.append(gen_prose_with_quant(expr, "fr"))
            # EN — 0.3 (corpus FR-dominant pour V/E mais EN utile pour forall/exists)
            if random.random() < 0.3:
                examples.append(gen_prose_with_quant(expr, "en"))
    return examples


def generate_v_e_relations() -> list[dict]:
    """V/E en relation seule (V > 0, E ≥ 0...) — multiple templates par expr."""
    examples = []
    for expr in V_RELATION_EXPRESSIONS + E_RELATION_EXPRESSIONS:
        # 3 templates aléatoires par expression
        for _ in range(3):
            examples.append(gen_v_e_relation(expr))
        # Formule isolée
        examples.append(gen_formula_only(expr, "fr"))
    return examples


def generate_intervals() -> list[dict]:
    """Intervalles autonomes ([0,1], ]0;1[, A U B...) — formule isolée + prose."""
    examples = []
    for expr in INTERVAL_EXPRESSIONS:
        # Formule isolée (Ctrl+Espace scenario)
        examples.append(gen_formula_only(expr, "fr"))
        # Prose FR
        examples.append(gen_prose_with_quant(expr, "fr"))
        # 0.4 prob deuxième template FR
        if random.random() < 0.4:
            examples.append(gen_prose_with_quant(expr, "fr"))
        # 0.2 prob EN
        if random.random() < 0.2:
            examples.append(gen_prose_with_quant(expr, "en"))
    return examples


def generate_distractors() -> list[dict]:
    examples = []
    for text in FR_DISTRACTORS:
        examples.append({"text": text, "spans": [], "lang": "fr"})
    for text in EN_DISTRACTORS:
        examples.append({"text": text, "spans": [], "lang": "en"})
    return examples


# ============================================================================
# VALIDATION
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
    n_without = n - n_with
    by_lang = {}
    for e in examples:
        by_lang[e["lang"]] = by_lang.get(e["lang"], 0) + 1

    print(f"\nTotal       : {n} lignes")
    print(f"  positifs  : {n_with}")
    print(f"  spans=[]  : {n_without}")
    print(f"  par lang  : {by_lang}")

    # Couverture par keyword (substring dans le span MATH)
    print("\nCouverture par keyword (positifs avec mot dans le span) :")
    for kw in ["forall", "exists", "∀", "∃", "V x", "V > ", "V =", "E y", "E x", "E > "]:
        count = sum(
            1
            for e in examples
            for s in e["spans"]
            if kw in e["text"][s["start"]:s["end"]]
        )
        print(f"  {kw:<10} {count}")


# ============================================================================
# MAIN
# ============================================================================

def main() -> None:
    examples = (
        generate_quant_positives()
        + generate_v_e_relations()
        + generate_intervals()
        + generate_distractors()
    )
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
