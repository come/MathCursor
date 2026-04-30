"""
Génère le corpus NER v6 couvrant les features ajoutées au moteur lattice
depuis v5 (28 avril) :
  - Function definitions : `f : x -> x+1`, `f : R -> R, x -> x^2`, `f(x)=...`
  - Implications / équivalences : `=>`, `<=>`, `==>`, `<==>`, `⇒`, `⇔`
  - Vecteurs avec coordonnées : `V(1,2,3)`, `point M(1,2)`
  - Listes paramètres avec virgule : `f(x,y)`, `(x,y,z) in R^3`
  - Ensembles canoniques étendus : `R\\{0}`, `N\\{0,1}`, `R+*`

ET renforce les trous identifiés dans les logs prod (29 avril) :
  - Longues expressions terminant par `+digit` / `-digit` / `=digit`
    (NER passait à zones=0 sur ces inputs en cours de frappe)
  - `°` (Word AutoCorrect) : 4° vs 4) — distinguer math (45°, cos(30°))
    et non-math (n°5, 4° anniversaire)
  - Bullet `*` en début de paragraphe : NEGATIVE (faux positif observé)

Cf. briefs/2026-04-29-* pour les features. Conversation 29-04 pour les trous.

Sortie : data/ner-corpus/extension_v6_recent_features.jsonl
"""

import io
import json
import random
import sys
from pathlib import Path

if hasattr(sys.stdout, "buffer"):
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

random.seed(20260429)

REPO = Path(__file__).resolve().parents[2]
DST = REPO / "data" / "ner-corpus" / "extension_v6_recent_features.jsonl"


# ============================================================================
# 1) FUNCTION DEFINITIONS
# ============================================================================

# Forme `f : x -> expr` (sans domaine explicite côté gauche)
FUNCDEF_SIMPLE = [
    "f : x -> x+1",
    "f : x -> x-1",
    "f : x -> x^2",
    "f : x -> x^2+1",
    "f : x -> x^3",
    "f : x -> 2x+1",
    "f : x -> 1/x",
    "f : x -> 1/(x+1)",
    "f : x -> sqrt(x)",
    "f : x -> exp(x)",
    "f : x -> ln(x)",
    "f : x -> sin(x)",
    "f : x -> cos(x)",
    "f : x -> tan(x)",
    "f : x -> |x|",
    "f : x -> -x",
    "f : x -> ax+b",
    "f : x -> (x-1)/(x+1)",
    "f : x -> x*ln(x)",
    "f : x -> e^x",
    "g : x -> x^2",
    "g : x -> 2x-3",
    "g : x -> 1/x^2",
    "g : t -> sin(t)",
    "g : t -> cos(t) + sin(t)",
    "h : n -> 1/n",
    "h : n -> n^2",
    "h : n -> 2^n",
    "h : k -> k!",
    "u : n -> u_n",
    "phi : x -> x^2",
    "psi : t -> e^(-t)",
    "f : x ↦ x+1",
    "f : x ↦ x^2",
    "g : x ↦ ln(x)",
    "h : t ↦ cos(t)",
    "f: x -> x+1",
    "f:x->x+1",
    "f:x->x^2",
    "g:t->sin(t)",
]

# Forme avec domaine explicite : `f : R -> R, x -> expr`
FUNCDEF_WITH_DOMAIN = [
    "f : R -> R, x -> x^2",
    "f : R -> R, x -> 2x+1",
    "f : R -> R, x -> sin(x)",
    "f : R+ -> R, x -> sqrt(x)",
    "f : R* -> R, x -> 1/x",
    "f : R*+ -> R, x -> ln(x)",
    "f : N -> N, n -> n^2",
    "f : N* -> R, n -> 1/n",
    "f : N -> R, n -> 1/(n+1)",
    "f : Z -> Z, n -> n+1",
    "g : [0,1] -> R, x -> x*(1-x)",
    "g : [0;1] -> R, x -> x^2",
    "g : ]0,1[ -> R, x -> 1/x",
    "g : [a,b] -> R, x -> f(x)",
    "h : R -> R+, x -> x^2",
    "h : R -> [0,1], x -> 1/(1+x^2)",
    "phi : [0,2pi] -> R, t -> sin(t)",
    "psi : R+ -> R+, t -> e^(-t)",
    "f : R \\ {0} -> R, x -> 1/x",
    "f : R^2 -> R, (x,y) -> x^2+y^2",
    "f : R^3 -> R, (x,y,z) -> x+y+z",
    "f : R \\to R, x \\mapsto x^2",
]

