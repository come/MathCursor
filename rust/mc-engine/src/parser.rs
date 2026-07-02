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

//! tokens → FORÊT (tous les parses) — port de Parser.cs / parser.py (Forest).
//!
//! Parser de spans mémoïsé sur une grammaire AMBIGUË :
//! E -> atome | (E) | PREFIX E | NARY E…E | E INFIX E.

use crate::culture::Culture;
use crate::node::{NType, Node, Token};
use crate::reg;
use crate::vocab::guard_accept;
use std::collections::HashMap;

const CAP: usize = 64; // produit cartésien des cellules au-delà -> repli

fn hole() -> Node {
    Node {
        ntype: NType::Atom,
        sym: Some("\\square ".into()),
        hole: true,
        ..Default::default()
    }
}

fn keep_eligible(e: &Node) -> bool {
    if e.ntype == NType::Atom {
        return !e.hole;
    }
    e.ntype == NType::Infix
        && e.sym
            .as_deref()
            .and_then(|s| reg().vocab.get(s))
            .map(|v| v.sub)
            .unwrap_or(false)
}

fn cartesian(lists: &[Vec<Node>]) -> Vec<Vec<Node>> {
    let mut acc: Vec<Vec<Node>> = vec![Vec::new()];
    for l in lists {
        let mut nxt: Vec<Vec<Node>> = Vec::new();
        for c in &acc {
            for x in l {
                let mut v = c.clone();
                v.push(x.clone());
                nxt.push(v);
            }
        }
        acc = nxt;
    }
    acc
}

fn match_close(toks: &[Token], i: usize) -> i64 {
    let mut d = 0i32;
    for k in i..toks.len() {
        if toks[k].kind == "lparen" {
            d += 1;
        } else if toks[k].kind == "rparen" {
            d -= 1;
            if d == 0 {
                return k as i64;
            }
        }
    }
    -1
}

fn match_brace(toks: &[Token], i: usize) -> i64 {
    let mut d = 0i32;
    for k in i..toks.len() {
        if toks[k].kind == "lbrace" {
            d += 1;
        } else if toks[k].kind == "rbrace" {
            d -= 1;
            if d == 0 {
                return k as i64;
            }
        }
    }
    -1
}

fn comma_segs(toks: &[Token], a: usize, b: usize) -> Option<Vec<(usize, usize)>> {
    let mut segs: Vec<(usize, usize)> = Vec::new();
    let mut depth = 0i32;
    let mut start = a;
    for k in a..b {
        let t = &toks[k];
        match t.kind.as_str() {
            "lparen" | "lbrace" => depth += 1,
            "rparen" | "rbrace" => depth -= 1,
            "sep" if depth == 0 && t.sym.as_deref() == Some(",") => {
                segs.push((start, k));
                start = k + 1;
            }
            _ => {}
        }
    }
    segs.push((start, b));
    if segs.len() >= 2 {
        Some(segs)
    } else {
        None
    }
}

fn is_repere(toks: &[Token], segs: &[(usize, usize)]) -> bool {
    if segs.len() < 3 || segs.len() > 4 {
        return false;
    }
    let (a0, b0) = segs[0];
    if b0 - a0 != 1 {
        return false;
    }
    let o = &toks[a0];
    let ok_o = o.kind == "atom"
        && o.syms.is_none()
        && o.sym
            .as_deref()
            .map(|s| s.chars().count() == 1 && s.chars().next().map(|c| ('A'..='Z').contains(&c)).unwrap_or(false))
            .unwrap_or(false);
    if !ok_o {
        return false;
    }
    for k in 1..segs.len() {
        let (a, b) = segs[k];
        if b - a != 2 {
            return false;
        }
        let p = &toks[a];
        let p_ok = p.kind == "prefix"
            && p.sym
                .as_deref()
                .and_then(|s| reg().vocab.get(s))
                .map(|v| v.tight)
                .unwrap_or(false);
        if !p_ok {
            return false;
        }
        if toks[a + 1].kind != "atom" {
            return false;
        }
    }
    true
}

fn is_word_atom_seg(toks: &[Token], a: usize, b: usize) -> bool {
    if b - a != 1 {
        return false;
    }
    let t = &toks[a];
    if t.kind != "atom" || t.num || t.syms.is_some() {
        return false;
    }
    match t.sym.as_deref() {
        Some(s) => s.chars().count() >= 2 && s.chars().all(|c| c.is_alphabetic()),
        None => false,
    }
}

