// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
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

//! chars → tokens — port de Lexer.cs / lexer.py.
//!
//! Générique : consulte le VOCABULAIRE (data) pour savoir si un lexème est
//! infixe/préfixe/n-aire. Ne nomme aucun opérateur. Indexation par CARACTÈRE
//! (Unicode) : on travaille sur un `&[char]` comme le Python sur `str`.

use crate::culture::Culture;
use crate::node::Token;
use crate::reg;

const SPLIT_CAP: usize = 4; // <= 2^4 flux ; au-delà, on découpe tout (un seul flux)

fn is_alpha(c: char) -> bool {
    ('a'..='z').contains(&c) || ('A'..='Z').contains(&c)
}
fn is_digit(c: char) -> bool {
    ('0'..='9').contains(&c)
}
fn is_upper(c: char) -> bool {
    ('A'..='Z').contains(&c)
}
// autocorrection FR de Word : U+00A0 / U+202F = espaces pour le lexer.
fn is_space(c: char) -> bool {
    c == ' ' || c == '\u{00A0}' || c == '\u{202F}'
}

fn ch(src: &[char], idx: i64) -> char {
    if idx >= 0 && (idx as usize) < src.len() {
        src[idx as usize]
    } else {
        '\0'
    }
}

fn starts_with_at(src: &[char], pat: &str, at: usize) -> bool {
    let mut idx = at;
    for pc in pat.chars() {
        if idx >= src.len() || src[idx] != pc {
            return false;
        }
        idx += 1;
    }
    true
}

struct Join {
    left: &'static [&'static str],
    right: &'static [&'static str],
    roles: &'static [&'static str],
    sticky: bool,
    cross: bool,
}

// jonction de tokens collés (JOIN) — premier match gagne.
const JOIN: &[Join] = &[
    Join { left: &["name"], right: &["num"], roles: &["sup"], sticky: true, cross: false },
    Join { left: &["close"], right: &["num"], roles: &["sup"], sticky: true, cross: false },
    Join { left: &["unit"], right: &["num"], roles: &["sup"], sticky: true, cross: false },
    Join { left: &["num"], right: &["unit"], roles: &["unitOp"], sticky: true, cross: true },
    Join { left: &["name", "unit", "post"], right: &["open"], roles: &["apply"], sticky: false, cross: false },
    Join {
        left: &["num", "name", "close", "post"],
        right: &["name", "open", "func"],
        roles: &["implicit"],
        sticky: false,
        cross: false,
    },
];

struct Lex<'a> {
    src: &'a [char],
    cu: &'a Culture,
    toks: Vec<Token>,
    saw_space: bool,
    i: usize,
    choices: Option<&'a dyn Fn(usize) -> bool>,
    runs: Option<&'a mut Vec<usize>>,
}

fn best_splittable_at(s: &str, at: usize) -> String {
    let mut best = String::new();
    let sub = &s[at..];
    for key in &reg().splittable {
        if key.len() > best.len() && sub.starts_with(key.as_str()) {
            best = key.clone();
        }
    }
    best
}

fn decompose(s: &str) -> Vec<String> {
    let chars: Vec<char> = s.chars().collect();
    let n = chars.len();
    let mut out: Vec<String> = Vec::new();
    let mut j = 0;
    while j < n {
        let mut best = best_splittable_at(s, j);
        if best.is_empty() && j == 0 && is_upper(chars[0]) && n >= 2 {
            let mut lowered = String::new();
            lowered.push(chars[0].to_ascii_lowercase());
            lowered.extend(chars[1..].iter());
            best = best_splittable_at(&lowered, 0);
        }
        if !best.is_empty() {
            let bl = best.chars().count();
            out.push(best);
            j += bl;
        } else if is_upper(chars[j]) {
            let st = j;
            while j < n && is_upper(chars[j]) {
                j += 1;
            }
            out.push(chars[st..j].iter().collect());
        } else {
            out.push(chars[j].to_string());
            j += 1;
        }
    }
    out
}

impl<'a> Lex<'a> {
    fn spaced(&self, a: i64, b: i64) -> bool {
        is_space(ch(self.src, a)) || is_space(ch(self.src, b))
    }

