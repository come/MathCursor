"""
Génère des exemples d'entraînement où le keyword math en début de zone est
inclus dans le span MATH.

Contexte : sur le corpus v3 (commit 60b8af4), `somme` et `frac` ont 0
occurrence positive — le modèle a appris que ces mots n'introduisent jamais
de zone math. Conséquence runtime : `On a somme k 1 n` détecte ` 1 n`
seulement, ce qui produit un OMath inutile.

Cf. ADR docs/dev/decisions/2026-04-27-Feat-ner-corpus-v4-keywords.md
et brief docs/dev/briefs/2026-04-27-ner-retraining-keywords.md.

Sortie : data/ner-corpus/extension_v4_keywords.jsonl
"""

import json
import random
from pathlib import Path

random.seed(20260427)

REPO = Path(__file__).resolve().parents[2]
DST = REPO / "data" / "ner-corpus" / "extension_v4_keywords.jsonl"


# ============================================================================
# EXPRESSIONS MATH AVEC KEYWORD EN TÊTE
# Chaque liste contient des fragments complets keyword+body, syntax MathCursor
# (séparateur espace, pas de LaTeX). Variations couvertes :
#  - avec / sans `=` après variable (somme k 1 n / somme k=1 n)
#  - avec / sans flèche (lim x 0 / lim x -> 0)
#  - body simple (atome) / composé (f(x), 2x+1, cos x)
# ============================================================================

SUM_EXPRESSIONS = [
    "somme k 1 n",
    "somme k 1 n k^2",
    "somme k 1 n k",
    "somme k 1 n 1/k",
    "somme k 1 n 1/k^2",
    "somme k=1 n k",
    "somme k=1 n k^2",
    "somme k=0 n+1 k",
    "somme k=0 n cos k",
    "somme k=0 n+1 cos 2x",
    "somme i 1 n a_i",
    "somme i=1 n a_i",
    "somme i 0 N x_i",
    "somme k 1 +inf 1/k^2",
    "somme n=0 +inf x^n / n!",
    "somme k 0 n (-1)^k",
    "sum k 1 n",
    "sum k 1 n k^2",
    "sum k=1 n k",
    "sum k=0 n+1 cos 2x",
    "sum i 1 n a_i",
    "sum n 0 inf x^n",
    "sum k 1 N (2k+1)",
    "Sum k=1 n k(k+1)",
]

LIM_EXPRESSIONS = [
    "lim x 0",
    "lim x -> 0",
    "lim x 0 f(x)",
    "lim x -> 0 f(x)",
    "lim x 0 sin x / x",
    "lim x -> 0 sin x / x",
    "lim x 0 frac sin x x",
    "lim x +inf",
    "lim x -> +inf",
    "lim x +inf 1/x",
    "lim x -> +inf 1/x",
    "lim x -> +inf f(x) = 0",
    "lim n +inf u_n",
    "lim n -> +inf u_n",
    "lim n -> +inf (1+1/n)^n",
    "lim h 0 (f(x+h)-f(x))/h",
    "lim h -> 0 (f(x+h)-f(x))/h",
    "limite x 0",
    "limite x -> 0",
    "limite x +inf 1/x",
    "limite n -> +inf u_n",
    "limite x -> 0 sin x / x",
]

INT_EXPRESSIONS = [
    "int 0 1",
    "int 0 1 x dx",
    "int 0 1 x^2 dx",
    "int -1 1 x dx",
    "int 0 +inf e^-x dx",
    "int a b f(x) dx",
    "int 0 pi sin x dx",
    "int 0 1 (2x+1) dx",
    "int_0^1 x dx",
    "int_a^b f(x) dx",
    "integrale 0 1 x dx",
    "integrale 0 1 x^2 dx",
    "integrale a b f(x) dx",
    "intégrale 0 1 x dx",
    "intégrale a b f(x) dx",
    "intégrale 0 +inf e^-x dx",
]

FRAC_EXPRESSIONS = [
    "frac a b",
    "frac 1 2",
    "frac 1 n",
    "frac x x+1",
    "frac x+1 x-1",
    "frac sin x x",
    "frac sin x cos x",
    "frac (a+b) (a-b)",
    "frac (a+b)^2 (a-b)^2",
    "frac 1 (2n)",
    "frac n+1 n",
    "frac 2x+1 x-3",
    "frac e^x e^x+1",
    "frac dx dt",
    "frac d f d x",
    "frac a^2+b^2 a^2-b^2",
]

