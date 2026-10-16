// MathCursor: capturing mathematical intent from linear keyboard input.
// Copyright (C) 2026  Côme de Percin
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

//! Mode chimie — port de ChemFormula.cs (ADR 2026-10-16-Feat-physics-chemistry-mode).
//!
//! Reconnaît une formule chimique bornée (H2O, Fe2+, Ca(OH)2, SO42-, Na+(aq)) par
//! GRAMMAIRE sur la table des éléments (data/engine/physchem.json). La formule
//! devient UN atome portant ses lectures : parser, score et rendu ne la voient pas.
//!   formule := groupe+ charge? état?
//!   groupe  := Élément indice? | '(' groupe+ ')' indice?      indice := '_'? chiffres
//!   charge  := '^'? chiffres? (+|-)   suivie d'une frontière   état := (aq|s|l|g)

use crate::reg;

#[derive(Clone, Copy, PartialEq)]
enum Kind {
    El,
    Num,
    Open,
    Close,
}

fn ch(src: &[char], idx: i64) -> char {
    if idx >= 0 && (idx as usize) < src.len() {
        src[idx as usize]
    } else {
        '\0'
    }
}

fn is_upper(c: char) -> bool {
    c.is_ascii_uppercase()
}

fn is_lower(c: char) -> bool {
    c.is_ascii_lowercase()
}

fn is_digit(c: char) -> bool {
    c.is_ascii_digit()
}