const SIG_CHARS: &[(&str, char)] = &[("atom", 'a'), ("lparen", '('), ("rparen", ')')];

fn sig_of(toks: &[Token], a: usize, b: usize) -> String {
    let mut s = String::new();
    for k in a..b {
        let t = &toks[k];
        if let Some((_, ch)) = SIG_CHARS.iter().find(|(kind, _)| *kind == t.kind) {
            s.push(*ch);
        } else if let Some(sym) = &t.sym {
            s.push_str(sym);
        } else if let Some(syms) = &t.syms {
            for x in syms {
                s.push_str(x);
            }
        } else {
            s.push('?');
        }
    }
    s
}

struct Forest<'a> {
    toks: &'a [Token],
    on_group: Option<&'a dyn Fn(&[Token]) -> Vec<Node>>,
    cu: &'a Culture,
    memo: HashMap<(usize, usize), Vec<Node>>,
    end: usize,
}

impl<'a> Forest<'a> {
    fn parse_span(&mut self, i: usize, j: usize) -> Vec<Node> {
        if let Some(v) = self.memo.get(&(i, j)) {
            return v.clone();
        }
        if i == j {
            let h = vec![hole()];
            self.memo.insert((i, j), h.clone());
            return h;
        }

        let toks = self.toks;
        let cu = self.cu;
        let og = self.on_group;
        let end = self.end;
        let mut outl: Vec<Node> = Vec::new();
        let head = &toks[i];

        // atome
        if j - i == 1 && head.kind == "atom" {
            let syms: Vec<String> = if let Some(s) = &head.syms {
                s.clone()
            } else {
                vec![head.sym.clone().unwrap()]
            };
            for (ai, sym) in syms.into_iter().enumerate() {
                outl.push(Node {
                    ntype: NType::Atom,
                    sym: Some(sym),
                    coh: head.coh.clone(),
                    ai: ai as i32,
                    ..Default::default()
                });
            }
        }

        // ( E )
        if head.kind == "lparen" && match_close(toks, i) == (j - 1) as i64 {
            let tuple_segs = comma_segs(toks, i + 1, j - 1);
            if let Some(segs) = &tuple_segs {
                let tlists: Vec<Vec<Node>> =
                    segs.iter().map(|(a, b)| self.parse_span(*a, *b)).collect();
                for combo in cartesian(&tlists) {
                    let parts: Vec<Node> = combo
                        .into_iter()
                        .map(|mut g| {
                            g.grouped = true;
                            g
                        })
                        .collect();
                    outl.push(Node { ntype: NType::Tuple, parts: Some(parts), ..Default::default() });
                }
            } else {
                let interior = if let Some(f) = og {
                    f(&toks[i + 1..j - 1])
                } else {
                    self.parse_span(i + 1, j - 1)
                };
                for e in interior {
                    if keep_eligible(&e) {
                        let mut inner = e;
                        inner.grouped = true;
                        outl.push(Node {
                            ntype: NType::Paren,
                            grouped: true,
                            parts: Some(vec![inner]),
                            ..Default::default()
                        });
                    } else {
                        let mut g = e;
                        g.grouped = true;
                        outl.push(g);
                    }
                }
            }
            let do_matrices = match &tuple_segs {
                None => true,
                Some(segs) => !is_repere(toks, segs),
            };
            if do_matrices {
                let m = self.matrices(i + 1, j - 1);
                outl.extend(m);
            }
        }

        // [AB], [a_i] : crochets tapés conservés
        if head.kind == "bracket"
            && head.sym.as_deref() == Some("[")
            && j - i >= 3
            && toks[j - 1].kind == "bracket"
            && toks[j - 1].sym.as_deref() == Some("]")
        {
            for e in self.parse_span(i + 1, j - 1) {
                if keep_eligible(&e) {
                    let mut inner = e;
                    inner.grouped = true;
                    outl.push(Node {
                        ntype: NType::Paren,
                        grouped: true,
                        lb: Some("[".into()),
                        rb: Some("]".into()),
                        parts: Some(vec![inner]),
                        ..Default::default()
                    });
                }
            }
        }

        // INTERVALLE : crochet … SEP … crochet
        if head.kind == "bracket" && toks[j - 1].kind == "bracket" && j - i >= 3 {
            let mut depth = 0i32;
            let mut comma: i64 = -1;
            let mut ok = true;
            let mut k = i + 1;
            while k < j - 1 && ok {
                let t = &toks[k];
                if t.kind == "lparen" {
                    depth += 1;
                } else if t.kind == "rparen" {
                    depth -= 1;
                } else if depth == 0 && t.kind == "bracket" {
                    ok = false;
                } else if depth == 0 && t.kind == "sep" && t.sym.as_deref() == Some(cu.interval_sep.as_str()) {
                    if comma == -1 {
                        comma = k as i64;
                    } else {
                        ok = false;
                    }
                }
                k += 1;
            }
            if ok && comma != -1 {
                let comma = comma as usize;
                let los = self.parse_span(i + 1, comma);
                let his = self.parse_span(comma + 1, j - 1);
                for lo in &los {
                    for hi in &his {
                        let mut glo = lo.clone();
                        glo.grouped = true;
                        let mut ghi = hi.clone();
                        ghi.grouped = true;
                        outl.push(Node {
                            ntype: NType::Interval,
                            lb: head.sym.clone(),
                            rb: toks[j - 1].sym.clone(),
                            parts: Some(vec![glo, ghi]),
                            ..Default::default()
                        });
                    }
                }
            }
        }

        // ENSEMBLE { e1, e2, … }
        if head.kind == "lbrace" && match_brace(toks, i) == (j - 1) as i64 {
            let segs = comma_segs(toks, i + 1, j - 1).unwrap_or_else(|| vec![(i + 1, j - 1)]);
            let lists: Vec<Vec<Node>> = segs.iter().map(|(a, b)| self.parse_span(*a, *b)).collect();
            for combo in cartesian(&lists) {
                let parts: Vec<Node> = combo
                    .into_iter()
                    .map(|mut g| {
                        g.grouped = true;
                        g
                    })
                    .collect();
                outl.push(Node { ntype: NType::Set, parts: Some(parts), ..Default::default() });
            }
        }

        // LISTE NUE : virgules profondeur 0 (anti-prose : aucun segment mot-atome)
        if let Some(bare_segs) = comma_segs(toks, i, j) {
            if !bare_segs.iter().any(|(a, b)| is_word_atom_seg(toks, *a, *b)) {
                let blists: Vec<Vec<Node>> =
                    bare_segs.iter().map(|(a, b)| self.parse_span(*a, *b)).collect();
                for combo in cartesian(&blists) {
                    let parts: Vec<Node> = combo
                        .into_iter()
                        .map(|mut g| {
                            g.grouped = true;
                            g
                        })
                        .collect();
                    outl.push(Node { ntype: NType::List, parts: Some(parts), ..Default::default() });
                }
            }
        }

        // VALEUR ABSOLUE |x| / NORME ||x||
        if head.kind == "bar"
            && toks[j - 1].kind == "bar"
            && toks[j - 1].sym == head.sym
            && j - i >= 2
        {
            let mut depth = 0i32;
            let mut ok = true;
            let mut k = i + 1;
            while k < j - 1 && ok {
                let t = &toks[k];
                if t.kind == "lparen" {
                    depth += 1;
                } else if t.kind == "rparen" {
                    depth -= 1;
                } else if depth == 0 && t.kind == "bar" {
                    ok = false;
                }
                k += 1;
            }
            if ok {
                for a in self.parse_span(i + 1, j - 1) {
                    let mut g = a;
                    g.grouped = true;
                    outl.push(Node {
                        ntype: NType::Prefix,
                        sym: head.sym.clone(),
                        parts: Some(vec![g]),
                        ..Default::default()
                    });
                }
            }
        }

        // POSTFIXE SERRÉ : n!, f'
        if toks[j - 1].kind == "postfix" && j - i >= 2 {
            let tight = (j - i == 2)
                || (toks[i].kind == "lparen" && match_close(toks, i) == (j - 2) as i64)
                || (toks[j - 2].kind == "postfix");
            if tight {
                for a in self.parse_span(i, j - 1) {
                    outl.push(Node {
                        ntype: NType::Postfix,
                        sym: toks[j - 1].sym.clone(),
                        parts: Some(vec![a]),
                        ..Default::default()
                    });
                }
            }
        }

        // barre « mid » au milieu = infixe A\mid B
        if j >= 2 {
            for k in (i + 1)..(j - 1) {
                if toks[k].kind == "bar" && toks[k].sym.as_deref() == Some("abs") {
                    let ls = self.parse_span(i, k);
                    let rs = self.parse_span(k + 1, j);
                    for l in &ls {
                        for rr in &rs {
                            outl.push(Node {
                                ntype: NType::Infix,
                                sym: Some("·mid".into()),
                                parts: Some(vec![l.clone(), rr.clone()]),
                                ..Default::default()
                            });
                        }
                    }
                }
            }
        }

        if head.kind == "prefix" {
            let close = if i + 1 < toks.len() && toks[i + 1].kind == "lparen" {
                match_close(toks, i + 1)
            } else {
                -1
            };
            let head_sym = head.sym.clone().unwrap();
            let head_spaced = head.spaced;
            if close != -1 {
                if close == (j - 1) as i64 {
                    for a in self.parse_span(i + 1, j) {
                        outl.push(Node {
                            ntype: NType::Prefix,
                            sym: Some(head_sym.clone()),
                            spaced: head_spaced,
                            parts: Some(vec![a]),
                            ..Default::default()
                        });
                    }
                }
            } else {
                let tight = reg().vocab.get(&head_sym).map(|v| v.tight).unwrap_or(false);
                if !tight || j - i <= 2 {
                    for a in self.parse_span(i + 1, j) {
                        outl.push(Node {
                            ntype: NType::Prefix,
                            sym: Some(head_sym.clone()),
                            spaced: head_spaced,
                            parts: Some(vec![a]),
                            ..Default::default()
                        });
                    }
                }
            }
            let is_list = reg().vocab.get(&head_sym).map(|v| v.list).unwrap_or(false);
            if is_list {
                if let Some(segs) = comma_segs(toks, i + 1, j) {
                    let lists: Vec<Vec<Node>> =
                        segs.iter().map(|(a, b)| self.parse_span(*a, *b)).collect();
                    for combo in cartesian(&lists) {
                        outl.push(Node {
                            ntype: NType::Prefix,
                            sym: Some(head_sym.clone()),
                            spaced: head_spaced,
                            parts: Some(vec![Node {
                                ntype: NType::List,
                                parts: Some(combo),
                                ..Default::default()
                            }]),
                            ..Default::default()
                        });
                    }
                }
            }
        }

        if head.kind == "nary" {
            let head_sym = head.sym.clone().unwrap();
            let head_spaced = head.spaced;
            let (arity, variants) = {
                let ve = &reg().vocab[&head_sym];
                (ve.arity, ve.variants.clone())
            };
            for parts in self.splits(i + 1, j, arity) {
                outl.push(Node {
                    ntype: NType::Nary,
                    sym: Some(head_sym.clone()),
                    spaced: head_spaced,
                    parts: Some(parts),
                    ..Default::default()
                });
            }
            if let Some(vs) = &variants {
                if j >= end {
                    for v in vs {
                        for parts in self.splits(i + 1, j, v.arity) {
                            let accept = match v.accept {
                                None => true,
                                Some(g) => guard_accept(g, &parts),
                            };
                            if accept {
                                outl.push(Node {
                                    ntype: NType::Nary,
                                    sym: Some(head_sym.clone()),
                                    spaced: head_spaced,
                                    parts: Some(parts),
                                    ..Default::default()
                                });
                            }
                        }
                    }
                }
            }
        }

        // opérateur infixe = sommet
        for k in i..j {
            if toks[k].kind == "infix" {
                if k == i && i != 0 {
                    continue;
                }
                if k == j - 1 && j < end {
                    continue;
                }
                if toks[k].sticky && (j - k - 1) != 1 {
                    continue;
                }
                let syms: Vec<String> = if let Some(s) = &toks[k].syms {
                    s.clone()
                } else {
                    vec![toks[k].sym.clone().unwrap()]
                };
                let choice = (j - k - 1) > 1;
                let spaced = toks[k].spaced;
                let implicit = toks[k].implicit;
                let ls = self.parse_span(i, k);
                let rs = self.parse_span(k + 1, j);
                for l in &ls {
                    for rr in &rs {
                        for sym in &syms {
                            outl.push(Node {
                                ntype: NType::Infix,
                                sym: Some(sym.clone()),
                                spaced,
                                implicit,
                                choice,
                                parts: Some(vec![l.clone(), rr.clone()]),
                                ..Default::default()
                            });
                        }
                    }
                }
            }
        }

        let sg = sig_of(toks, i, j);
        for n in &mut outl {
            n.sig = Some(sg.clone());
        }
        self.memo.insert((i, j), outl.clone());
        outl
    }