RACINE_EXPRESSIONS = [
    "racine x",
    "racine x+1",
    "racine 2",
    "racine 3",
    "racine x^2+1",
    "racine x^2 - 1",
    "racine a^2+b^2",
    "racine n+1",
    "racine x+1 + racine x",
    "sqrt x",
    "sqrt 2",
    "sqrt x+1",
    "sqrt x^2 + 1",
    "sqrt(x+1)",
    "sqrt(x^2 - 4)",
    "rac x",
    "rac 2",
    "rac x+1",
    "rac x^2 + 1",
]

VEC_EXPRESSIONS = [
    "vec u",
    "vec u + vec v",
    "vec AB",
    "vec AB + vec BC",
    "vec AB . vec AC",
    "vec u . vec v",
    "vec OM",
    "vec OA + vec OB",
    "vecteur u",
    "vecteur AB",
    "vecteur AB + vecteur BC",
    "vec n unitaire",
    "vec u = (1, 2)",
    "vec AB = (3, -1)",
]

PROD_EXPRESSIONS = [
    "prod k 1 n",
    "prod k 1 n k",
    "prod k=1 n k",
    "prod k=1 n (1+1/k)",
    "prod i 1 n a_i",
    "produit k 1 n",
    "produit k 1 n k",
    "produit k=1 n (2k+1)",
    "produit i=0 n a_i",
]

QUANTIFIER_EXPRESSIONS = [
    "forall x in R",
    "forall x > 0",
    "forall epsilon > 0",
    "forall n in N",
    "forall x in [0,1]",
    "exists x in R",
    "exists n in N",
    "exists x > 0",
    "exists ! x",
    "forall x in R, f(x) >= 0",
    "exists n in N tel que u_n > M",
]

INF_EXPRESSIONS = [
    "inf {x : x in R, x > 0}",
    "inf E",
    "inf A inter B",
    "+inf",
    "-inf",
    "x -> +inf",
    "x -> -inf",
    "n -> +inf",
    "u_n -> +inf",
]


# Tous les buckets — utilisés pour itérer en pondérant par taille
KEYWORD_BUCKETS = [
    ("sum", SUM_EXPRESSIONS),
    ("lim", LIM_EXPRESSIONS),
    ("int", INT_EXPRESSIONS),
    ("frac", FRAC_EXPRESSIONS),
    ("racine", RACINE_EXPRESSIONS),
    ("vec", VEC_EXPRESSIONS),
    ("prod", PROD_EXPRESSIONS),
    ("quantifier", QUANTIFIER_EXPRESSIONS),
    ("inf", INF_EXPRESSIONS),
]


# ============================================================================
# TEMPLATES PROSE
# {F} = expression math (avec keyword en tête)
# ============================================================================

FR_TEMPLATES = [
    "On a {F}.",
    "Soit {F}.",
    "Calculons {F}.",
    "Calculer {F}.",
    "On pose {F}.",
    "On note {F}.",
    "On en déduit {F}.",
    "On suppose {F}.",
    "Démontrer que {F} > 0.",
    "Montrer que {F} est strictement positif.",
    "Montrer que {F} est défini.",
    "L'expression {F} se simplifie.",
    "On a alors {F}.",
    "D'après le cours, {F}.",
    "Si {F}, alors le résultat suit.",
    "On considère {F}.",
    "Évaluer {F}.",
    "Soit {F} strictement positif.",
    "On note F = {F}.",
    "Considérons {F}.",
    "Par définition, {F}.",
    "Il est clair que {F}.",
    "On vérifie que {F}.",
    "Pour tout n, {F}.",
    "En classe on a vu {F}.",
    "L'enseignant a écrit {F} au tableau.",
]

EN_TEMPLATES = [
    "We have {F}.",
    "Let {F}.",
    "Compute {F}.",
    "We note {F}.",
    "Consider {F}.",
    "We deduce {F}.",
    "Show that {F} > 0.",
    "Prove that {F} is well-defined.",
    "By definition, {F}.",
    "It is clear that {F}.",
    "We then have {F}.",
    "From the lecture, {F}.",
    "If {F}, the result follows.",
    "Evaluate {F}.",
    "We set F = {F}.",
    "Note that {F}.",
]


# ============================================================================
# DISTRACTORS — keyword utilisé en sens commun, span=[]
# Brief §6 + élargissement pour équilibrer chaque keyword.
# ============================================================================

