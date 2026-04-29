"""
Extension dataset MathCursor.
3 catégories manquantes :
1. Français/anglais pur avec vocabulaire math (spans=[])
2. Prose mixte naturelle (français long autour d'une formule math)
3. Style SMS / typos / abréviations libres

Objectif : 2000 lignes supplémentaires (1000 FR + 1000 EN)
"""

import json
import random
from typing import List, Dict, Tuple

random.seed(123)

# ============================================================================
# CATÉGORIE 1 — FRANÇAIS PUR AVEC VOCAB MATH (spans=[])
# Apprend au modèle que ces mots seuls ne sont PAS du math.
# ============================================================================

FR_PROSE_WITH_MATH_VOCAB = [
    "le produit scalaire est une operation bilineaire et symetrique",
    "la notion de derivee est fondamentale en analyse",
    "les fonctions trigonometriques sont periodiques",
    "la convergence d'une suite depend de sa limite",
    "le theoreme de pythagore s'applique aux triangles rectangles",
    "l'integration par parties est une technique utile",
    "la factorisation permet de simplifier les expressions",
    "le calcul differentiel etudie les taux de variation",
    "l'algebre lineaire manipule les matrices et vecteurs",
    "la geometrie euclidienne repose sur cinq axiomes",
    "le produit vectoriel est defini en dimension trois",
    "les nombres complexes etendent les nombres reels",
    "la relation de recurrence definit une suite",
    "l'equation differentielle modelise un phenomene physique",
    "le polynome caracteristique donne les valeurs propres",
    "la matrice identite laisse les vecteurs invariants",
    "l'ensemble vide ne contient aucun element",
    "l'union de deux ensembles regroupe leurs elements",
    "l'intersection conserve les elements communs",
    "le complementaire depend de l'ensemble de reference",
    "la fonction exponentielle croit rapidement",
    "le logarithme est la fonction reciproque de l'exponentielle",
    "la primitive est l'inverse de la derivation",
    "l'angle droit mesure quatre-vingt-dix degres",
    "le cercle trigonometrique a pour rayon un",
    "les coordonnees cartesiennes decrivent un point du plan",
    "la distance euclidienne est toujours positive",
    "le vecteur nul est l'element neutre de l'addition",
    "la norme mesure la longueur d'un vecteur",
    "le determinant indique si une matrice est inversible",
    "la trace est la somme des elements diagonaux",
    "l'espace vectoriel a une structure algebrique",
    "la base canonique est simple a manipuler",
    "le noyau d'une application lineaire est un sous-espace",
    "l'image d'une fonction est son ensemble d'arrivee",
    "le graphe d'une fonction represente ses variations",
    "la continuite est une propriete locale",
    "la derivabilite implique la continuite",
    "l'injectivite signifie que deux entrees distinctes ont deux sorties distinctes",
    "la surjectivite signifie que toute valeur cible est atteinte",
    "la bijectivite combine injectivite et surjectivite",
    "la limite a gauche peut differer de la limite a droite",
    "l'asymptote verticale indique une discontinuite",
    "le point d'inflexion change la concavite",
    "le maximum local est un extremum",
    "la fonction paire est symetrique par rapport a l'axe des ordonnees",
    "la fonction impaire est symetrique par rapport a l'origine",
    "la periode d'une fonction est son plus petit motif",
    "la serie converge si la suite de ses sommes partielles converge",
    "la factorielle croit plus vite que l'exponentielle",
    "le theoreme des valeurs intermediaires est tres utile",
    "la demonstration par recurrence comporte deux etapes",
    "le raisonnement par l'absurde part d'une hypothese fausse",
    "la contraposee est logiquement equivalente",
    "l'implication differe de l'equivalence",
    "la disjonction est le ou logique",
    "la conjonction est le et logique",
    "la negation inverse la valeur de verite",
    "le quantificateur universel s'applique a tous",
    "le quantificateur existentiel affirme l'existence d'au moins un",
    "la loi normale est une loi de probabilite continue",
    "la loi binomiale compte les succes dans des essais independants",
    "l'esperance mathematique est la moyenne theorique",
    "la variance mesure la dispersion autour de la moyenne",
    "l'ecart-type est la racine carree de la variance",
    "la covariance mesure la dependance lineaire entre deux variables",
    "le coefficient de correlation est normalise",
    "la probabilite conditionnelle depend de l'evenement conditionnant",
    "l'independance signifie que l'un n'influence pas l'autre",
    "le tirage avec remise conserve les probabilites initiales",
    "le tirage sans remise modifie les probabilites au fil du temps",
    "l'arbre de probabilite visualise les issues possibles",
    "le diagramme de venn represente les ensembles",
    "l'histogramme represente une distribution",
    "la mediane partage la population en deux moities",
    "le mode est la valeur la plus frequente",
    "les quartiles divisent la distribution en quatre",
    "le diagramme en boite resume cinq valeurs cles",
    "la regression lineaire ajuste une droite aux donnees",
    "le coefficient directeur est la pente de la droite",
    "l'ordonnee a l'origine est l'intersection avec l'axe vertical",
    "la transformation affine conserve les droites",
    "la translation deplace chaque point du meme vecteur",
    "la rotation conserve les distances",
    "l'homothetie change les distances par un meme facteur",
    "la symetrie centrale inverse par rapport a un point",
    "la similitude combine rotation et homothetie",
    "l'isomorphisme preserve la structure algebrique",
    "l'automorphisme est un isomorphisme d'un ensemble sur lui-meme",
    "la congruence modulo n est une relation d'equivalence",
    "le plus grand commun diviseur se calcule par l'algorithme d'euclide",
    "le plus petit commun multiple est utilise pour additionner des fractions",
    "le nombre premier n'a que deux diviseurs",
    "la decomposition en facteurs premiers est unique",
    "la divisibilite est une relation d'ordre partiel",
    "le theoreme fondamental de l'arithmetique etablit l'unicite de la decomposition",
    "le cercle circonscrit passe par les trois sommets",
    "le cercle inscrit est tangent aux trois cotes",
    "la mediatrice d'un segment coupe celui-ci en son milieu perpendiculairement",
    "la bissectrice partage un angle en deux angles egaux",
    "la hauteur d'un triangle est issue d'un sommet",
    "la mediane relie un sommet au milieu du cote oppose",
    "le centre de gravite est le point de concours des medianes",
    "l'orthocentre est le point de concours des hauteurs",
    "le circonscrit est le point de concours des mediatrices",
    "le theoreme de thales concerne les triangles semblables",
    "le theoreme de l'angle inscrit relie les angles au centre et inscrits",
    "la formule de heron donne l'aire d'un triangle",
    "la loi des sinus relie cotes et angles d'un triangle",
    "la loi des cosinus generalise le theoreme de pythagore",
    "l'aire d'un cercle est proportionnelle au carre de son rayon",
    "le volume d'une sphere croit avec le cube du rayon",
    "la surface d'un cube a six faces identiques",
    "le prisme a deux bases polygonales paralleles",
    "le cylindre a deux bases circulaires",
    "le cone a une base circulaire et un sommet",
    "la pyramide a une base polygonale et un sommet",
    "le tetraedre est une pyramide a base triangulaire",
    "l'icosaedre a vingt faces triangulaires",
    "le polyedre regulier a toutes ses faces identiques",
    "les solides de platon sont au nombre de cinq",
    "la sphere est la surface la plus symetrique",
    "le tore a la forme d'un anneau",
    "la bande de moebius n'a qu'une seule face",
    "la bouteille de klein ne possede pas de bord",
    "la topologie etudie les proprietes invariantes par deformation continue",
    "les fractales possedent une structure auto-similaire",
    "le flocon de koch a un perimetre infini",
    "l'ensemble de mandelbrot a une frontiere infiniment complexe",
    "les nombres transcendants ne sont racines d'aucun polynome a coefficients entiers",
    "le nombre pi est transcendant",
    "le nombre d'euler est aussi transcendant",
    "la suite de fibonacci apparait dans la nature",
    "le nombre d'or est lie au pentagone regulier",
    "la spirale logarithmique est une forme remarquable",
    "la conjecture de goldbach reste a demontrer",
    "l'hypothese de riemann est un probleme du millenaire",
    "la conjecture des nombres premiers jumeaux est encore ouverte",
    "la theorie des graphes etudie les reseaux",
    "le probleme du voyageur de commerce est np-complet",
    "l'algorithmique cherche l'efficacite des calculs",
    "la complexite polynomiale est consideree comme acceptable",
    "la theorie de l'information mesure l'incertitude",
    "l'entropie quantifie le desordre",
    "la compression sans perte preserve l'information originale",
    "la cryptographie protege les communications",
    "le chiffrement asymetrique utilise deux cles",
    "la signature numerique authentifie un message",
]