    fn splits(&mut self, a: usize, b: usize, k_arity: i32) -> Vec<Vec<Node>> {
        if k_arity == 0 {
            return if a == b { vec![vec![]] } else { vec![] };
        }
        if a == b {
            return vec![(0..k_arity).map(|_| hole()).collect()];
        }
        let toks = self.toks;
        let mut res: Vec<Vec<Node>> = Vec::new();
        let head_tok = &toks[a];
        // Un sup issu d'un postSign (0⁺) ne peut PAS s'orienter en chapeau orphelin
        // quand la découpe n-aire coupe sa base (ADR 2026-06-22 orphan-postsign-sup-not-hat).
        let unary: Option<String> = if !head_tok.sign_sup && head_tok.kind == "infix" {
            head_tok
                .sym
                .as_deref()
                .and_then(|s| reg().vocab.get(s))
                .and_then(|v| v.unary.clone())
        } else {
            None
        };
        let head_spaced = head_tok.spaced;
        for m in (a + 1)..=b {
            let firsts = self.parse_span(a, m);
            let mut seq: Vec<Node> = firsts;
            if let Some(un) = &unary {
                if m > a + 1 {
                    for x in self.parse_span(a + 1, m) {
                        seq.push(Node {
                            ntype: NType::Prefix,
                            sym: Some(un.clone()),
                            spaced: head_spaced,
                            parts: Some(vec![x]),
                            ..Default::default()
                        });
                    }
                }
            }
            for first in &seq {
                for mut rest in self.splits(m, b, k_arity - 1) {
                    let mut row = vec![first.clone()];
                    row.append(&mut rest);
                    res.push(row);
                }
            }
            if m < b
                && toks[m].kind == "infix"
                && toks[m].spaced
                && toks[m].sym.as_deref() == Some(reg().role["unitOp"].as_str())
            {
                let f2 = self.parse_span(a, m);
                for first in &f2 {
                    for mut rest in self.splits(m + 1, b, k_arity - 1) {
                        let mut row = vec![first.clone()];
                        row.append(&mut rest);
                        res.push(row);
                    }
                }
            }
        }
        res
    }