# Forme `f(x) = expr` (style équation)
FUNCDEF_EQUATION = [
    "f(x) = x+1",
    "f(x) = x^2",
    "f(x) = x^2+1",
    "f(x) = 2x+3",
    "f(x) = 1/x",
    "f(x) = sqrt(x)",
    "f(x) = exp(x)",
    "f(x) = ln(x)",
    "f(x) = sin(x)",
    "f(x) = cos(x)",
    "f(x) = (x-1)/(x+1)",
    "f(x) = ax^2 + bx + c",
    "g(x) = x^3 - 3x",
    "g(x) = x*ln(x)",
    "h(n) = 1/n",
    "h(n) = u_n + v_n",
    "u(n) = 2^n",
    "f(x,y) = x^2 + y^2",
    "f(x,y) = xy",
    "f(x,y) = sqrt(x^2+y^2)",
    "g(a,b) = a+b",
    "g(a,b,c) = abc",
    "h(x,y,z) = x+y+z",
]


# ============================================================================
# 2) IMPLICATIONS / ÉQUIVALENCES
# ============================================================================

# Forme isolée
IMPLIC_SIMPLE = [
    "A => B",
    "A <=> B",
    "A ==> B",
    "A <==> B",
    "P => Q",
    "P <=> Q",
    "p => q",
    "p <=> q",
    "A ⇒ B",
    "A ⇔ B",
    "A ⟹ B",
    "A ⟺ B",
    "x > 0 => x^2 > 0",
    "x ≥ 0 => sqrt(x) ≥ 0",
    "x = 0 => x^2 = 0",
    "x in R => x^2 in R+",
    "x in N => x in Z",
    "x in [0,1] => 0 ≤ x ≤ 1",
    "x > 0 ⇒ x^2 > 0",
    "x = 0 ⇔ x^2 = 0",
    "n pair <=> n = 2k",
    "n pair ⇔ n = 2k",
    "n impair <=> n = 2k+1",
    "f continue => f bornée",
    "f dérivable => f continue",
    "(A et B) => C",
    "A et B ⇒ C",
    "non A => non B",
    "P(0) et (P(n) => P(n+1))",
    "x = y <=> y = x",
    "x ≤ y et y ≤ x <=> x = y",
    "x in A => x in A U B",
    "x in A inter B => x in A",
    "f(x) = 0 <=> x = 0",
    "x > 0 et y > 0 => xy > 0",
    "x ≥ 0 ⇔ |x| = x",
    "n^2 pair => n pair",
    "ab = 0 <=> a = 0 ou b = 0",
]

IMPLIC_LONG = [
    "forall x in R, x^2 ≥ 0 => |x| ≥ 0",
    "forall x in R+, sqrt(x)^2 = x",
    "forall n in N, n+1 > n => n in N",
    "forall x in R, (x > 0 <=> -x < 0)",
    "x ≥ 0 et x ≤ 0 <=> x = 0",
    "(forall x, P(x)) => (exists x, P(x))",
    "exists x in R, x^2 = 1 ⇒ x = 1 ou x = -1",
    "x in [0,1] et x in [1,2] <=> x = 1",
    "f(x) = 0 et g(x) = 0 ⇒ (fg)(x) = 0",
    "u_n -> L et v_n -> L' ⇒ u_n + v_n -> L + L'",
]


# ============================================================================
# 3) VECTEURS + COORDONNÉES
# ============================================================================

VECTOR_COORDS = [
    "V(1,2)",
    "V(1,2,3)",
    "V(0,0)",
    "V(0,0,0)",
    "V(-1,2)",
    "V(1,-2,3)",
    "V(a,b)",
    "V(a,b,c)",
    "V(x,y)",
    "V(x,y,z)",
    "V(2,3)",
    "V(1,1,1)",
    "v(1,2)",
    "v(1,2,3)",
    "u(1,2)",
    "u(1,2,3)",
    "u(a,b)",
    "u(x,y)",
    "vecteur AB",
    "vecteur u",
    "vecteur v",
    "vecteur AB(1,2)",
    "vecteur u(1,2,3)",
    "point A(1,2)",
    "point B(0,1)",
    "point M(x,y)",
    "point M(1,2,3)",
    "P(0,0)",
    "P(1,1)",
    "A(0,0,0)",
    "A(1,2,3)",
    "M(x,y)",
    "M(x,y,z)",
    "\\vec{u} = (1,2)",
    "\\vec{u} = (1,2,3)",
    "\\vec{AB} = (1,2)",
    "u = (1,2)",
    "u = (1,2,3)",
    "AB = (1,2,3)",
]