EN_PROSE_WITH_MATH_VOCAB = [
    "the scalar product is a bilinear and symmetric operation",
    "the concept of derivative is fundamental in analysis",
    "trigonometric functions are periodic",
    "the convergence of a sequence depends on its limit",
    "the pythagorean theorem applies to right triangles",
    "integration by parts is a useful technique",
    "factorization helps simplify expressions",
    "differential calculus studies rates of change",
    "linear algebra handles matrices and vectors",
    "euclidean geometry rests on five axioms",
    "the cross product is defined in three dimensions",
    "complex numbers extend the real numbers",
    "a recurrence relation defines a sequence",
    "a differential equation models a physical phenomenon",
    "the characteristic polynomial gives the eigenvalues",
    "the identity matrix leaves vectors unchanged",
    "the empty set contains no element",
    "the union of two sets groups their elements",
    "the intersection keeps the common elements",
    "the complement depends on the reference set",
    "the exponential function grows rapidly",
    "the logarithm is the inverse of the exponential",
    "the antiderivative is the inverse of differentiation",
    "a right angle measures ninety degrees",
    "the trigonometric circle has radius one",
    "cartesian coordinates describe a point in the plane",
    "euclidean distance is always positive",
    "the zero vector is the additive identity",
    "the norm measures the length of a vector",
    "the determinant indicates whether a matrix is invertible",
    "the trace is the sum of diagonal elements",
    "a vector space has an algebraic structure",
    "the canonical basis is simple to handle",
    "the kernel of a linear map is a subspace",
    "the image of a function is its range",
    "the graph of a function represents its variations",
    "continuity is a local property",
    "differentiability implies continuity",
    "injectivity means distinct inputs have distinct outputs",
    "surjectivity means every target value is reached",
    "bijectivity combines injectivity and surjectivity",
    "the left limit may differ from the right limit",
    "a vertical asymptote indicates a discontinuity",
    "an inflection point changes the concavity",
    "a local maximum is an extremum",
    "an even function is symmetric about the y axis",
    "an odd function is symmetric about the origin",
    "the period of a function is its smallest repeating pattern",
    "a series converges if its partial sums converge",
    "the factorial grows faster than the exponential",
    "the intermediate value theorem is very useful",
    "proof by induction has two steps",
    "proof by contradiction starts from a false hypothesis",
    "the contrapositive is logically equivalent",
    "implication differs from equivalence",
    "disjunction is the logical or",
    "conjunction is the logical and",
    "negation inverts the truth value",
    "the universal quantifier applies to all",
    "the existential quantifier asserts the existence of at least one",
    "the normal distribution is a continuous probability distribution",
    "the binomial distribution counts successes in independent trials",
    "the expected value is the theoretical mean",
    "variance measures dispersion around the mean",
    "the standard deviation is the square root of the variance",
    "covariance measures linear dependence between two variables",
    "the correlation coefficient is normalized",
    "conditional probability depends on the conditioning event",
    "independence means one does not influence the other",
    "sampling with replacement preserves initial probabilities",
    "sampling without replacement changes probabilities over time",
    "a probability tree visualizes possible outcomes",
    "a venn diagram represents sets",
    "a histogram represents a distribution",
    "the median splits the population into two halves",
    "the mode is the most frequent value",
    "quartiles divide the distribution into four",
    "a box plot summarizes five key values",
    "linear regression fits a line to data",
    "the slope coefficient is the slope of the line",
    "the y intercept is where the line crosses the vertical axis",
    "an affine transformation preserves lines",
    "a translation moves each point by the same vector",
    "a rotation preserves distances",
    "a homothety scales distances by the same factor",
    "a central symmetry inverts with respect to a point",
    "a similarity combines rotation and homothety",
    "an isomorphism preserves algebraic structure",
    "an automorphism is an isomorphism from a set to itself",
    "congruence modulo n is an equivalence relation",
    "the greatest common divisor is computed by euclid's algorithm",
    "the least common multiple is used to add fractions",
    "a prime number has only two divisors",
    "prime factorization is unique",
    "divisibility is a partial order relation",
    "the fundamental theorem of arithmetic establishes the uniqueness of factorization",
    "the circumscribed circle passes through the three vertices",
    "the inscribed circle is tangent to the three sides",
    "the perpendicular bisector cuts a segment at its midpoint perpendicularly",
    "the angle bisector splits an angle into two equal parts",
    "the altitude of a triangle comes from a vertex",
    "the median connects a vertex to the midpoint of the opposite side",
    "the centroid is the point where medians meet",
    "the orthocenter is the point where altitudes meet",
    "the circumcenter is the point where perpendicular bisectors meet",
    "thales' theorem concerns similar triangles",
    "the inscribed angle theorem relates central and inscribed angles",
    "heron's formula gives the area of a triangle",
    "the law of sines relates sides and angles of a triangle",
    "the law of cosines generalizes the pythagorean theorem",
    "the area of a circle is proportional to the square of its radius",
    "the volume of a sphere grows with the cube of the radius",
    "a cube has six identical faces",
    "a prism has two parallel polygonal bases",
    "a cylinder has two circular bases",
    "a cone has a circular base and a vertex",
    "a pyramid has a polygonal base and a vertex",
    "a tetrahedron is a pyramid with a triangular base",
    "an icosahedron has twenty triangular faces",
    "a regular polyhedron has all identical faces",
    "there are five platonic solids",
    "the sphere is the most symmetric surface",
    "a torus has the shape of a ring",
    "a mobius strip has only one face",
    "a klein bottle has no boundary",
    "topology studies properties invariant under continuous deformation",
    "fractals have a self-similar structure",
    "the koch snowflake has infinite perimeter",
    "the mandelbrot set has an infinitely complex boundary",
    "transcendental numbers are roots of no polynomial with integer coefficients",
    "pi is transcendental",
    "euler's number is also transcendental",
    "the fibonacci sequence appears in nature",
    "the golden ratio is related to the regular pentagon",
    "the logarithmic spiral is a remarkable shape",
    "goldbach's conjecture remains to be proven",
    "the riemann hypothesis is a millennium problem",
    "the twin prime conjecture is still open",
    "graph theory studies networks",
    "the traveling salesman problem is np-complete",
    "algorithmics seeks computational efficiency",
    "polynomial complexity is considered acceptable",
    "information theory measures uncertainty",
    "entropy quantifies disorder",
    "lossless compression preserves original information",
    "cryptography protects communications",
    "asymmetric encryption uses two keys",
    "a digital signature authenticates a message",
]