    fn push(&mut self, mut t: Token) {
        t.space_before = self.saw_space;
        self.toks.push(t);
        self.saw_space = false;
    }

    fn word(&mut self, w: &str, sp: bool) {
        let cu = self.cu;
        let r = reg();
        let k0 = cu.canon(w);
        let v0 = r.vocab.get(&k0);
        let wlow = w.to_ascii_lowercase();

        let v0_atom = v0.map(|e| e.shape.as_deref() == Some("atom")).unwrap_or(false);
        let g = if v0_atom { v0 } else { r.vocab.get(&wlow) };
        let g_atom = g.map(|e| e.shape.as_deref() == Some("atom")).unwrap_or(false);

        // repli casse (Word AutoCorrect « Sum » -> sum) : mots-clés non-atomes >= 2.
        let mut k = k0.clone();
        let mut v = v0;
        if v0.is_none() && !g_atom && w.chars().count() >= 2 {
            let k2 = cu.canon(&wlow);
            let v2 = r.vocab.get(&k2);
            let v2_kw = v2
                .map(|e| e.shape.is_some() && e.shape.as_deref() != Some("atom"))
                .unwrap_or(false);
            if k2 != k0 && v2_kw {
                k = k2;
                v = v2;
            }
        }

        let first_upper = w.chars().next().map(is_upper).unwrap_or(false);

        if let Some(ge) = g {
            if ge.shape.as_deref() == Some("atom") && ge.alts.is_some() {
                self.push(Token {
                    kind: "atom".into(),
                    syms: ge.alts.clone(),
                    coh: ge.coh.clone(),
                    ..Default::default()
                });
                return;
            }
            if ge.shape.as_deref() == Some("atom") {
                let sym = if first_upper { ge.upper.clone() } else { ge.lower.clone() };
                self.push(Token { kind: "atom".into(), sym, ..Default::default() });
                return;
            }
        }

        if let Some(ve) = v {
            if ve.shape.as_deref() == Some("infix") {
                let binary = self
                    .toks
                    .last()
                    .map(|p| matches!(p.kind.as_str(), "atom" | "rparen" | "bracket" | "bar" | "postfix"))
                    .unwrap_or(false);
                if binary {
                    self.push(Token { kind: "infix".into(), sym: Some(k), spaced: sp, ..Default::default() });
                } else if ve.unary.is_some() {
                    self.push(Token { kind: "prefix".into(), sym: ve.unary.clone(), spaced: sp, ..Default::default() });
                } else if ve.cut {
                    // relation-MOT sans opérande gauche : reste INFIXE (tête-de-relation
                    // gérée par la couche chaîne / erreur), comme « = ≤ < ».
                    self.push(Token { kind: "infix".into(), sym: Some(k), spaced: sp, ..Default::default() });
                } else {
                    self.push(Token { kind: "atom".into(), sym: Some(w.to_string()), ..Default::default() });
                }
                return;
            }
            if ve.word_space && !is_space(ch(self.src, self.i as i64)) {
                self.push(Token { kind: "atom".into(), sym: Some(w.to_string()), ..Default::default() });
                return;
            }
            if ve.shape.is_some() {
                self.push(Token { kind: ve.shape.clone().unwrap(), sym: Some(k), spaced: sp, ..Default::default() });
                return;
            }
            if ve.unit_word {
                self.push(Token { kind: "atom".into(), sym: Some(w.to_string()), unit_word: true, ..Default::default() });
                return;
            }
        }
        self.push(Token { kind: "atom".into(), sym: Some(w.to_string()), ..Default::default() });
    }

