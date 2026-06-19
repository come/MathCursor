"""
Génère des exemples pour les SYNTAXES DE SAISIE MOTEUR RÉCENTES, absentes du
corpus NER (sondes 2026-06-19 : 0 occurrence dans data/ner-corpus/*.jsonl) :

 1. RACCOURCI GREC `@` (`@a`→α, `@p`→π/φ/ψ, `@theta`→θ, `@D`→Δ, `2@p`, `@a+@b`).
    Le NER n'a JAMAIS vu de `@` → il ne détecte pas la saisie grecque rapide.
 2. FRACTIONS VULGAIRES `½ ¾ ⅓ ⅔ ¼` (release 0.11.0 — `½`→\frac{1}{2}).
 3. PUISSANCE `**` (`x**2`→x², convention clavier — backlog moteur #1).

⚠ PIÈGE FAUX POSITIFS — ces glyphes ont des homonymes NON-math très fréquents :
 - `@` : handles (`@marie`), emails (`jean@exemple.fr`), mentions (`@everyone`).
 - `**` : gras markdown/chat (`**important**`).
 → distractors massifs (spans=[]) pour que le modèle apprenne la frontière :
   `@`+lettre(grecque) en contexte math = MATH ; `@`+mot/handle = prose.

Les lettres grecques EN TOUTES LETTRES (alpha, pi, theta…) sont déjà bien
couvertes (pi 599, alpha 320 occ.) — inutile de les régénérer. C'est la FORME
`@` qui manque.

Même pattern que build_v8/v9 (formules isolées + prose + distractors), seed
dédiée. Sortie : data/ner-corpus/extension_v10_recent_syntaxes.jsonl
"""

import json
import random
import sys
from pathlib import Path

try:
    sys.stdout.reconfigure(encoding="utf-8")  # stats impriment ½/≈/@ → utf-8
except Exception:
    pass

random.seed(20260620)

REPO = Path(__file__).resolve().parents[2]
DST = REPO / "data" / "ner-corpus" / "extension_v10_recent_syntaxes.jsonl"


# ============================================================================
# 1. RACCOURCI GREC `@`
# ============================================================================

# Formes nues (minuscules, majuscules, noms complets) — l'utilisateur tape `@x`
# et attend la lettre grecque ; la zone math = la forme entière.
AT_SINGLE = ["@a", "@b", "@g", "@d", "@e", "@h", "@t", "@k", "@l", "@m",
             "@n", "@x", "@o", "@p", "@r", "@s", "@u", "@c", "@w", "@f"]
AT_CAPS = ["@A", "@B", "@G", "@D", "@T", "@P", "@O", "@L", "@S", "@F", "@W"]
AT_NAMES = ["@alpha", "@beta", "@theta", "@pi", "@lambda", "@omega", "@phi",
            "@varphi", "@delta", "@gamma", "@mu", "@sigma", "@tau", "@psi"]

# `@` en CONTEXTE (le cas réaliste de frappe — combinaisons et opérations).
AT_CONTEXT = [
    "2@p", "3@a", "@a+@b", "@a-@b", "@a^2", "@p^2", "@a_n", "1/@a",
    "1/@a+1", "cos @t", "sin @t", "@l/2", "f(@a)", "@a = @b", "@a@b",
    "sum k 1 n @a_k", "lim @e 0", "@D x", "@a x + @b", "r@e^(i@t)",
    "@a in R", "@t = @p/2", "2@p r", "@a^2 + @b^2", "v(@a)", "@p/4",
    "exp(i@t)", "@s^2", "@m + @s", "x @a y",
]


# ============================================================================
# 2. FRACTIONS VULGAIRES
# ============================================================================

VULGAR = [
    "½", "¾", "⅓", "⅔", "¼", "⅕", "⅛",
    "2½", "3¾", "½x", "¾y", "x=½", "½+¼", "½ + ¼", "½+½", "1-¾",
    "½ x + ¼", "f(x)=½", "½n", "a½",
]


# ============================================================================
# 3. PUISSANCE `**`
# ============================================================================

POWER = [
    "x**2", "a**b", "2**10", "(x+1)**2", "x**2+1", "x**-1", "e**x",
    "n**2", "2**n", "x**2 + y**2", "10**3", "(a+b)**2", "x**3-1",
    "cos(x)**2", "@a**2",
]