# ============================================================================
# CATÉGORIE 2 — PROSE MIXTE NATURELLE
# Phrases longues en français/anglais avec 1-2 formules math vraies.
# Le modèle doit capturer UNIQUEMENT les formules, pas le français autour.
# ============================================================================

FR_PROSE_MIXED_TEMPLATES = [
    # {FORMULA} sera remplacé par un vrai fragment math
    "On note f la fonction definie par {FORMULA}. Alors f est lineaire.",
    "Dans la suite de l'exercice, on supposera que {FORMULA} et on etudiera ses consequences.",
    "Il faut bien comprendre que {FORMULA} avant de passer a la suite du raisonnement.",
    "Le professeur a insiste sur le fait que {FORMULA} est une identite remarquable.",
    "D'apres le cours precedent, on sait que {FORMULA} dans tous les cas.",
    "Supposons par l'absurde que {FORMULA} et voyons quelle contradiction en decoule.",
    "En appliquant le theoreme vu en classe, on obtient immediatement {FORMULA}.",
    "Le but de cet exercice est de demontrer que {FORMULA} est toujours vrai.",
    "Apres calcul et simplification, on aboutit a l'expression {FORMULA}.",
    "Il est facile de verifier que {FORMULA} en substituant les valeurs numeriques.",
    "Dans le cadre general, il convient de noter que {FORMULA} pour tout reel.",
    "On remarquera que {FORMULA} par identification des termes de meme degre.",
    "La demonstration de ce resultat repose sur l'egalite fondamentale {FORMULA}.",
    "Le raisonnement par recurrence permet d'etablir que {FORMULA} est vrai a tout rang.",
    "Pour resoudre cette equation, commencons par remarquer que {FORMULA}.",
    "Il est important de retenir que {FORMULA} est une propriete centrale du cours.",
    "A ce stade de la demonstration, nous avons montre que {FORMULA}.",
    "Le calcul est relativement simple : il suffit de constater que {FORMULA}.",
    "Pour terminer l'exercice, il reste a etablir que {FORMULA} dans ce cas particulier.",
    "Notons d'ores et deja que {FORMULA} ce qui nous servira plus tard.",
    "La premiere etape consiste a demontrer que {FORMULA} puis on conclut.",
    "On constate alors sans difficulte que {FORMULA} et le resultat s'ensuit.",
    "Il est crucial de ne pas oublier que {FORMULA} au moment du calcul.",
    "Cette formule permet de calculer rapidement {FORMULA} dans la pratique.",
    "Revenons maintenant a l'equation initiale pour verifier que {FORMULA}.",
    "A partir de cette hypothese, on peut deduire que {FORMULA} dans la plupart des cas.",
    "Par substitution directe, l'expression devient {FORMULA} ce qui est bien plus simple.",
    "On va maintenant chercher a caracteriser les solutions telles que {FORMULA}.",
    "Le bon reflexe ici consiste a factoriser pour obtenir {FORMULA}.",
    "En derivant les deux membres de l'equation, il vient {FORMULA}.",
]