    fn run(mut self) -> Vec<Token> {
        let src = self.src;
        let cu = self.cu;
        let r = reg();
        let n = src.len();
        let mut i = 0usize;
        let mut brk = 0i32;
        let mut brc = 0i32;

        'main: while i < n {
            let c = src[i];
            if is_space(c) {
                self.saw_space = true;
                i += 1;
                continue;
            }
            if c == '\\' && is_alpha(ch(src, i as i64 + 1)) {
                i += 1;
                continue;
            }
            if c == '(' {
                self.push(Token { kind: "lparen".into(), ..Default::default() });
                i += 1;
                continue;
            }
            if c == ')' {
                self.push(Token { kind: "rparen".into(), ..Default::default() });
                i += 1;
                continue;
            }
            if c == '[' || c == ']' {
                self.push(Token { kind: "bracket".into(), sym: Some(c.to_string()), ..Default::default() });
                brk += 1;
                i += 1;
                continue;
            }
            if c == '{' {
                self.push(Token { kind: "lbrace".into(), ..Default::default() });
                brc += 1;
                i += 1;
                continue;
            }
            if c == '}' {
                self.push(Token { kind: "rbrace".into(), ..Default::default() });
                brc -= 1;
                i += 1;
                continue;
            }
            if c == '|' {
                let norm = ch(src, i as i64 + 1) == '|';
                self.push(Token {
                    kind: "bar".into(),
                    sym: Some(if norm { "norm".into() } else { "abs".into() }),
                    ..Default::default()
                });
                i += if norm { 2 } else { 1 };
                continue;
            }
            if let Some(&rank) = r.sep.get(&c) {
                self.push(Token { kind: "sep".into(), sym: Some(c.to_string()), rank, ..Default::default() });
                i += 1;
                continue;
            }

            // raccourcis grecs « @ » + lettre(s)
            if c == '@' && is_alpha(ch(src, i as i64 + 1)) {
                let at0 = i as i64;
                i += 1;
                let mut gr = String::new();
                while i < n && is_alpha(src[i]) {
                    gr.push(src[i]);
                    i += 1;
                }
                let gsp = self.spaced(at0 - 1, i as i64);
                if gr.chars().count() == 1 {
                    let g0 = gr.chars().next().unwrap();
                    let up = is_upper(g0);
                    let low = format!("@{}", g0.to_ascii_lowercase());
                    let key = cu.canon(&low);
                    if key != low {
                        let ve = &r.vocab[&key];
                        if ve.alts.is_some() {
                            let alts = if up { ve.alts_upper.clone() } else { ve.alts.clone() };
                            if let Some(a) = alts {
                                self.push(Token { kind: "atom".into(), syms: Some(a), ..Default::default() });
                                continue 'main;
                            }
                        } else if !up || ve.upper != ve.lower {
                            let sym = if up { ve.upper.clone() } else { ve.lower.clone() };
                            self.push(Token { kind: "atom".into(), sym, ..Default::default() });
                            continue 'main;
                        }
                    }
                }
                self.i = i;
                self.word(&gr, gsp);
                continue 'main;
            }

            // unité COMPOSÉE juste après un nombre (5 m/s)
            let prev_num = self.toks.last().map(|t| t.num).unwrap_or(false);
            if prev_num {
                let mut best = String::new();
                for key in r.units_compound.keys() {
                    if key.chars().count() > best.chars().count() && starts_with_at(src, key, i) {
                        best = key.clone();
                    }
                }
                if !best.is_empty() {
                    let bl = best.chars().count();
                    self.push(Token {
                        kind: "atom".into(),
                        sym: Some(r.units_compound[&best].clone()),
                        unit_word: true,
                        ..Default::default()
                    });
                    i += bl;
                    continue;
                }
            }

            if is_alpha(c) {
                // run de lettres
                let st = i;
                let mut s = String::new();
                while i < n && is_alpha(src[i]) {
                    s.push(src[i]);
                    i += 1;
                }
                let sp = self.spaced(st as i64 - 1, i as i64);
                let dots_len = if starts_with_at(src, "...", i) {
                    3
                } else if ch(src, i as i64) == '…' {
                    1
                } else {
                    0
                };
                if dots_len > 0 {
                    let slow = s.to_ascii_lowercase();
                    let dv = r
                        .vocab
                        .get(&cu.canon(&format!("{s}...")))
                        .or_else(|| r.vocab.get(&cu.canon(&format!("{slow}..."))));
                    if let Some(dv) = dv {
                        if dv.shape.as_deref() == Some("atom") {
                            self.push(Token { kind: "atom".into(), sym: dv.lower.clone(), ..Default::default() });
                            i += dots_len;
                            continue;
                        }
                    }
                }
                let slow = s.to_ascii_lowercase();
                let known = r.vocab.contains_key(&cu.canon(&s))
                    || r.vocab.get(&slow).map(|e| e.shape.as_deref() == Some("atom")).unwrap_or(false)
                    || (s.chars().count() >= 2 && r.vocab.contains_key(&cu.canon(&slow)));
                if !known {
                    let pieces = decompose(&s);
                    let any_split = pieces.len() >= 2 && pieces.iter().any(|p| r.splittable.contains(p));
                    if any_split {
                        if let Some(runs) = self.runs.as_deref_mut() {
                            runs.push(st);
                        }
                        let take = match &self.choices {
                            Some(f) => f(st),
                            None => true,
                        };
                        if take {
                            self.i = i;
                            for kk in 0..pieces.len() {
                                let piece = pieces[kk].clone();
                                self.word(&piece, kk == 0 && sp);
                            }
                            continue 'main;
                        }
                    }
                }
                self.i = i;
                self.word(&s, sp);
                continue 'main;
            }

            if is_digit(c) {
                // run de chiffres
                let mut s = String::new();
                while i < n && is_digit(src[i]) {
                    s.push(src[i]);
                    i += 1;
                }
                let cn = ch(src, i as i64);
                if cu.decimals_in.contains(&cn)
                    && !(cn == ',' && brc > 0)
                    && is_digit(ch(src, i as i64 + 1))
                {
                    i += 1;
                    s.push_str(&cu.decimal_tex);
                    while i < n && is_digit(src[i]) {
                        s.push(src[i]);
                        i += 1;
                    }
                }
                self.push(Token { kind: "atom".into(), sym: Some(s), num: true, ..Default::default() });
                continue;
            }

            // opérateur infixe / postfixe / symbole-atome : PLUS-LONG-MATCH (3,2,1)
            let mut matched = false;
            for length in [3usize, 2, 1] {
                if i + length > n {
                    continue;
                }
                let op: String = src[i..i + length].iter().collect();
                let v = match r.vocab.get(&op) {
                    Some(v) => v,
                    None => continue,
                };
                let shape = v.shape.as_deref();
                if !matches!(shape, Some("infix") | Some("postfix") | Some("atom")) {
                    continue;
                }
                if shape == Some("atom") {
                    self.push(Token { kind: "atom".into(), sym: v.lower.clone(), ..Default::default() });
                    i += length;
                    matched = true;
                    break;
                }
                if shape == Some("postfix") {
                    self.push(Token { kind: "postfix".into(), sym: Some(op), ..Default::default() });
                    i += length;
                    matched = true;
                    break;
                }
                let binary = self
                    .toks
                    .last()
                    .map(|p| matches!(p.kind.as_str(), "atom" | "rparen" | "bracket" | "bar" | "postfix"))
                    .unwrap_or(false);
                let after = ch(src, (i + length) as i64);
                let detached_r = is_space(after) || matches!(after, ')' | ']' | ',' | ';');
                if length == 1 && v.post_sign.is_some() && binary && !self.saw_space && detached_r {
                    self.push(Token {
                        kind: "infix".into(),
                        sym: Some(r.role["sup"].clone()),
                        sticky: true,
                        sign_sup: true,
                        ..Default::default()
                    });
                    self.push(Token { kind: "atom".into(), sym: v.post_sign.clone(), ..Default::default() });
                } else if length == 1 && v.unary.is_some() && !binary {
                    self.push(Token {
                        kind: "prefix".into(),
                        sym: v.unary.clone(),
                        spaced: self.spaced(i as i64 - 1, i as i64 + 1),
                        ..Default::default()
                    });
                } else if v.alts.is_some() {
                    self.push(Token {
                        kind: "infix".into(),
                        syms: v.alts.clone(),
                        spaced: self.spaced(i as i64 - 1, (i + length) as i64),
                        ..Default::default()
                    });
                } else {
                    self.push(Token {
                        kind: "infix".into(),
                        sym: Some(op),
                        spaced: self.spaced(i as i64 - 1, (i + length) as i64),
                        ..Default::default()
                    });
                }
                i += length;
                matched = true;
                break;
            }
            if matched {
                continue;
            }
            panic!("caractère inattendu: {c}");
        }

