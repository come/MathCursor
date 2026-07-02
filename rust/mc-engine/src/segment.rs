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

//! Pré-passe de bornage + recombinaison — port de Segment.cs / segment.py.
//!
//! Générique : lit class/spaced/cut via le vocabulaire, ne nomme aucun opérateur.

use crate::node::{NType, Node, Token};
use crate::reg;
use crate::vocab::VocabEntry;
use std::collections::HashMap;

pub const MAX_CHAIN: usize = 8;

fn vinfix(t: &Token) -> Option<&'static VocabEntry> {
    if t.kind == "infix" {
        if let Some(s) = t.sym.as_deref() {
            return reg().vocab.get(s);
        }
    }
    None
}

fn is_cut(t: &Token) -> bool {
    match vinfix(t) {
        Some(v) => v.cut || (t.spaced && v.looseness > 0.0),
        None => false,
    }
}

fn cut_by<F: Fn(&Token) -> bool>(seg: &[Token], keep: F) -> (Vec<Vec<Token>>, Vec<Token>) {
    let mut parts: Vec<Vec<Token>> = Vec::new();
    let mut ops: Vec<Token> = Vec::new();
    let mut depth = 0i32;
    let mut brk = 0i32;
    let mut brc = 0i32;
    let mut start = 0usize;
    for i in 0..seg.len() {
        let t = &seg[i];
        match t.kind.as_str() {
            "lparen" | "lbrace" => depth += 1,
            "rparen" | "rbrace" => depth -= 1,
            "bracket" => brk += 1,
            "bar" => brc += 1,
            "infix" if depth == 0 && brk % 2 == 0 && brc % 2 == 0 && keep(t) => {
                parts.push(seg[start..i].to_vec());
                ops.push(t.clone());
                start = i + 1;
            }
            _ => {}
        }
    }
    parts.push(seg[start..].to_vec());
    (parts, ops)
}

pub fn split(toks: &[Token]) -> (Vec<Vec<Token>>, Vec<Token>) {
    cut_by(toks, is_cut)
}

pub fn split_rel(toks: &[Token]) -> (Vec<Vec<Token>>, Vec<Token>) {
    cut_by(toks, |t| vinfix(t).map(|v| v.cut).unwrap_or(false))
}

pub fn split_operands(seg: &[Token]) -> (Vec<Vec<Token>>, Vec<Token>) {
    cut_by(seg, |_t| true)
}

pub fn chain_len(seg: &[Token]) -> usize {
    split_operands(seg).1.len()
}

pub fn split_loosest(seg: &[Token]) -> (Vec<Vec<Token>>, Vec<Token>) {
    let mut mx = f64::NEG_INFINITY;
    let mut depth = 0i32;
    let mut brk = 0i32;
    let mut brc = 0i32;
    for t in seg {
        match t.kind.as_str() {
            "lparen" | "lbrace" => depth += 1,
            "rparen" | "rbrace" => depth -= 1,
            "bracket" => brk += 1,
            "bar" => brc += 1,
            _ => {
                if depth == 0 && brk % 2 == 0 && brc % 2 == 0 {
                    if let Some(v) = vinfix(t) {
                        if v.looseness > mx {
                            mx = v.looseness;
                        }
                    }
                }
            }
        }
    }
    if mx == f64::NEG_INFINITY {
        return (vec![seg.to_vec()], Vec::new());
    }
    cut_by(seg, |t| vinfix(t).map(|v| v.looseness >= mx).unwrap_or(false))
}

fn go(
    i: usize,
    j: usize,
    picked: &[Node],
    ops: &[Token],
    memo: &mut HashMap<(usize, usize), Vec<Node>>,
) -> Vec<Node> {
    if let Some(v) = memo.get(&(i, j)) {
        return v.clone();
    }
    let out: Vec<Node> = if i == j {
        vec![picked[i].clone()]
    } else {
        let mut acc: Vec<Node> = Vec::new();
        for k in i..j {
            let op = &ops[k];
            let lefts = go(i, k, picked, ops, memo);
            let rights = go(k + 1, j, picked, ops, memo);
            for l in &lefts {
                for rnode in &rights {
                    acc.push(Node {
                        ntype: NType::Infix,
                        sym: op.sym.clone(),
                        spaced: op.spaced,
                        parts: Some(vec![l.clone(), rnode.clone()]),
                        ..Default::default()
                    });
                }
            }
        }
        acc
    };
    memo.insert((i, j), out.clone());
    out
}

pub fn combine(cands_per_seg: &[Vec<Node>], ops: &[Token]) -> Vec<Node> {
    let n = cands_per_seg.len();
    let mut dim_key: Vec<String> = Vec::with_capacity(n);
    for i in 0..n {
        let c = &cands_per_seg[i];
        if !c.is_empty() && c[0].sig.is_some() && c[0].ntype != NType::Atom {
            dim_key.push(format!("s:{}", c[0].sig.as_deref().unwrap()));
        } else {
            dim_key.push(format!("u:{i}"));
        }
    }
    let mut dims: Vec<String> = Vec::new();
    for k in &dim_key {
        if !dims.contains(k) {
            dims.push(k.clone());
        }
    }
    let dim_index: HashMap<String, usize> =
        dims.iter().enumerate().map(|(i, d)| (d.clone(), i)).collect();
    let mut length: Vec<usize> = Vec::with_capacity(dims.len());
    for d in &dims {
        let mut m = usize::MAX;
        for i in 0..n {
            if &dim_key[i] == d && cands_per_seg[i].len() < m {
                m = cands_per_seg[i].len();
            }
        }
        length.push(m);
    }

    // produit cartésien des indices par dimension
    let mut assigns: Vec<Vec<usize>> = vec![Vec::new()];
    for (di, _d) in dims.iter().enumerate() {
        let mut nxt: Vec<Vec<usize>> = Vec::new();
        for a in &assigns {
            for x in 0..length[di] {
                let mut na = a.clone();
                na.push(x);
                nxt.push(na);
            }
        }
        assigns = nxt;
    }

    let mut outl: Vec<Node> = Vec::new();
    for a in &assigns {
        let picked: Vec<Node> = (0..n)
            .map(|i| {
                let idx = a[dim_index[&dim_key[i]]];
                cands_per_seg[i][idx].clone()
            })
            .collect();
        let mut memo: HashMap<(usize, usize), Vec<Node>> = HashMap::new();
        let bracketed = go(0, picked.len() - 1, &picked, ops, &mut memo);
        outl.extend(bracketed);
    }
    outl
}