EN_PROSE_MIXED_TEMPLATES = [
    "We denote by f the function defined by {FORMULA}. Then f is linear.",
    "In what follows, we will assume that {FORMULA} and study its consequences.",
    "It is important to understand that {FORMULA} before moving to the rest of the reasoning.",
    "The teacher emphasized that {FORMULA} is a remarkable identity.",
    "From the previous course, we know that {FORMULA} in all cases.",
    "Assume by contradiction that {FORMULA} and see what contradiction follows.",
    "Applying the theorem seen in class, we immediately obtain {FORMULA}.",
    "The goal of this exercise is to prove that {FORMULA} is always true.",
    "After calculation and simplification, we arrive at the expression {FORMULA}.",
    "It is easy to verify that {FORMULA} by substituting numerical values.",
    "In the general framework, it should be noted that {FORMULA} for any real number.",
    "We notice that {FORMULA} by identifying terms of the same degree.",
    "The proof of this result relies on the fundamental equality {FORMULA}.",
    "Induction allows us to establish that {FORMULA} is true at every rank.",
    "To solve this equation, let us first notice that {FORMULA}.",
    "It is important to remember that {FORMULA} is a central property of the course.",
    "At this stage of the proof, we have shown that {FORMULA}.",
    "The calculation is relatively simple: just notice that {FORMULA}.",
    "To finish the exercise, it remains to establish that {FORMULA} in this particular case.",
    "Let us already note that {FORMULA} which will be useful later.",
    "The first step consists in proving that {FORMULA} then we conclude.",
    "We then observe without difficulty that {FORMULA} and the result follows.",
    "It is crucial not to forget that {FORMULA} when computing.",
    "This formula allows us to quickly compute {FORMULA} in practice.",
    "Let us now go back to the initial equation to verify that {FORMULA}.",
    "From this hypothesis, we can deduce that {FORMULA} in most cases.",
    "By direct substitution, the expression becomes {FORMULA} which is much simpler.",
    "We will now look for solutions such that {FORMULA}.",
    "The right reflex here is to factor to obtain {FORMULA}.",
    "By differentiating both sides of the equation, it follows that {FORMULA}.",
]

