# -*- coding: utf-8 -*-
"""Raffinage des zones NER — port pur de `ZoneRefiner.cs` + `NerInputWindow.cs`.

Fonctions pures (pas d'UNO, pas de state), testables hors LibreOffice."""
from .zone import DetectedZone

# Mots dont la zone juste à droite est étendue en arrière (port verbatim de
# ZoneRefiner.DefaultMathPrefixKeywords). Comparaison insensible à la casse.
DEFAULT_MATH_PREFIX_KEYWORDS = frozenset(w.lower() for w in (
    "lim", "limite", "lmt",
    "sqrt", "rac", "racine", "racn", "root",
    "int", "iint", "iiint", "integrale", "integ", "integral",
    "sum", "somme", "som", "prod", "produit",
    "forall", "qq", "qqe", "pourtout",
    "exists", "existe", "ilexiste",
    "vec", "vect", "vecteur",
    "norm", "abs", "module", "conj",
    "cos", "sin", "tan", "ln", "log", "exp",
    "angle", "pgcd", "ppcm", "binom",
    "floor", "ceil",
    "alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta",
    "theta", "iota", "kappa", "lambda", "mu", "nu", "xi",
    "omicron", "pi", "rho", "sigma", "tau", "upsilon", "phi",
    "chi", "psi", "omega",
))


def compute_window(paragraph, omath_regions, caret):
    """Fenêtre NER autour du caret : texte entre l'OLE voisine de gauche (ou
    début ¶) et celle de droite (ou fin ¶). Renvoie (left_cut, input)."""
    if paragraph is None:
        return 0, ""
    left_cut = 0
    right_cut = len(paragraph)
    for s, e in (omath_regions or []):
        if e <= caret and e > left_cut:
            left_cut = e
        if s >= caret and s < right_cut:
            right_cut = s
    if right_cut < left_cut:
        right_cut = left_cut
    return left_cut, paragraph[left_cut:right_cut]


def filter_omath_overlap(zones, regions):
    """Jette les zones qui chevauchent une région OLE (déjà converties)."""
    if not zones or not regions:
        return list(zones or [])
    kept = []
    for z in zones:
        if not any(z.end > s and z.start < e for s, e in regions):
            kept.append(z)
    return kept


def pick_nearest(zones, caret):
    """Zone la plus proche du caret (distance 0 si dedans/au bord)."""
    best = None
    best_dist = None
    for z in zones:
        if z.start <= caret <= z.end:
            dist = 0
        elif caret < z.start:
            dist = z.start - caret
        else:
            dist = caret - z.end
        if best_dist is None or dist < best_dist:
            best_dist = dist
            best = z
    return best, (best_dist if best_dist is not None else None)


def _is_ws_gap(text, frm, to, max_gap):
    if to < frm or to - frm > max_gap:
        return False
    return all(text[i].isspace() for i in range(frm, min(to, len(text))))


def merge_whitespace_adjacent(zones, text, pivot, max_gap=3):
    """Fusionne les zones adjacentes séparées seulement par des blancs (≤ gap)
    autour du pivot. Le NER fragmente parfois une formule en deux."""
    if not zones or pivot is None or not text:
        return pivot
    start, end = pivot.start, pivot.end
    grew = True
    while grew:
        grew = False
        for z in zones:
            if z.end <= start and _is_ws_gap(text, z.end, start, max_gap) and z.start < start:
                start = z.start
                grew = True
            elif z.start >= end and _is_ws_gap(text, end, z.start, max_gap) and z.end > end:
                end = z.end
                grew = True
    if start == pivot.start and end == pivot.end:
        return pivot
    end = min(end, len(text))
    return DetectedZone(start, end, text[start:end], pivot.confidence)


def try_extend_forward_whitespace(paragraph, zone, caret):
    """Si le caret suit la zone avec uniquement des blancs entre, pousse end
    jusqu'au caret (l'user a tapé un espace pour continuer)."""
    if zone is None or not paragraph or caret <= zone.end:
        return zone
    if caret - zone.end > 5:
        return zone
    for i in range(zone.end, min(caret, len(paragraph))):
        if not paragraph[i].isspace():
            return zone
    new_end = min(caret, len(paragraph))
    return DetectedZone(zone.start, new_end, paragraph[zone.start:new_end], zone.confidence)


def extend_backward_keyword(paragraph, zone, keywords=DEFAULT_MATH_PREFIX_KEYWORDS):
    """Si le mot juste avant la zone est un mot-clé math (lim, racine, pi…),
    l'inclut dans la zone."""
    if not paragraph or zone is None:
        return zone
    i = zone.start
    while i > 0 and paragraph[i - 1].isspace():
        i -= 1
    word_end = i
    while i > 0 and paragraph[i - 1].isalpha():
        i -= 1
    word_start = i
    if word_end <= word_start:
        return zone
    prev = paragraph[word_start:word_end]
    if prev.lower() not in keywords:
        return zone
    return DetectedZone(word_start, zone.end, paragraph[word_start:zone.end], zone.confidence)