        // saisie en cours : fermer VIRTUELLEMENT les ( non fermées
        let mut depth = 0i32;
        for t in &self.toks {
            depth += match t.kind.as_str() {
                "lparen" => 1,
                "rparen" => -1,
                _ => 0,
            };
        }
        while depth > 0 {
            self.toks.push(Token { kind: "rparen".into(), virtual_: true, ..Default::default() });
            depth -= 1;
        }
        if brk % 2 == 1 {
            self.toks
                .push(Token { kind: "bracket".into(), sym: Some("]".into()), virtual_: true, ..Default::default() });
        }

        juxtapose(self.toks)
    }
}

fn cat(t: Option<&Token>) -> Option<&'static str> {
    let t = t?;
    if t.kind == "atom" {
        if t.unit_word {
            return Some("unit");
        }
        let all_digits = t
            .sym
            .as_deref()
            .map(|s| !s.is_empty() && s.chars().all(is_digit))
            .unwrap_or(false);
        return Some(if t.num || all_digits { "num" } else { "name" });
    }
    Some(match t.kind.as_str() {
        "lparen" => "open",
        "rparen" => "close",
        "postfix" => "post",
        "prefix" => "func",
        "nary" => "func",
        _ => "op",
    })
}

fn juxtapose(toks: Vec<Token>) -> Vec<Token> {
    let r = reg();
    let mut out: Vec<Token> = Vec::new();
    for idx in 0..toks.len() {
        let l = &toks[idx];
        let rr = toks.get(idx + 1);
        out.push(l.clone());
        let rtok = match rr {
            Some(t) => t,
            None => continue,
        };
        let lc = cat(Some(l));
        let rc = cat(Some(rtok));
        let mut rule: Option<&Join> = None;
        for j in JOIN {
            let lm = lc.map(|x| j.left.contains(&x)).unwrap_or(false);
            let rm = rc.map(|x| j.right.contains(&x)).unwrap_or(false);
            if lm && rm {
                rule = Some(j);
                break;
            }
        }
        let rule = match rule {
            Some(r) => r,
            None => continue,
        };
        if rtok.space_before && !rule.cross {
            continue;
        }
        let syms: Vec<String> = rule
            .roles
            .iter()
            .filter_map(|role| r.role.get(*role).cloned())
            .collect();
        if syms.is_empty() {
            continue;
        }
        let mut t = Token {
            kind: "infix".into(),
            spaced: rule.cross && rtok.space_before,
            ..Default::default()
        };
        if syms.len() > 1 {
            t.syms = Some(syms);
        } else {
            t.sym = Some(syms.into_iter().next().unwrap());
        }
        if rule.roles.contains(&"implicit") || rule.roles.contains(&"apply") {
            t.implicit = true;
        }
        if rule.sticky {
            t.sticky = true;
        }
        out.push(t);
    }
    out
}

