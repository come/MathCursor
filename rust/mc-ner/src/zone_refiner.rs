// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

//! Raffinage des zones NER — port pur de ZoneRefiner.cs.
//!
//! Opère sur des UNITÉS UTF-16 (`&[u16]`) + offsets UTF-16, pour coïncider avec
//! les positions de l'hôte (VSCode Range / C# char). Classification de caractère
//! sur l'unité décodée (BMP) — les demi-surrogates (astraux, ~absents en maths)
//! ne sont ni blancs ni lettres, comportement sûr.

use crate::DetectedZone;

fn is_ws(u: u16) -> bool {
    char::from_u32(u as u32).map(|c| c.is_whitespace()).unwrap_or(false)
}
fn is_letter(u: u16) -> bool {
    char::from_u32(u as u32).map(|c| c.is_alphabetic()).unwrap_or(false)
}

/// Zone la plus proche du caret (distance 0 si dedans/au bord).
pub fn pick_nearest_zone(zones: &[DetectedZone], caret: usize) -> Option<DetectedZone> {
    let mut best: Option<&DetectedZone> = None;
    let mut best_dist = usize::MAX;
    for z in zones {
        let dist = if caret >= z.start && caret <= z.end {
            0
        } else if caret < z.start {
            z.start - caret
        } else {
            caret - z.end
        };
        if dist < best_dist {
            best_dist = dist;
            best = Some(z);
        }
    }
    best.cloned()
}

fn is_ws_gap(units: &[u16], from: usize, to: usize, max_gap: usize) -> bool {
    if to < from || to - from > max_gap {
        return false;
    }
    for i in from..to {
        if i < units.len() && !is_ws(units[i]) {
            return false;
        }
    }
    true
}

/// Fusionne les zones adjacentes séparées uniquement par des blancs (le NER
/// fragmente parfois une formule). Retourne le pivot tel quel si rien à fusionner.
pub fn merge_whitespace_adjacent(
    zones: &[DetectedZone],
    units: &[u16],
    pivot: DetectedZone,
    max_gap: usize,
) -> DetectedZone {
    let mut start = pivot.start;
    let mut end = pivot.end;
    let mut grew = true;
    while grew {
        grew = false;
        for z in zones {
            if z.end <= start && is_ws_gap(units, z.end, start, max_gap) && z.start < start {
                start = z.start;
                grew = true;
            } else if z.start >= end && is_ws_gap(units, end, z.start, max_gap) && z.end > end {
                end = z.end;
                grew = true;
            }
        }
    }
    if start == pivot.start && end == pivot.end {
        return pivot;
    }
    DetectedZone { start, end: end.min(units.len()), confidence: pivot.confidence }
}

/// Si le caret suit la zone avec UNIQUEMENT du blanc entre (≤ 5), pousse la fin
/// jusqu'au caret (l'user a tapé un espace pour continuer la formule).
pub fn try_extend_forward_whitespace(units: &[u16], zone: DetectedZone, caret: usize) -> DetectedZone {
    if caret <= zone.end {
        return zone;
    }
    if caret - zone.end > 5 {
        return zone;
    }
    for i in zone.end..caret {
        if i < units.len() && !is_ws(units[i]) {
            return zone;
        }
    }
    DetectedZone { start: zone.start, end: caret.min(units.len()), confidence: zone.confidence }
}

/// Si le mot juste avant la zone est un mot-clé math (lim/racine/somme…), l'inclut.
pub fn extend_backward_with_keyword(units: &[u16], zone: DetectedZone) -> DetectedZone {
    let mut i = zone.start;
    while i > 0 && is_ws(units[i - 1]) {
        i -= 1;
    }
    let word_end = i;
    while i > 0 && is_letter(units[i - 1]) {
        i -= 1;
    }
    let word_start = i;
    if word_end <= word_start {
        return zone;
    }
    let word: String = char::decode_utf16(units[word_start..word_end].iter().copied())
        .map(|r| r.unwrap_or('\u{FFFD}'))
        .collect();
    if !MATH_PREFIX_KEYWORDS.contains(&word.to_lowercase().as_str()) {
        return zone;
    }
    DetectedZone { start: word_start, end: zone.end, confidence: zone.confidence }
}

/// Mots-clés dont la zone détectée juste à droite absorbe le mot (table portée
/// de ZoneRefiner.DefaultMathPrefixKeywords, comparaison insensible à la casse).
const MATH_PREFIX_KEYWORDS: &[&str] = &[
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
];
