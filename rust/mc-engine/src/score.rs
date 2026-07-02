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

//! filtre COUPE + COÛT — port de Score.cs / score.py.
//!
//! Générique : ne lit que les FEATURES (class, looseness, cut, bracketed,
//! mapping), jamais un nom d'opérateur.

use crate::node::{NType, Node};
use crate::vocab::decl;
use std::collections::{HashMap, HashSet};

const PENALTY: f64 = 1.5;
const WIDEN: f64 = 1.0;
const MODE_MIX: f64 = 3.0;
const ORIENT: f64 = 0.5;
const MAP_DEF: f64 = 2.5;
const HOLE_COST: f64 = 3.0;

fn loose_node(n: &Node) -> f64 {
    let d = match decl(n) {
        Some(d) => d,
        None => return 0.0,
    };
    d.looseness + if d.shape.as_deref() == Some("infix") && n.spaced { 1.5 } else { 0.0 }
}

fn is_strong(n: &Node) -> bool {
    n.ntype != NType::Atom && decl(n).map(|d| d.cls.as_deref() == Some("STRONG")).unwrap_or(false)
}
fn is_weak(n: &Node) -> bool {
    n.ntype != NType::Atom && decl(n).map(|d| d.cls.as_deref() == Some("WEAK")).unwrap_or(false)
}
fn is_cut_op(n: &Node) -> bool {
    n.ntype != NType::Atom && decl(n).map(|d| d.cut).unwrap_or(false)
}

// ── Filtre COUPE ─────────────────────────────────────────────────────────────
fn covers_spaced_weak(n: &Node) -> bool {
    if n.ntype == NType::Atom || n.grouped {
        return false;
    }
    if is_weak(n) && n.spaced {
        return true;
    }
    n.effective_parts().iter().any(|c| covers_spaced_weak(c))
}

pub fn crosses_cut(n: &Node) -> bool {
    if n.ntype == NType::Atom {
        return false;
    }
    if is_strong(n) && n.effective_parts().iter().any(|c| covers_spaced_weak(c)) {
        return true;
    }
    if !is_cut_op(n) && n.effective_parts().iter().any(|c| is_cut_op(c) && !c.grouped) {
        return true;
    }
    n.effective_parts().iter().any(|c| crosses_cut(c))
}

// ── Coût ─────────────────────────────────────────────────────────────────────
fn inversions(n: &Node) -> i32 {
    if n.ntype == NType::Atom {
        return 0;
    }
    let mut inv = 0;
    let has_decl = decl(n).is_some();
    let ln = loose_node(n);
    for c in n.effective_parts() {
        if c.ntype != NType::Atom && has_decl && ln < loose_node(c) {
            inv += 1;
        }
        inv += inversions(c);
    }
    inv
}

fn all_atom_parts(n: &Node) -> bool {
    n.effective_parts().iter().all(|c| c.ntype == NType::Atom)
}
fn atomic_script(n: &Node) -> bool {
    decl(n).map(|d| (d.sup || d.sub) && all_atom_parts(n)).unwrap_or(false)
}
fn nest_strong(n: &Node) -> bool {
    is_strong(n) && !n.implicit && !decl(n).map(|d| d.tight).unwrap_or(false)
}
fn contains_nest(n: &Node, in_brackets: bool) -> bool {
    if n.ntype == NType::Atom {
        return false;
    }
    if nest_strong(n) && !(in_brackets && atomic_script(n)) {
        return true;
    }
    n.effective_parts().iter().any(|c| contains_nest(c, in_brackets))
}
fn nesting(n: &Node) -> i32 {
    if n.ntype == NType::Atom {
        return 0;
    }
    let mut c = 0;
    for x in n.effective_parts() {
        c += nesting(x);
    }
    if nest_strong(n) {
        let br = decl(n).map(|d| d.bracketed).unwrap_or(false);
        for x in n.effective_parts() {
            if contains_nest(x, br) {
                c += 1;
                break;
            }
        }
    }
    c
}

fn widen(n: &Node) -> f64 {
    if n.ntype == NType::Atom {
        return 0.0;
    }
    let mut c = 0.0;
    for x in n.effective_parts() {
        c += widen(x);
    }
    if n.implicit {
        for p in n.effective_parts() {
            if p.ntype != NType::Atom && !p.grouped {
                c += WIDEN;
                break;
            }
        }
    }
    c
}

fn base(n: &Node) -> f64 {
    inversions(n) as f64 + nesting(n) as f64 + widen(n)
}

fn shape(n: &Node) -> String {
    if n.ntype == NType::Atom {
        return "a".to_string();
    }
    let parts = n.effective_parts();
    let inner: Vec<String> = parts.iter().map(|p| shape(p)).collect();
    let head = n.sym.as_deref().unwrap_or_else(|| n.ntype.name());
    format!("{}({})", head, inner.join(","))
}

fn parent_refund(n: &Node) -> f64 {
    let mut r = 0.0;
    for c in n.effective_parts() {
        r += parent_refund(c);
    }
    let mut groups: HashMap<String, Vec<&Node>> = HashMap::new();
    for c in n.effective_parts() {
        if c.ntype == NType::Atom || c.sig.is_none() {
            continue;
        }
        groups.entry(c.sig.clone().unwrap()).or_default().push(c);
    }
    let ln = loose_node(n);
    let mut sym = 0.0;
    for g in groups.values() {
        if g.len() < 2 {
            continue;
        }
        let shapes: HashSet<String> = g.iter().map(|c| shape(c)).collect();
        if shapes.len() == 1 {
            for c in g {
                if ln < loose_node(c) {
                    sym += 1.0;
                }
            }
        }
    }
    let mut gestalt = 0.0;
    if decl(n).map(|d| d.bracketed).unwrap_or(false) {
        let mut inv = 0;
        for c in n.effective_parts() {
            if c.ntype != NType::Atom && ln < loose_node(c) {
                inv += 1;
            }
        }
        if inv >= 2 {
            gestalt = (inv - 1) as f64;
        }
    }
    r + if sym > gestalt { sym } else { gestalt }
}