# ============================================================================
# 4) LISTES PARAMÈTRES AVEC VIRGULE
# ============================================================================

COMMA_LISTS = [
    "f(x,y)",
    "f(x,y,z)",
    "g(a,b)",
    "g(a,b,c)",
    "h(u,v)",
    "h(x,y,z,t)",
    "phi(x,y)",
    "psi(s,t)",
    "(x,y) in R^2",
    "(x,y,z) in R^3",
    "(a,b) in Z^2",
    "(x,y) ∈ R^2",
    "(x,y,z) ∈ R^3",
    "(a_1, a_2)",
    "(a_1, a_2, ..., a_n)",
    "(u_n, v_n)",
    "(x_1, x_2, x_3)",
    "(a,b,c) ∈ R^3",
    "(x,y) = (1,2)",
    "(x,y,z) = (0,0,0)",
    "f(x_1, x_2, x_3) = x_1 + x_2 + x_3",
    "g(a,b,c,d)",
    "M(x,y,z) = (1,2,3)",
    "couple (x,y)",
    "triplet (x,y,z)",
]


# ============================================================================
# 5) ENSEMBLES CANONIQUES ÉTENDUS
# ============================================================================

CANONICAL_SETS = [
    "R*",
    "R+",
    "R-",
    "R+*",
    "R-*",
    "R*+",
    "R*-",
    "N*",
    "N+",
    "Z*",
    "Q*",
    "R \\ {0}",
    "R\\{0}",
    "R \\ {0, 1}",
    "N \\ {0}",
    "N\\{0}",
    "N \\ {0,1}",
    "Z \\ {0}",
    "x in R*",
    "x in R+",
    "x in R+*",
    "x ∈ R*",
    "x ∈ R+*",
    "y in N*",
    "y ∈ N*",
    "n ∈ Z*",
    "x in R \\ {0}",
    "x ∈ R \\ {0}",
    "n in N \\ {0,1}",
    "forall x in R*",
    "forall x in R+*",
    "forall x in R \\ {0}",
    "exists x in R+*",
    "x in R^* x R^*",
    "R^*+",
    "R^+*",
    "R^* \\ {1}",
]


# ============================================================================
# 6) TROUS DES LOGS — LONGUES EXPRESSIONS terminant par +digit/-digit/=digit
# C'est LE bug observé en prod : NER passait à zones=0 sur ces inputs
# en cours de frappe alors que le contenu est clairement math.
# ============================================================================

LONG_EXPR_DIGIT_END = [
    # Variations sur "Somme k 1 n f(k) = 1/x^2 + tan^2(x) / sqrt(...)"
    "Somme k 1 n f(k) = 1/x^2 + tan^2(x) / sqrt(4+1)",
    "Somme k 1 n f(k) = 1/x^2 + tan^2(x) / sqrt(4+1",
    "Somme k 1 n f(k) = 1/x^2 + tan^2(x) / sqrt(4)",
    "Somme k 1 n f(k) = 1/x^2 + tan^2(x) / sqrt(4",
    "Somme k=1 n f(k) = 1/x^2 + tan^2(x) / sqrt(4+1)",
    "somme k 1 n f(k) = 1/x^2 + sin(x)^2",
    "somme k 0 inf 1/k^2",
    "somme k 1 n k^2",
    # Toutes ces longues expressions doivent rester math jusqu'au bout,
    # même quand elles se terminent par +1, -2, =3
    "f(x) = x^2 + 2x + 1",
    "f(x) = x^2 - 3x + 2",
    "f(x) = (x+1)(x-1) = x^2 - 1",
    "g(n) = sum k 1 n k = n(n+1)/2",
    "u_n = (-1)^n / (n+1)",
    "u_n = u_(n-1) + 2",
    "u_n = u_(n-1) + 1",
    "1 + 2 + 3 + ... + n = n(n+1)/2",
    "sum_(k=1)^n k = n(n+1)/2",
    "sum_(k=0)^inf 1/k! = e",
    "lim_(x->0) sin(x)/x = 1",
    "lim_(n->inf) (1+1/n)^n = e",
    "f'(x) = 2x + 1",
    "f'(x) = 3x^2 - 2",
    "f'(x) = cos(x) - sin(x)",
    "f''(x) = 6x - 2",
    "(x+1)^2 = x^2 + 2x + 1",
    "(x-1)(x+1) = x^2 - 1",
    "exp(x+y) = exp(x) * exp(y)",
    "ln(xy) = ln(x) + ln(y)",
    "sin^2(x) + cos^2(x) = 1",
    "1 - cos(2x) = 2 sin^2(x)",
    "racine carrée de x^2 + 1",
    "sqrt(x^2 + 1)",
    "sqrt(a^2 + b^2)",
    "intégrale de 0 à 1 x^2 dx = 1/3",
    "int 0 1 x^2 dx = 1/3",
    "int 0 +inf e^(-x) dx = 1",
    # Inputs en cours de frappe (incomplets) — doivent rester math
    "1/x^2 + tan^2(x) / sqrt(4+1)",
    "1/x^2 + tan^2(x) / sqrt(4+1",
    "1/x^2 + tan^2(x) / sqrt(4+",
    "1/x^2 + tan^2(x) / sqrt(4",
    "1/x^2 + tan^2(x) / sqrt",
    "1/x^2 + tan^2(x) /",
    "f(k) = 1/x^2 + tan^2(x)",
    "x^2 + 2x + 1",
    "x^2 + 2x +",
    "x^2 + 2x",
    "x^2 +",
    "(4+1)",
    "(4+1",
    "(a+b)",
    "(a+b)^2",
    # Avec digit en fin
    "n + 1",
    "n - 1",
    "n + 2",
    "x = 0",
    "x = 1",
    "y = 2",
    "f(0) = 1",
    "f(1) = 0",
    "f(2) = 4",
    "u_n = 0",
    "u_0 = 1",
    "u_1 = 2",
    "f(x) = 0",
    "f(x) = 1",
    "g(x) - 1 = 0",
    "g(x) + 1 = 2",
]