# Formules à insérer dans les templates mixtes
MIXED_FORMULAS = [
    "f(x) = 2x + 1", "f(x) = x^2", "g(x) = sqrt(x)", "h(x) = 1/x",
    "f(x) = x^2 - 4", "u(n) = n^2", "v(n) = 1/n",
    "x^2 + 2x + 1 = (x+1)^2", "x^2 - 4 = (x-2)(x+2)",
    "sin^2(x) + cos^2(x) = 1", "cos(2x) = 2cos^2(x) - 1",
    "ln(x*y) = ln(x) + ln(y)", "e^(x+y) = e^x * e^y",
    "un = 1/n", "un = q^n", "un = n^2", "u(n+1) = 2*un + 3",
    "f'(x) = 2x", "f'(x) = cos(x)", "f''(x) + f(x) = 0",
    "P(A inter B) = P(A) * P(B)", "E(X+Y) = E(X) + E(Y)",
    "det(A*B) = det(A)*det(B)", "(A*B)^T = B^T * A^T",
    "vec u . vec v = ||u||*||v||*cos(theta)",
    "int de 0 a 1 de x^2 dx = 1/3", "lim un = 0",
    "|z|^2 = z * z_barre", "a^2 + b^2 = c^2",
    "x^3 - 1 = (x-1)(x^2+x+1)", "1 + 2 + ... + n = n(n+1)/2",
]