fn collect_sig<'a>(x: &'a Node, by_sig: &mut HashMap<String, (HashSet<String>, Vec<&'a Node>)>) {
    if x.ntype != NType::Atom {
        if let Some(s) = &x.sig {
            let g = by_sig.entry(s.clone()).or_insert_with(|| (HashSet::new(), Vec::new()));
            g.0.insert(shape(x));
            g.1.push(x);
        }
    }
    for c in x.effective_parts() {
        collect_sig(c, by_sig);
    }
}

fn global_coherence(root: &Node) -> f64 {
    let mut by_sig: HashMap<String, (HashSet<String>, Vec<&Node>)> = HashMap::new();
    collect_sig(root, &mut by_sig);
    let mut d = 0.0;
    for (shapes, nodes) in by_sig.values() {
        if shapes.len() > 1 {
            d += PENALTY * (shapes.len() as f64 - 1.0);
        } else if nodes.len() >= 2 {
            d -= (nodes.len() as f64 - 1.0) * base(nodes[0]);
        }
    }
    d
}

fn collect_coh(x: &Node, by_coh: &mut HashMap<String, HashSet<i32>>) {
    if x.ntype == NType::Atom {
        if let Some(c) = &x.coh {
            by_coh.entry(c.clone()).or_default().insert(x.ai);
        }
    }
    for c in x.effective_parts() {
        collect_coh(c, by_coh);
    }
}

fn mode_coherence(root: &Node) -> f64 {
    let mut by_coh: HashMap<String, HashSet<i32>> = HashMap::new();
    collect_coh(root, &mut by_coh);
    let mut d = 0.0;
    for idx in by_coh.values() {
        if idx.len() > 1 {
            d += MODE_MIX * (idx.len() as f64 - 1.0);
        }
    }
    d
}

fn minimal_frac(n: &Node) -> bool {
    if n.ntype == NType::Atom || n.grouped {
        return false;
    }
    let parts = n.effective_parts();
    decl(n).map(|d| d.bracketed).unwrap_or(false) && parts.len() == 2 && parts[1].ntype == NType::Atom
}

fn leftmost(n: &Node) -> Option<String> {
    let mut cur = n;
    while cur.ntype != NType::Atom {
        let p = cur.effective_parts();
        if p.is_empty() {
            return None;
        }
        cur = p[0];
    }
    cur.sym.clone()
}

struct Site {
    slurp: bool,
    key: String,
    sig: String,
    base: f64,
}

fn slurp_add(f: &Node, slurp: bool, sites: &mut Vec<Site>) {
    let p = f.effective_parts();
    let key = format!(
        "{}/{}",
        leftmost(p[0]).unwrap_or_else(|| "?".to_string()),
        leftmost(p[1]).unwrap_or_else(|| "?".to_string())
    );
    sites.push(Site {
        slurp,
        key,
        sig: f.sig.clone().unwrap_or_default(),
        base: base(f),
    });
}

fn slurp_walk(x: &Node, sites: &mut Vec<Site>) {
    if x.ntype == NType::Atom {
        return;
    }
    let parts = x.effective_parts();
    if x.ntype == NType::Infix && !x.spaced && parts.len() == 2 && minimal_frac(parts[0]) {
        slurp_add(parts[0], false, sites);
    }
    if x.choice
        && decl(x).map(|d| d.bracketed).unwrap_or(false)
        && parts.len() == 2
        && parts[1].ntype != NType::Atom
    {
        slurp_add(x, true, sites);
    }
    for c in &parts {
        slurp_walk(c, sites);
    }
}

fn slurp_coherence(root: &Node) -> f64 {
    let mut sites: Vec<Site> = Vec::new();
    slurp_walk(root, &mut sites);
    if sites.len() < 2 {
        return 0.0;
    }
    let mut d = 0.0;
    for i in 0..sites.len() {
        for k in (i + 1)..sites.len() {
            let a = &sites[i];
            let b = &sites[k];
            if a.key != b.key {
                continue;
            }
            if a.slurp != b.slurp {
                d += 1.0;
            } else if a.slurp && a.sig != b.sig {
                d -= a.base.min(b.base);
            }
        }
    }
    d
}

fn matrix_extra(n: &Node) -> f64 {
    if n.ntype == NType::Atom {
        return 0.0;
    }
    let mut e = 0.0;
    for c in n.effective_parts() {
        e += matrix_extra(c);
    }
    if n.ntype == NType::Matrix && n.alt {
        e += ORIENT;
    }
    e
}

fn def_chain(n: &Node) -> f64 {
    let mut r = 0.0;
    for c in n.effective_parts() {
        r += def_chain(c);
    }
    if decl(n).map(|d| d.mapping).unwrap_or(false) {
        for c in n.effective_parts() {
            if is_cut_op(c) && !c.grouped {
                r += MAP_DEF;
                break;
            }
        }
    }
    r
}

fn holes(n: &Node) -> i32 {
    let mut h = if n.hole { 1 } else { 0 };
    for c in n.effective_parts() {
        h += holes(c);
    }
    h
}

pub fn cost(n: &Node) -> f64 {
    base(n) + matrix_extra(n) + HOLE_COST * holes(n) as f64
        + global_coherence(n)
        + mode_coherence(n)
        + slurp_coherence(n)
        - parent_refund(n)
        - def_chain(n)
}