# ============================================================================
# 7) DEGRÉ ° — distinguer math (angle) et non-math (n°/4°)
# ============================================================================

DEGREE_MATH = [
    "45°",
    "30°",
    "60°",
    "90°",
    "180°",
    "cos(45°)",
    "sin(30°)",
    "tan(60°)",
    "cos(30°) = sqrt(3)/2",
    "sin(45°) = sqrt(2)/2",
    "angle de 60°",
    "un angle droit de 90°",
    "le triangle a un angle de 30°",
    "rotation de 45°",
    "rotation de 90°",
]


# ============================================================================
# 8) BULLETS / PUCES — NEGATIVES (* en début de ligne ≠ math)
# ============================================================================

BULLET_NEGATIVES = [
    "* Item 1",
    "* Item 2",
    "* Premier exercice",
    "* Deuxième partie",
    "* À acheter au magasin",
    "* Réviser le chapitre 3",
    "* Liste des courses",
    "* Préparer le contrôle",
    "- Item 1",
    "- Premier point",
    "- Réviser",
    "- Faire les exos",
    "• Premier exercice",
    "• À traiter en cours",
    "* Devoir maison",
    "- Devoir maison",
    "* Question 1",
    "* Question 2",
    "* a) calculer la dérivée",
    "* b) étudier le signe",
    "- a) Démontrer",
    "- b) En déduire",
]


# ============================================================================
# 9) DISTRACTEURS — mots ambigus en sens commun
# ============================================================================