FR_DISTRACTORS = [
    # somme / sum (sens commun : argent, addition de côtés...)
    "J'ai mis ma somme de côté ce mois-ci.",
    "La somme demandée est trop élevée.",
    "Il a payé une grosse somme pour cette voiture.",
    "La somme totale dépasse mes prévisions.",
    "On a fait la somme des ingrédients.",
    "Je connais la somme par cœur.",
    "Sa somme d'argent était insuffisante.",
    # lim / limite (limite d'âge, à la limite)
    "La limite d'âge pour ce concours est fixée à 25 ans.",
    "À la limite, je veux bien essayer demain.",
    "Sa patience avait atteint sa limite.",
    "La limite de vitesse en ville est de 50.",
    "Il y a une limite à ne pas dépasser.",
    "Ma limite ce soir, c'est dix heures.",
    # int / integrale (intégral au sens "complet")
    "Le rendu intégral du film dure trois heures.",
    "Il a fait une intégrale de Bach.",
    "Le texte intégral est disponible en ligne.",
    "Une formation intégrale prend deux ans.",
    # racine (racine d'un arbre, racine du problème)
    "La racine du problème est ailleurs.",
    "Les racines de cet arbre sont profondes.",
    "Sa racine étymologique vient du latin.",
    "Il faut s'attaquer à la racine du mal.",
    "La racine du mot est commune aux deux langues.",
    # frac (fractale, fraction au sens commun)
    "Les fractales ont une structure auto-similaire.",
    "Une fraction de la population s'est exprimée.",
    "Il s'est cassé en mille fractures.",
    "Il a juste eu une fracture du poignet.",
    "Ce n'est qu'une fraction du problème.",
    # vec (vecteur biologique, vector au sens commun)
    "Le moustique est un vecteur de maladies.",
    "Cette idée est un puissant vecteur de changement.",
    # prod / produit (produit du supermarché)
    "Le produit est en promotion cette semaine.",
    "Ce produit ménager est efficace.",
    "Le rayon produits frais est au fond du magasin.",
    # forall / exists / inf (mots techniques pas écrits comme ça en français)
    "L'inférieur droit du document est mal aligné.",
    "Le sommet de la pyramide est visible de loin.",
    "La pyramide a un sommet visible depuis le sol.",
    # racisme — distractor mentionné brief §6
    "Le racisme est inacceptable dans toute société.",
    "Combattre le racisme est l'affaire de tous.",
]

EN_DISTRACTORS = [
    "She set aside a large sum of money last year.",
    "The total sum was higher than expected.",
    "He paid a hefty sum for that car.",
    "There is a limit to my patience.",
    "The age limit for the competition is twenty-five.",
    "We've reached the speed limit on this road.",
    "The integral text is available online.",
    "An integral approach is required here.",
    "The root of the problem lies elsewhere.",
    "Tree roots can damage pavements.",
    "The fractal patterns of nature are fascinating.",
    "Fractures of the wrist heal in six weeks.",
    "Only a fraction of the audience stayed.",
    "Mosquitoes are a vector for several diseases.",
    "This product is on sale this week.",
    "The pyramid has a visible summit.",
    "Racism is unacceptable in any society.",
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
    """Expression math toute seule sur la ligne."""
    return {
        "text": expr,
        "spans": [{"start": 0, "end": len(expr), "label": "MATH"}],
        "lang": lang,
    }


def gen_prose_with_keyword(expr: str, lang: str) -> dict:
    """Insère l'expression dans un template prose FR ou EN."""
    templates = FR_TEMPLATES if lang == "fr" else EN_TEMPLATES
    tpl = random.choice(templates)
    text = tpl.replace("{F}", expr)
    span = make_span(text, expr)
    return {"text": text, "spans": [span] if span else [], "lang": lang}


def generate_positives() -> list[dict]:
    """Pour chaque bucket : ~70% prose, ~30% formule isolée, FR + EN."""
    examples = []
    for _, expressions in KEYWORD_BUCKETS:
        for expr in expressions:
            # FR : 1 prose + parfois 1 formule isolée
            examples.append(gen_prose_with_keyword(expr, "fr"))
            if random.random() < 0.3:
                examples.append(gen_formula_only(expr, "fr"))
            # EN : moins fréquent (corpus déjà majoritairement EN sur sum/lim)
            if random.random() < 0.5:
                examples.append(gen_prose_with_keyword(expr, "en"))
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

    # Couverture par keyword (substring match dans le span)
    print("\nCouverture par keyword (positifs avec mot dans le span) :")
    for kw in [
        "somme", "sum", "lim", "limite", "int ", "integrale", "intégrale",
        "frac", "racine", "sqrt", "rac ", "vec", "vecteur",
        "prod", "produit", "forall", "exists", "inf",
    ]:
        count = sum(
            1
            for e in examples
            for s in e["spans"]
            if kw in e["text"][s["start"]:s["end"]]
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