# ============================================================================
# CATÉGORIE 3 — STYLE SMS / TYPOS / ABRÉVIATIONS
# ============================================================================

FR_SMS_TEMPLATES = [
    # Abréviations courantes d'élèves
    "{SMS1} {FORMULA} {SMS2}",
]

FR_SMS_PREFIXES = [
    "jpp", "wesh", "bref", "franchement", "genre",
    "euh", "bon", "ok donc", "bref donc", "ok alors",
    "mdr c'est", "tqt", "dsl mais", "alors la",
    "tkt", "frr", "ptdr", "jvois pas pk",
    "ouais bah", "si jme souviens", "jsp si",
]

FR_SMS_SUFFIXES = [
    "c'est ca non", "tkt", "jpp", "frr", "mdr",
    "enfin je crois", "jsp trop", "bref", "c clair",
    "voila quoi", "fin bref", "enfin bon",
    "ca fait le taf", "c bon", "c ok comme ca",
    "ptdr", "franchement ouais",
]

EN_SMS_PREFIXES = [
    "so like", "ok so", "basically", "lol",
    "wait", "idk but", "pretty sure", "tbh",
    "wait so", "ok then", "lmao", "bruh",
    "yo", "honestly", "kinda",
]

EN_SMS_SUFFIXES = [
    "right", "idk", "lol", "tbh", "wait",
    "i guess", "or whatever", "that's it",
    "pretty sure", "maybe", "fr fr",
    "no cap", "honestly",
]

# Formules avec fautes/raccourcis réalistes
SMS_FORMULAS = [
    "f(x) = 2x+1", "x^2 = 4", "un -> 0", "lim fx x=0",
    "cos pi = -1", "sin 0 = 0", "ln x = 2", "e^x pour x=1",
    "P(A) = 1/2", "E(X) = 2", "sqrt 2", "rac de 3",
    "f'(x) = 2x", "derivee x^2 c 2x", "x^2 >= 0",
    "A inter B", "x in R", "pour tt x > 0",
    "vec AB + vec BC", "un+1 = 2un", "x = 3 ou x = -3",
    "f de x = x^2", "lim en +inf", "quand n->inf",
]


# ============================================================================
# UTILITAIRES
# ============================================================================

def make_span(text: str, fragment: str, start_offset: int = 0) -> Dict:
    pos = text.find(fragment, start_offset)
    if pos < 0:
        return None
    return {"start": pos, "end": pos + len(fragment), "label": "MATH"}


# ============================================================================
# GÉNÉRATEURS
# ============================================================================

def gen_prose_no_math(lang: str) -> Dict:
    """Phrase de prose avec vocabulaire math mais sans formule → spans=[]."""
    pool = FR_PROSE_WITH_MATH_VOCAB if lang == "fr" else EN_PROSE_WITH_MATH_VOCAB
    text = random.choice(pool)
    return {"text": text, "spans": [], "lang": lang}