    fn matrices(&mut self, a: usize, b: usize) -> Vec<Node> {
        if b <= a {
            return vec![];
        }
        let toks = self.toks;
        let mut cells: Vec<(usize, usize)> = Vec::new();
        let mut seps: Vec<i32> = Vec::new();
        let mut depth = 0i32;
        let mut start = a;
        let mut prev_sep = true;
        // Séparateur EXPLICITE ; ou , = vraie matrice 2D (un des deux déclencheurs
        // de la greffe unaire, l'autre étant la frontière de frappe). Miroir Parser.cs.
        let mut has_explicit_sep = false;
        for k in a..b {
            let t = &toks[k];
            if depth == 0 && t.kind != "sep" && t.space_before && !prev_sep && k > start {
                cells.push((start, k));
                seps.push(1);
                start = k;
            }
            if t.kind == "lparen" {
                depth += 1;
                prev_sep = false;
                continue;
            }
            if t.kind == "rparen" {
                depth -= 1;
                prev_sep = false;
                continue;
            }
            if depth == 0 && t.kind == "sep" {
                cells.push((start, k));
                seps.push(t.rank);
                start = k + 1;
                prev_sep = true;
                has_explicit_sep = true;
                continue;
            }
            prev_sep = false;
        }
        cells.push((start, b));
        if cells.len() < 2 {
            return vec![];
        }

        let mut ranks: Vec<i32> = Vec::new();
        for r in &seps {
            if !ranks.contains(r) {
                ranks.push(*r);
            }
        }

        let by_rank = |rank: i32| -> Vec<Vec<(usize, usize)>> {
            let mut rows: Vec<Vec<(usize, usize)>> = Vec::new();
            let mut row: Vec<(usize, usize)> = vec![cells[0]];
            for k in 0..seps.len() {
                if seps[k] == rank {
                    rows.push(row);
                    row = vec![cells[k + 1]];
                } else {
                    row.push(cells[k + 1]);
                }
            }
            rows.push(row);
            rows
        };

        let mut grids: Vec<(Vec<Vec<(usize, usize)>>, bool)> = Vec::new();
        if ranks.len() >= 2 {
            let mx = *ranks.iter().max().unwrap();
            grids.push((by_rank(mx), false));
        } else if ranks[0] == 2 {
            grids.push((cells.iter().map(|c| vec![*c]).collect(), false));
        } else {
            grids.push((vec![cells.clone()], false));
            grids.push((cells.iter().map(|c| vec![*c]).collect(), true));
        }

        // Greffe unaire activée si séparateur explicite ; , OU matrice à la frontière
        // de frappe (paren en cours de saisie). Cf. Parser.cs. ADR 2026-06-30.
        let allow_graft = has_explicit_sep || b == self.end;
        let mut outl: Vec<Node> = Vec::new();
        for (g_rows, g_alt) in grids {
            let w = g_rows.iter().map(|r| r.len()).max().unwrap_or(0);
            // chaque cellule (paddée à None) → sa forêt
            let mut forests: Vec<Vec<Vec<Node>>> = Vec::new();
            for row in &g_rows {
                let mut padded: Vec<Option<(usize, usize)>> = row.iter().map(|c| Some(*c)).collect();
                while padded.len() < w {
                    padded.push(None);
                }
                let cell_forests: Vec<Vec<Node>> = padded
                    .iter()
                    .map(|c| match c {
                        Some((s, e)) => self.cell_forest(*s, *e, allow_graft),
                        None => vec![hole()],
                    })
                    .collect();
                forests.push(cell_forests);
            }
            let flat: Vec<Vec<Node>> = forests.iter().flatten().cloned().collect();
            if flat.iter().any(|f| f.is_empty()) {
                continue;
            }
            let mut prod: usize = 1;
            for f in &flat {
                prod = prod.saturating_mul(f.len());
            }
            let lists: Vec<Vec<Node>> = if prod > CAP {
                flat.iter().map(|f| vec![f[0].clone()]).collect()
            } else {
                flat
            };
            for combo in cartesian(&lists) {
                let mut rows: Vec<Vec<Node>> = Vec::new();
                for ri in 0..forests.len() {
                    let mut row_nodes: Vec<Node> = Vec::new();
                    for ci in 0..forests[ri].len() {
                        row_nodes.push(combo[ri * w + ci].clone());
                    }
                    rows.push(row_nodes);
                }
                outl.push(Node {
                    ntype: NType::Matrix,
                    rows: Some(rows),
                    delim: Some("(".into()),
                    alt: g_alt,
                    ..Default::default()
                });
            }
        }
        outl
    }

