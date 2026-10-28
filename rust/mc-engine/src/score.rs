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
// Indice qui avale l'application d'une lettre (P_{A(B)}) : au moins l'écart de popup
// (POPUP_GAP = 2) → la lecture « nom indicé appliqué » passe en auto. Miroir C# SWALLOW.
const SWALLOW: i32 = 2;

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
    let d = decl(n);
    let has_decl = d.is_some();
    let ln = loose_node(n);
    for (k, c) in n.effective_parts().iter().enumerate() {
        // Devant une unité, un script atomique (x² m, 10⁻³ ms) n'est pas une
        // inversion : il se lit d'un bloc (cf. render::child). Idem un nom INDICÉ
        // appliqué à une parenthèse : P_A(B), f_n(x), u_n(1+r) (ADR
        // 2026-10-28-Fix-indexed-name-application). Miroir C# Score.Inversions.
        let unit_script = d.map(|d| d.unit_op).unwrap_or(false) && atomic_script(c);
        let named_apply = d.map(|d| d.apply).unwrap_or(false) && k == 0 && indexed_name(c);
        if c.ntype != NType::Atom && has_decl && ln < loose_node(c) && !unit_script && !named_apply {
            inv += 1;
        }
        // Indice qui avale une application (P_{A(B)}) : écarté au-delà de la popup.
        if d.map(|d| d.sub).unwrap_or(false) && k == 1 && swallows_apply(c) {
            inv += SWALLOW;
        }
        inv += inversions(c);
    }
    inv
}

/// Nom INDICÉ (P_A, f_n, P_Ā) : base atome, indice atome ou décoration tight d'atome —
/// se lit d'un bloc devant une parenthèse. Miroir C# Score.IndexedName (ADR
/// 2026-10-28-Fix-indexed-name-application).
fn indexed_name(n: &Node) -> bool {
    if !decl(n).map(|d| d.sub).unwrap_or(false) {
        return false;
    }
    let p = n.effective_parts();
    // base = lettre ou symbole (P, f, \Gamma), jamais un nombre (« 1_n » n'est pas un nom)
    p.len() == 2
        && p[0].ntype == NType::Atom
        && p[0].sym.as_deref().and_then(|b| b.chars().next()).map_or(false, |c| !c.is_ascii_digit())
        && (p[1].ntype == NType::Atom
            || (decl(&p[1]).map(|d| d.tight).unwrap_or(false)
                && p[1].effective_parts().iter().all(|x| atom_like(x))))
}

/// L'indice commence par l'application d'une LETTRE, éventuellement sous un script :
/// A(B), n(x)², n(x)_k — la parenthèse du nom indicé a été avalée. Une fonction nommée
/// (φ(n), sin(x)) reste ambiguë (u_{φ(n)} sous-suite) : pas de pénalité. Miroir C# SwallowsApply.
fn swallows_apply(c: &Node) -> bool {
    match decl(c) {
        Some(d) if d.apply => c.effective_parts().first().map_or(false, |h| {
            h.ntype == NType::Atom
                && h.sym.as_deref().map_or(false, |s| s.len() == 1 && s.as_bytes()[0].is_ascii_alphabetic())
        }),
        Some(d) if d.sup || d.sub => c.effective_parts().first().map(|p| swallows_apply(p)).unwrap_or(false),
        _ => false,
    }
}

/// Atome, ou atome entre parenthèses tapées (« bar(A) » → paren(A)). Miroir C# AtomLike.
fn atom_like(x: &Node) -> bool {
    if x.ntype == NType::Atom {
        return true;
    }
    let q = x.effective_parts();
    x.ntype == NType::Paren && q.len() == 1 && q[0].ntype == NType::Atom
}

fn all_atom_parts(n: &Node) -> bool {
    n.effective_parts().iter().all(|c| c.ntype == NType::Atom)
}
/// Script à opérandes atomiques (x², u_n, 10⁻³ tight) : visuellement d'un seul bloc.
pub(crate) fn atomic_script(n: &Node) -> bool {
    decl(n).map(|d| (d.sup || d.sub || d.tight) && all_atom_parts(n)).unwrap_or(false)
}
// Grandeur à exposant (10⁻³ ms, x² m) : la puissance et son unité forment un
// seul bloc — pas une imbrication. Miroir C# ScriptQuantity.
fn nest_strong(n: &Node) -> bool {
    is_strong(n) && !n.implicit && !decl(n).map(|d| d.tight).unwrap_or(false) && !script_quantity(n)
}
fn script_quantity(n: &Node) -> bool {
    decl(n).map(|d| d.unit_op).unwrap_or(false)
        && n.effective_parts().first().map(|p| atomic_script(p)).unwrap_or(false)
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
        let parts = n.effective_parts();
        let apply = decl(n).map(|d| d.apply).unwrap_or(false);
        for (k, p) in parts.iter().enumerate() {
            // nom indicé collé à sa parenthèse (P_A(B), P_bar(A)(B)) : un seul bloc,
            // pas un élargissement — application, ou produit implicite devant un groupe
            if k == 0
                && indexed_name(p)
                && parts.len() == 2
                && (apply || parts[1].ntype == NType::Paren || parts[1].grouped)
            {
                continue;
            }
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

// lecture secondaire d'un token ranked (mode physique-chimie : v⁰ derrière v₀) :
// reste dans la popup (< PopupGap = 2) mais toujours derrière la préférée.
// COUPLAGE : doit dépasser l'écart d'inversion de 1 entre (x^2) et (x_2) sous une
// fonction (la parenthèse d'un indice reste un nœud « paren », celle d'une
// puissance se dissout) — sinon f(x2) lit x₂ d'abord. Gardé par les fixtures
// physchem « f(x2) » et « cos(x2) ».
const SECONDARY: f64 = 1.5;

fn secondaries(n: &Node) -> i32 {
    let mut s = if n.secondary { 1 } else { 0 };
    for c in n.effective_parts() {
        s += secondaries(c);
    }
    s
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
        + SECONDARY * secondaries(n) as f64
        + global_coherence(n)
        + mode_coherence(n)
        + slurp_coherence(n)
        - parent_refund(n)
        - def_chain(n)
}