/// Formule commençant en `i` : longueur consommée (0 = pas une formule) et
/// lectures LaTeX, la première étant la lecture par défaut.
pub fn scan(src: &[char], i: usize) -> (usize, Vec<String>) {
    let r = reg();
    let at = |k: usize| ch(src, k as i64);
    let before = ch(src, i as i64 - 1);
    // début : un élément, ou un groupe parenthésé indicé ((NH4)2SO4, vérifié plus bas)
    let paren_start = at(i) == '(' && is_upper(at(i + 1));
    if !(is_upper(at(i)) || paren_start) || is_lower(before) || is_upper(before) || before == '\\' {
        return (0, vec![]);
    }

    let mut items: Vec<(Kind, String)> = Vec::new();
    let (mut p, mut depth) = (i, 0i32);
    while p < src.len() {
        let c = src[p];
        let last = items.last().map(|t| t.0);
        if is_upper(c) {
            let two: String = [c, at(p + 1)].iter().collect();
            let one = c.to_string();
            let el = if is_lower(at(p + 1)) && r.elements.contains(&two) {
                two
            } else if r.elements.contains(&one) {
                one
            } else {
                return (0, vec![]);
            };
            p += el.chars().count();
            items.push((Kind::El, el));
        } else if (is_digit(c) || (c == '_' && is_digit(at(p + 1))))
            && matches!(last, Some(Kind::El) | Some(Kind::Close))
        {
            if c == '_' {
                p += 1;
            }
            let st = p;
            while is_digit(at(p)) {
                p += 1;
            }
            items.push((Kind::Num, src[st..p].iter().collect()));
        } else if c == '(' && is_upper(at(p + 1)) {
            items.push((Kind::Open, "(".into()));
            depth += 1;
            p += 1;
        } else if c == ')' && depth > 0 {
            items.push((Kind::Close, ")".into()));
            depth -= 1;
            p += 1;
        } else {
            break;
        }
    }
    if depth != 0 || items.is_empty() {
        return (0, vec![]);
    }
    // groupe de tête : seulement s'il porte un indice ((NH4)2), sinon (BC), (AB)
    // restent de la géométrie
    let mut group_only = false; // la formule n'est que le groupe indicé : (BC)2
    if paren_start {
        let mut d = 0i32;
        let mut close = 0usize;
        while close < items.len() {
            d += match items[close].0 {
                Kind::Open => 1,
                Kind::Close => -1,
                _ => 0,
            };
            if d == 0 {
                break;
            }
            close += 1;
        }
        if close + 1 >= items.len() || items[close + 1].0 != Kind::Num {
            return (0, vec![]);
        }
        group_only = close + 2 == items.len();
    }

    // charge : signe final. Sans « ^ », ses chiffres ont déjà été lus en
    // indice (ambigus) ; avec « ^ » (Fe^2+), ils sont explicitement la charge.
    let mut hat = at(p) == '^';
    let mut q = if hat { p + 1 } else { p };
    while hat && is_digit(at(q)) {
        q += 1;
    }
    let mut charge: Option<String> = None;
    if "+-".contains(at(q)) && at(q) != '\0' && charge_end(src, q + 1) {
        let digits: String = if hat { src[p + 1..q].iter().collect() } else { String::new() };
        charge = Some(format!("{}{}", digits, at(q)));
        p = q + 1;
    } else {
        hat = false;
    }
    let state = state_at(src, p, !items.is_empty());
    if let Some(s) = &state {
        p += s.chars().count() + 2;
    }
    if at(p).is_alphabetic() || is_digit(at(p)) {
        return (0, vec![]);
    }

    let bare = items.len() == 1;
    let single_letters = items.iter().all(|t| t.0 != Kind::El || t.1.chars().count() == 1);
    let digits = items.iter().any(|t| t.0 == Kind::Num);
    let elements = items.iter().filter(|t| t.0 == Kind::El).count();
    // un élément seul sans rien autour reste une variable (C, H, V) ou un mot
    // déjà connu du moteur : unité (2 Pa), fonction (Re(z))
    if bare
        && charge.is_none()
        && state.is_none()
        && (items[0].1.chars().count() == 1 || r.vocab.contains_key(&items[0].1))
    {
        return (0, vec![]);
    }

    let suffix = state.as_ref().map(|s| format!("(\\mathrm{{{}}})", s)).unwrap_or_default();
    let mut readings: Vec<String> = Vec::new();
    let last_num = items.last().map(|t| t.0 == Kind::Num).unwrap_or(false);
    match &charge {
        Some(cg) if !hat && last_num => {
            // chiffres juste avant le signe : charge ou indice, à égalité (popup)
            let d: Vec<char> = items.last().unwrap().1.chars().collect();
            let head = render(&items[..items.len() - 1]);
            let sub = if d.len() > 1 {
                format!("_{{{}}}", d[..d.len() - 1].iter().collect::<String>())
            } else {
                String::new()
            };
            readings.push(format!("{}{}^{{{}{}}}{}", head, sub, d[d.len() - 1], cg, suffix));
            readings.push(format!("{}^{{{}}}{}", render(&items), cg, suffix));
        }
        _ => {
            let cg = charge.as_ref().map(|c| format!("^{{{}}}", c)).unwrap_or_default();
            readings.push(format!("{}{}{}", render(&items), cg, suffix));
            // lecture maths gardée en alternative quand la formule y ressemble :
            // lettres seules (BC, UI → maths d'abord), une lettre indicée (V2, O2) ou un
            // groupe de tête de lettres seules ((BC)2 : longueur au carré)
            if charge.is_none() && state.is_none() && single_letters && (!digits || elements == 1 || group_only) {
                let math: String = items
                    .iter()
                    .map(|t| if t.0 == Kind::Num { format!("^{{{}}}", t.1) } else { t.1.clone() })
                    .collect();
                if digits {
                    readings.push(math);
                } else {
                    readings.insert(0, math);
                }
            }
        }
    }
    (p - i, readings)
}

fn render(items: &[(Kind, String)]) -> String {
    items
        .iter()
        .map(|(k, s)| match k {
            Kind::El => format!("\\mathrm{{{}}}", s),
            Kind::Num => format!("_{{{}}}", s),
            _ => s.clone(),
        })
        .collect()
}

// après une charge : fin, espace, fermant, séparateur, « = » ou état
pub(crate) fn charge_end(src: &[char], p: usize) -> bool {
    let c = ch(src, p as i64);
    c == '\0' || c == ' ' || c == '\u{00A0}' || c == '\u{202F}' || ")],;=".contains(c) || state_at(src, p, true).is_some()
}

fn state_at(src: &[char], p: usize, any: bool) -> Option<String> {
    if !any || ch(src, p as i64) != '(' {
        return None;
    }
    for s in &reg().states {
        let n = s.chars().count();
        if p + n + 1 < src.len() && src[p + 1..p + 1 + n].iter().copied().eq(s.chars()) && src[p + n + 1] == ')' {
            return Some(s.clone());
        }
    }
    None
}