FR_DISTRACTORS = [
    # function en sens commun
    "La fonction publique recrute des enseignants.",
    "La fonction de directeur est exigeante.",
    "Une fonction de cadre supérieur.",
    "Cette fonction sociale est essentielle.",
    "Fonction f en mathématiques.",
    # implication / équivalence en sens commun
    "L'implication politique du président est forte.",
    "Son implication dans le projet est totale.",
    "Une équivalence des diplômes est demandée.",
    "L'équivalence entre les deux licences est admise.",
    "Cette implication entraîne plusieurs conséquences.",
    # vecteur en sens commun
    "Le vecteur de la croissance, c'est l'innovation.",
    "Vecteur de transmission de la maladie.",
    "Le vecteur principal du projet est l'équipe.",
    "Un vecteur idéologique majeur.",
    # point en sens commun
    "Le point M est important pour la suite.",
    "Mettre les points sur les i.",
    "À ce point précis du débat.",
    "Le point de vue de l'auteur est clair.",
    "Un point d'honneur à respecter.",
    # ° non-math (°)
    "n°5 sur la liste des élèves.",
    "n° 12 dans le classement.",
    "Bureau n°7 au premier étage.",
    "C'est mon 4° anniversaire ce week-end.",
    "Le 3° trimestre est difficile.",
    "Article n° 42 de la Constitution.",
    "Réf : n°2026-04-29.",
    "1° cas, 2° cas et 3° cas dans le cours.",
    # intersection / union en sens commun
    "L'intersection des deux rues est dangereuse.",
    "À l'intersection on tourne à droite.",
    "Une intersection est interdite la nuit.",
    "L'union européenne est née en 1992.",
    "Une union sacrée pour la paix.",
    "Union libre et mariage civil.",
    # set / ensemble en sens commun
    "Un ensemble cohérent de mesures.",
    "Visiter l'ensemble du musée.",
    "L'ensemble des étudiants est convoqué.",
    "Un ensemble de pavillons modernes.",
    # Bullets sans math (renforcement)
    "* Acheter du pain",
    "* Faire les courses",
    "* Lire le chapitre 4",
    "- Réserver le restaurant",
    "- Téléphoner au médecin",
    "• Préparer la valise",
    # f, g, h en sens commun (lettres seules)
    "Note F au contrôle de physique.",
    "Plan F en cas d'urgence.",
    "Le H muet en français.",
    "Plan G secret défense.",
    # Phrases longues sans math
    "Le cours de mathématiques de la semaine prochaine est annulé.",
    "L'enseignant a expliqué la leçon avec beaucoup de patience.",
    "Les élèves devront rendre le devoir lundi prochain matin.",
    "Une réunion de parents est prévue jeudi à 18 heures.",
    "Le directeur convoque tous les délégués demain matin.",
    "On se retrouve à la cantine pour déjeuner ensemble.",
]

EN_DISTRACTORS = [
    "The function of a manager is demanding.",
    "Vector of disease transmission.",
    "Point of view of the author is clear.",
    "Implication of his absence is unclear.",
    "Equivalence of degrees is required.",
    "Set of measures must be implemented.",
    "Intersection of two streets is dangerous.",
    "Union jack flag is iconic.",
    "Bullet point one is the most important.",
    "Function of the heart is to pump blood.",
    "* Buy bread",
    "* Read chapter 4",
    "- Call the doctor",
    "Item n°5 on the list.",
    "Reference number n°123.",
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
    "On définit {F}.",
    "On définit la fonction {F}.",
    "Étudier {F}.",
    "Calculer {F}.",
    "Soit la fonction {F}.",
    "Considérons la fonction {F}.",
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
    "Define the function {F}.",
    "Compute {F}.",
]


# ============================================================================
# UTILITAIRES
# ============================================================================

def make_span(text: str, fragment: str) -> dict | None:
    pos = text.find(fragment)
    if pos < 0:
        return None
    return {"start": pos, "end": pos + len(fragment), "label": "MATH"}


def gen_formula_only(expr: str, lang: str = "fr") -> dict:
    """Expression math toute seule (cas Ctrl+Espace / saisie isolée)."""
    return {
        "text": expr,
        "spans": [{"start": 0, "end": len(expr), "label": "MATH"}],
        "lang": lang,
    }


def gen_prose(expr: str, lang: str = "fr") -> dict:
    templates = FR_TEMPLATES if lang == "fr" else EN_TEMPLATES
    tpl = random.choice(templates)
    text = tpl.replace("{F}", expr)
    span = make_span(text, expr)
    return {"text": text, "spans": [span] if span else [], "lang": lang}


def gen_negative(text: str, lang: str = "fr") -> dict:
    return {"text": text, "spans": [], "lang": lang}


# ============================================================================
# GÉNÉRATEURS PAR CATÉGORIE
# ============================================================================

def generate_funcdefs() -> list[dict]:
    examples = []
    for expr in FUNCDEF_SIMPLE:
        examples.append(gen_formula_only(expr, "fr"))
        examples.append(gen_prose(expr, "fr"))
        if random.random() < 0.4:
            examples.append(gen_prose(expr, "fr"))
        if random.random() < 0.25:
            examples.append(gen_prose(expr, "en"))
    for expr in FUNCDEF_WITH_DOMAIN:
        examples.append(gen_formula_only(expr, "fr"))
        examples.append(gen_prose(expr, "fr"))
        if random.random() < 0.3:
            examples.append(gen_prose(expr, "en"))
    for expr in FUNCDEF_EQUATION:
        examples.append(gen_formula_only(expr, "fr"))
        examples.append(gen_prose(expr, "fr"))
        if random.random() < 0.4:
            examples.append(gen_prose(expr, "fr"))
        if random.random() < 0.25:
            examples.append(gen_prose(expr, "en"))
    return examples