def gen_prose_mixed(lang: str) -> Dict:
    """Prose longue avec UNE formule math vraie dedans."""
    templates = FR_PROSE_MIXED_TEMPLATES if lang == "fr" else EN_PROSE_MIXED_TEMPLATES
    template = random.choice(templates)
    formula = random.choice(MIXED_FORMULAS)
    text = template.replace("{FORMULA}", formula)
    span = make_span(text, formula)
    return {"text": text, "spans": [span] if span else [], "lang": lang}


def gen_sms(lang: str) -> Dict:
    """Phrase SMS/abrégée avec math minimal au milieu."""
    if lang == "fr":
        prefixes = FR_SMS_PREFIXES
        suffixes = FR_SMS_SUFFIXES
    else:
        prefixes = EN_SMS_PREFIXES
        suffixes = EN_SMS_SUFFIXES

    has_formula = random.random() > 0.25  # 75% avec formule, 25% sans

    if not has_formula:
        # Phrase SMS sans math
        text = f"{random.choice(prefixes)} jvais pas rendre le devoir avant demain {random.choice(suffixes)}"
        if lang == "en":
            text = f"{random.choice(prefixes)} i won't hand in the homework before tomorrow {random.choice(suffixes)}"
        return {"text": text, "spans": [], "lang": lang}

    formula = random.choice(SMS_FORMULAS)
    prefix = random.choice(prefixes)
    suffix = random.choice(suffixes)
    text = f"{prefix} {formula} {suffix}"
    span = make_span(text, formula)
    return {"text": text, "spans": [span] if span else [], "lang": lang}


# ============================================================================
# GÉNÉRATION PRINCIPALE
# ============================================================================

def generate_extension(lang: str, n: int) -> List[Dict]:
    distribution = {
        "prose_no_math": int(n * 0.45),    # beaucoup de négatifs avec vocab math
        "prose_mixed": int(n * 0.40),       # prose longue avec vraie formule
        "sms": int(n * 0.15),               # style SMS/typos
    }

    examples = []
    for _ in range(distribution["prose_no_math"]):
        examples.append(gen_prose_no_math(lang))
    for _ in range(distribution["prose_mixed"]):
        examples.append(gen_prose_mixed(lang))
    for _ in range(distribution["sms"]):
        examples.append(gen_sms(lang))

    while len(examples) < n:
        examples.append(gen_prose_no_math(lang))

    random.shuffle(examples)
    return examples


def write_jsonl(examples: List[Dict], path: str):
    with open(path, "w", encoding="utf-8") as f:
        for ex in examples:
            ex["spans"] = [s for s in ex["spans"] if s is not None]
            f.write(json.dumps(ex, ensure_ascii=False) + "\n")


def validate(path: str) -> int:
    errors = 0
    with open(path) as f:
        for i, line in enumerate(f):
            ex = json.loads(line)
            for span in ex["spans"]:
                if span["start"] < 0 or span["end"] > len(ex["text"]) or span["start"] >= span["end"]:
                    print(f"BAD: {path}:{i+1} {span}")
                    errors += 1
    return errors


if __name__ == "__main__":
    fr = generate_extension("fr", 1000)
    en = generate_extension("en", 1000)

    write_jsonl(fr, "/home/claude/dataset/extension_fr.jsonl")
    write_jsonl(en, "/home/claude/dataset/extension_en.jsonl")
    write_jsonl(fr + en, "/home/claude/dataset/extension_all.jsonl")

    print(f"Extension FR: {len(fr)} lignes")
    print(f"Extension EN: {len(en)} lignes")
    print(f"Total extension: {len(fr) + len(en)} lignes")

    err_fr = validate("/home/claude/dataset/extension_fr.jsonl")
    err_en = validate("/home/claude/dataset/extension_en.jsonl")
    print(f"Erreurs offsets FR: {err_fr}, EN: {err_en}")

    print()
    for name, subset in [("FR", fr), ("EN", en)]:
        n_no = sum(1 for e in subset if not e["spans"])
        n_one = sum(1 for e in subset if len(e["spans"]) == 1)
        print(f"{name}: {n_no} sans span, {n_one} avec 1 span")