    // Forêt d'une cellule de matrice = sa lecture normale + (si la tête est un
    // infixe unaire-capable, « - »/« + ») la lecture greffée prefix(unaire, reste).
    // Sans ça, une cellule « -1 » (le « - » lexé BINAIRE car précédé d'un opérande
    // dans le flux plat) a une forêt VIDE → cellule morte → matrice morte. La greffe
    // fait coexister `x-1` (lecture non-matricielle) et la matrice [x,-1] ; le Score
    // arbitre. Miroir de Parser.cs::CellForest. ADR 2026-06-30-Fix-matrix-signed-cell-unary-graft.
    fn cell_forest(&mut self, s: usize, e: usize, allow_graft: bool) -> Vec<Node> {
        let mut f = self.parse_span(s, e);
        if allow_graft && e > s + 1 {
            let toks = self.toks;
            let ht = &toks[s];
            if !ht.sign_sup && ht.kind == "infix" {
                let unary = ht
                    .sym
                    .as_deref()
                    .and_then(|sym| reg().vocab.get(sym))
                    .and_then(|v| v.unary.clone());
                if let Some(un) = unary {
                    let spaced = ht.spaced;
                    for x in self.parse_span(s + 1, e) {
                        f.push(Node {
                            ntype: NType::Prefix,
                            sym: Some(un.clone()),
                            spaced,
                            parts: Some(vec![x]),
                            ..Default::default()
                        });
                    }
                }
            }
        }
        f
    }
}

pub fn parse(
    toks: &[Token],
    on_group: Option<&dyn Fn(&[Token]) -> Vec<Node>>,
    cu: &Culture,
) -> Vec<Node> {
    let end = toks.iter().position(|t| t.virtual_).unwrap_or(toks.len());
    let mut f = Forest {
        toks,
        on_group,
        cu,
        memo: HashMap::new(),
        end,
    };
    f.parse_span(0, toks.len())
}