def generate_implications() -> list[dict]:
    examples = []
    for expr in IMPLIC_SIMPLE:
        examples.append(gen_formula_only(expr, "fr"))
        examples.append(gen_prose(expr, "fr"))
        if random.random() < 0.3:
            examples.append(gen_prose(expr, "en"))
    for expr in IMPLIC_LONG:
        examples.append(gen_formula_only(expr, "fr"))
        examples.append(gen_prose(expr, "fr"))
    return examples


def generate_vectors() -> list[dict]:
    examples = []
    for expr in VECTOR_COORDS:
        examples.append(gen_formula_only(expr, "fr"))
        examples.append(gen_prose(expr, "fr"))
        if random.random() < 0.4:
            examples.append(gen_prose(expr, "fr"))
    return examples


def generate_comma_lists() -> list[dict]:
    examples = []
    for expr in COMMA_LISTS:
        examples.append(gen_formula_only(expr, "fr"))
        examples.append(gen_prose(expr, "fr"))
        if random.random() < 0.25:
            examples.append(gen_prose(expr, "en"))
    return examples


def generate_canonical_sets() -> list[dict]:
    examples = []
    for expr in CANONICAL_SETS:
        examples.append(gen_formula_only(expr, "fr"))
        examples.append(gen_prose(expr, "fr"))
        if random.random() < 0.3:
            examples.append(gen_prose(expr, "fr"))
    return examples


def generate_long_expr() -> list[dict]:
    """Trous des logs : longues expressions terminant par +digit/-digit/=digit.
    Pas de prose ici — on veut entraîner le modèle à voir l'expression nue
    car c'est exactement le contexte où il décroche en prod (ligne quasi
    vide avec une longue expression math en cours de saisie)."""
    examples = []
    for expr in LONG_EXPR_DIGIT_END:
        examples.append(gen_formula_only(expr, "fr"))
        # Variations avec léger contexte prose pour robustesse
        if random.random() < 0.4:
            examples.append(gen_prose(expr, "fr"))
    return examples


def generate_degree_math() -> list[dict]:
    examples = []
    for expr in DEGREE_MATH:
        examples.append(gen_formula_only(expr, "fr"))
        examples.append(gen_prose(expr, "fr"))
    return examples


def generate_bullet_negatives() -> list[dict]:
    """* Item, - Item, • Item — span vide. Critique : le NER doit
    apprendre que `*` en tête de ligne suivi d'un mot n'est pas math."""
    return [gen_negative(text, "fr") for text in BULLET_NEGATIVES]


def generate_distractors() -> list[dict]:
    examples = []
    for text in FR_DISTRACTORS:
        examples.append(gen_negative(text, "fr"))
    for text in EN_DISTRACTORS:
        examples.append(gen_negative(text, "en"))
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
            else:
                fragment = ex["text"][span["start"]:span["end"]]
                if not fragment.strip():
                    print(f"EMPTY span line {i+1}: {span} in {ex['text']!r}")
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
    print(f"  positifs  : {n_with} ({100*n_with/n:.0f} %)")
    print(f"  spans=[]  : {n_without} ({100*n_without/n:.0f} %)")
    print(f"  par lang  : {by_lang}")

    keywords = [
        "->", "↦", "=>", "<=>", "==>", "⇒", "⇔",
        "V(", "vecteur", "point ",
        "R*", "R+", "R-", "N*", "\\{0}",
        "Somme", "sum_", "sqrt",
        "°", "f(x)", "f(x,y)",
    ]
    print("\nCouverture par keyword (positifs avec mot dans le span) :")
    for kw in keywords:
        count = sum(
            1 for e in examples for s in e["spans"]
            if kw in e["text"][s["start"]:s["end"]]
        )
        print(f"  {kw:<12} {count}")

    neg_keywords = ["*", "-", "•", "n°", "fonction publique", "intersection",
                    "vecteur de"]
    print("\nDistracteurs (lignes spans=[] contenant le mot) :")
    for kw in neg_keywords:
        count = sum(
            1 for e in examples
            if not e["spans"] and kw in e["text"]
        )
        print(f"  {kw!r:<25} {count}")


# ============================================================================
# MAIN
# ============================================================================

def main() -> None:
    examples = (
        generate_funcdefs()
        + generate_implications()
        + generate_vectors()
        + generate_comma_lists()
        + generate_canonical_sets()
        + generate_long_expr()
        + generate_degree_math()
        + generate_bullet_negatives()
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