# ============================================================================
# TEMPLATES PROSE (zone = formule complète)
# ============================================================================

FR_TEMPLATES = [
    "On pose {F}.", "On a {F}.", "Soit {F}.", "On note {F}.",
    "Calculer {F}.", "On considère {F}.", "Le résultat vaut {F}.",
    "D'après le cours, {F}.",
]
EN_TEMPLATES = [
    "We set {F}.", "We have {F}.", "Let {F}.", "Consider {F}.",
    "Compute {F}.", "The result is {F}.",
]


# ============================================================================
# DISTRACTORS — homonymes NON-math (CRITIQUE pour la précision)
# ============================================================================

# `@` = handles / emails / mentions → JAMAIS math.
AT_DISTRACTORS_FR = [
    "Contactez @marie pour le projet.",
    "Envoyez un mail à jean@exemple.fr.",
    "Mentionne @tout-le-monde dans le canal.",
    "Mon pseudo est @lucas2010.",
    "@admin a fermé le ticket hier.",
    "Suivez @mathcursor sur le réseau.",
    "Écris à support@ecole.fr si besoin.",
    "Réponds à @paul avant midi.",
]
AT_DISTRACTORS_EN = [
    "Email me at john@example.com.",
    "Ping @everyone in the channel.",
    "Follow @openai for updates.",
    "Her handle is @sarah_codes.",
    "Send it to admin@school.org.",
]

# `**` = gras markdown / emphase → JAMAIS math.
STAR_DISTRACTORS_FR = [
    "C'est **très** important à retenir.",
    "Le mot **clé** est en gras.",
    "**Attention** au piège de l'énoncé.",
    "Mets **la définition** en évidence.",
]
STAR_DISTRACTORS_EN = [
    "Use **bold** for emphasis.",
    "This is **really** important.",
    "**Note** the exception here.",
]


# ============================================================================
# UTILITAIRES / GÉNÉRATEURS (pattern build_v8/v9)
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


def generate_positives() -> list[dict]:
    examples = []

    # @ : formes nues (le cas frappe directe) — léger sous-échantillon de la
    # prose (la forme isolée est le cas dominant dans Word).
    for expr in AT_SINGLE + AT_CAPS + AT_NAMES:
        examples.append(gen_formula_only(expr, "fr"))
        if random.random() < 0.25:
            examples.append(gen_prose(expr, "fr"))

    # @ : en contexte (combinaisons / opérations) — le plus instructif.
    for expr in AT_CONTEXT:
        examples.append(gen_formula_only(expr, "fr"))
        examples.append(gen_prose(expr, "fr"))
        if random.random() < 0.3:
            examples.append(gen_prose(expr, "en"))

    # Fractions vulgaires.
    for expr in VULGAR:
        examples.append(gen_formula_only(expr, "fr"))
        if random.random() < 0.4:
            examples.append(gen_prose(expr, "fr"))

    # Puissance **.
    for expr in POWER:
        examples.append(gen_formula_only(expr, "fr"))
        if random.random() < 0.4:
            examples.append(gen_prose(expr, "fr"))
        if random.random() < 0.2:
            examples.append(gen_prose(expr, "en"))

    return examples


def generate_distractors() -> list[dict]:
    examples = []
    for text in AT_DISTRACTORS_FR + STAR_DISTRACTORS_FR:
        examples.append({"text": text, "spans": [], "lang": "fr"})
    for text in AT_DISTRACTORS_EN + STAR_DISTRACTORS_EN:
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

    print("\nCouverture par famille (span contenant le motif) :")
    fam = {"@": "@", "**": "**", "½/¾/⅓": ("½", "¾", "⅓", "⅔", "¼")}
    for label, pat in fam.items():
        pats = pat if isinstance(pat, tuple) else (pat,)
        count = sum(
            1
            for e in examples
            for s in e["spans"]
            if any(p in e["text"][s["start"]:s["end"]] for p in pats)
        )
        print(f"  {label:<8} {count}")
    print(f"  distractors @ / ** : {sum(1 for e in examples if not e['spans'])}")


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