fn lex(
    src: &[char],
    cu: &Culture,
    choices: Option<&dyn Fn(usize) -> bool>,
    runs_out: Option<&mut Vec<usize>>,
) -> Vec<Token> {
    let lx = Lex {
        src,
        cu,
        toks: Vec::new(),
        saw_space: false,
        i: 0,
        choices,
        runs: runs_out,
    };
    lx.run()
}

/// Tous les flux de découpe (multiplexage des runs splittables).
pub fn lex_all(src: &str, cu: &Culture) -> Vec<(Vec<Token>, usize)> {
    let chars: Vec<char> = src.chars().collect();
    let mut runs: Vec<usize> = Vec::new();
    let never = |_st: usize| false;
    let whole = lex(&chars, cu, Some(&never), Some(&mut runs));
    let nrun = runs.len();
    if nrun == 0 {
        return vec![(whole, 0)];
    }
    if nrun > SPLIT_CAP {
        let always = |_st: usize| true;
        return vec![(lex(&chars, cu, Some(&always), None), nrun), (whole, 0)];
    }
    let mut out: Vec<(Vec<Token>, usize)> = Vec::new();
    for m in 0u32..(1u32 << nrun) {
        let f = |st: usize| -> bool {
            let bit = runs.iter().position(|&x| x == st).unwrap();
            (m & (1u32 << bit)) != 0
        };
        out.push((lex(&chars, cu, Some(&f), None), (m.count_ones()) as usize));
    }
    out
}
