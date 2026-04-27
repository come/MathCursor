"""
Génère des contre-exemples (spans=[]) pour réduire les faux positifs du NER.

Motivé par les sorties observées dans train_mathcursor.ipynb cell-19 :
- 'Mon frere a sin aine comme prenom' → détecte 'sin aine' (conf 0.97)
- 'racine carree ... dans R' → détecte 'R' seul (conf 0.63)
- 'cos pi = -1 et pas 1' → détecte 'pas 1'

On force explicitement le modèle à apprendre que ces contextes ne sont PAS
du math en lui donnant des exemples annotés spans=[].

Sortie : data/ner-corpus/extension_v3_false_positives.jsonl
"""

import json
import random
from pathlib import Path

random.seed(31415)

REPO = Path(__file__).resolve().parents[2]
DST = REPO / "data" / "ner-corpus" / "extension_v3_false_positives.jsonl"


# Substrings math qui apparaissent dans des mots ou noms propres non-math
FR_SUBSTRING_TRAPS = [
    "Mon frère Sinane est content.",
    "Elle s'appelle Sinéad et vient d'Irlande.",
    "Le prénom Cosima vient d'Italie.",
    "Costa Rica est un beau pays.",
    "La marque Cosmos vend des produits bio.",
    "Il habite à Sinaloa au Mexique.",
    "L'actrice Cosima Coppola est jeune.",
    "Sinon on va au cinéma ce soir.",
    "Sinusite aiguë et rhume, pas drôle.",
    "L'integral food store a ouvert.",
    "Les sinusoïdes apparaissent en physique (mais sans formule ici).",
    "Sinon, franchement, j'en ai marre.",
    "Le tangage du bateau était impressionnant.",
    "La tangente, c'est un truc géométrique.",
    "Le cosinus a été étudié en cours mais là je parle d'autre chose.",
    "J'ai acheté un radeau pour descendre la rivière.",
    "La racine du problème est ailleurs.",
    "Le primage est trop élevé cette année.",
    "Ma grand-mère faisait de la somme toutes nuits.",
    "C'est un integrale au personnel du magasin.",
]

EN_SUBSTRING_TRAPS = [
    "My brother Sinan is coming over.",
    "Sinéad O'Connor was a singer.",
    "Cosima is a beautiful name.",
    "Costa Rica is a lovely country.",
    "The Cosmos is infinite.",
    "Sinusitis is annoying.",
    "Sinai mountain is in Egypt.",
    "Sine qua non is a latin phrase.",
    "The costume party is tomorrow.",
    "I bought a cosmetic kit.",
    "The root of the problem is elsewhere.",
    "The primary school opens at nine.",
    "Functional integrals are hard but we skip the formulas.",
    "The tangent of the discussion got us off track.",
    "The radical views of the group are controversial.",
    "Something has to divide them eventually.",
    "Please sum up what you said earlier.",
    "That's a tangent but interesting.",
]


# Mots-lettres isolés (R, N, Z, C, x, y) dans un contexte prose non-math
FR_LETTER_IN_PROSE = [
    "Ça fait R comme rouge.",
    "Dans le métro ligne R, il y a beaucoup de monde.",
    "Le N de ne pas confondre.",
    "Z comme Zorro, c'est mon héros.",
    "Le C est une note de musique.",
    "Le P majuscule en calligraphie est élégant.",
    "Le code postal commence par R dans cette ville.",
    "Point de départ R sur la carte au trésor.",
    "Le groupe R a joué hier soir.",
    "Il s'appelle M. X dans le dessin animé.",
]

EN_LETTER_IN_PROSE = [
    "It starts with R like red.",
    "Point R on the map.",
    "N is for November.",
    "The letter Z is last.",
    "C sharp is a music note.",
    "Mister X from the cartoon.",
    "The R category in the library.",
    "Zip code starts with N here.",
]


# Phrases complètes "traps" copiées/adaptées de cell-19 du notebook
PROSE_TRAPS = [
    ("fr", "Le produit scalaire de deux vecteurs orthogonaux est nul."),
    ("fr", "La racine carrée d'un nombre négatif n'existe pas dans R."),
    ("fr", "L'intégration par parties est une technique utile."),
    ("fr", "La dérivée d'une constante est toujours nulle."),
    ("fr", "Le théorème de Pythagore s'applique aux triangles rectangles."),
    ("fr", "L'espace vectoriel a une structure algébrique riche."),
    ("fr", "La convergence d'une série peut être difficile à prouver."),
    ("fr", "Les fonctions trigonométriques sont périodiques."),
    ("fr", "La démonstration par récurrence comporte deux étapes."),
    ("fr", "Le calcul différentiel étudie les taux de variation."),
    ("en", "The scalar product of two orthogonal vectors is zero."),
    ("en", "The square root of a negative number does not exist in R."),
    ("en", "Integration by parts is a useful technique."),
    ("en", "The derivative of a constant is always zero."),
    ("en", "The Pythagorean theorem applies to right triangles."),
    ("en", "A vector space has a rich algebraic structure."),
    ("en", "Convergence of a series can be hard to prove."),
    ("en", "Trigonometric functions are periodic."),
    ("en", "Proof by induction has two steps."),
    ("en", "Differential calculus studies rates of change."),
]


# Phrases où "pas", "c", "k", etc. risquent d'être pris pour un token math
FR_AMBIGUOUS_TOKENS = [
    "Je n'ai pas compris, c'est flou.",
    "Pas de chance aujourd'hui, vraiment.",
    "Je prends pas le métro, je préfère marcher.",
    "C'est pas grave.",
    "K-way, le vêtement de pluie.",
    "C'est la vie quoi.",
    "Pas du tout d'accord avec lui.",
    "Pas 1 mais plusieurs raisons.",
    "Pas 2 sans 3, comme on dit.",
]

EN_AMBIGUOUS_TOKENS = [
    "I don't get it, c'est la vie as they say.",
    "Not 1 but many reasons.",
    "It's not that bad honestly.",
    "He's a K-pop fan.",
]


def main():
    examples = []

    for text in FR_SUBSTRING_TRAPS:
        examples.append({"text": text, "spans": [], "lang": "fr"})
    for text in EN_SUBSTRING_TRAPS:
        examples.append({"text": text, "spans": [], "lang": "en"})

    for text in FR_LETTER_IN_PROSE:
        examples.append({"text": text, "spans": [], "lang": "fr"})
    for text in EN_LETTER_IN_PROSE:
        examples.append({"text": text, "spans": [], "lang": "en"})

    for lang, text in PROSE_TRAPS:
        examples.append({"text": text, "spans": [], "lang": lang})

    for text in FR_AMBIGUOUS_TOKENS:
        examples.append({"text": text, "spans": [], "lang": "fr"})
    for text in EN_AMBIGUOUS_TOKENS:
        examples.append({"text": text, "spans": [], "lang": "en"})

    random.shuffle(examples)

    DST.parent.mkdir(parents=True, exist_ok=True)
    with DST.open("w", encoding="utf-8") as f:
        for ex in examples:
            f.write(json.dumps(ex, ensure_ascii=False) + "\n")

    print(f"\nGénérés : {len(examples)} lignes")
    print(f"  FR / EN : {sum(1 for e in examples if e['lang']=='fr')} / {sum(1 for e in examples if e['lang']=='en')}")
    print(f"  tous spans=[] (contre-exemples)")
    print(f"\nÉcrit : {DST.relative_to(REPO)}")


if __name__ == "__main__":
    main()
