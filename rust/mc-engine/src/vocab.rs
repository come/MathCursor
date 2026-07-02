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

//! Vocabulaire du moteur — port du builder de Vocabulary.cs / vocab.py.
//!
//! La TABLE vient de data/engine/symbols.json (source unique partagée). Ce
//! module = les types d'entrée + le builder + les logiques non-déclaratives
//! (render-hooks, accept-guards), strictement équivalentes au C#/Python.

use crate::node::{NType, Node};
use crate::reg;
use serde_json::Value;
use std::collections::HashMap;

/// Stratégie de rendu d'une entrée (port des closures Python `render`/hooks).
#[derive(Clone, Debug)]
pub enum Render {
    None,
    Template(String),
    TemplateImplicit { normal: String, implicit: String },
    FracOrSetminus,
    Neg,
    Pos,
    Unit,
    Deco { wide: String, narrow: String },
}

/// Guard d'acceptation d'un variant n-aire (port du registre `_GUARDS`).
#[derive(Clone, Copy, Debug)]
pub enum Guard {
    TightBody0,
    NonNumeric1,
    NameAtomTail,
}

#[derive(Clone, Debug)]
pub struct Variant {
    pub arity: i32,
    pub render: String,
    pub accept: Option<Guard>,
}

#[derive(Clone, Debug)]
pub struct VocabEntry {
    pub shape: Option<String>,
    pub arity: i32,
    pub cls: Option<String>,
    pub looseness: f64,
    pub bracketed: bool,
    pub cut: bool,
    pub implicit: bool,
    pub sup: bool,
    pub sub: bool,
    pub tight: bool,
    pub list: bool,
    pub mapping: bool,
    pub word_space: bool,
    pub unit_word: bool,
    pub unit_op: bool,
    pub apply: bool,
    pub unary: Option<String>,
    pub post_sign: Option<String>,
    pub lower: Option<String>,
    pub upper: Option<String>,
    pub alts: Option<Vec<String>>,
    pub alts_upper: Option<Vec<String>>,
    pub coh: Option<String>,
    pub render: Render,
    pub variants: Option<Vec<Variant>>,
}

impl Default for VocabEntry {
    fn default() -> Self {
        VocabEntry {
            shape: None,
            arity: 0,
            cls: None,
            looseness: 0.0,
            bracketed: false,
            cut: false,
            implicit: false,
            sup: false,
            sub: false,
            tight: false,
            list: false,
            mapping: false,
            word_space: false,
            unit_word: false,
            unit_op: false,
            apply: false,
            unary: None,
            post_sign: None,
            lower: None,
            upper: None,
            alts: None,
            alts_upper: None,
            coh: None,
            render: Render::None,
            variants: None,
        }
    }
}

// ── helpers d'accès ──────────────────────────────────────────────────────────

/// VOCAB[sym] (None si absent). 'static car le registre est global.
pub fn decl(n: &Node) -> Option<&'static VocabEntry> {
    n.sym.as_deref().and_then(|s| reg().vocab.get(s))
}

/// loose(sym) du C#/Python : looseness de l'entrée, 0 si inconnue/None.
pub fn loose(sym: Option<&str>) -> f64 {
    sym.and_then(|s| reg().vocab.get(s))
        .map(|e| e.looseness)
        .unwrap_or(0.0)
}

// ── substitution single-pass des templates ({0}..{5}) ────────────────────────
fn fill(tmpl: &str, a: &[String]) -> String {
    let chars: Vec<char> = tmpl.chars().collect();
    let mut out = String::new();
    let mut i = 0;
    while i < chars.len() {
        if chars[i] == '{'
            && i + 2 < chars.len()
            && chars[i + 1].is_ascii_digit()
            && chars[i + 2] == '}'
        {
            let idx = (chars[i + 1] as usize) - ('0' as usize);
            out.push_str(&a[idx]);
            i += 3;
        } else {
            out.push(chars[i]);
            i += 1;
        }
    }
    out
}

// ── guards (sur les Node parsés) ─────────────────────────────────────────────
fn is_numeric(n: &Node) -> bool {
    n.ntype == NType::Atom
        && !n.hole
        && n.sym
            .as_deref()
            .map(|s| s.chars().next().map(|c| c.is_ascii_digit()).unwrap_or(false))
            .unwrap_or(false)
}
fn is_non_numeric(n: &Node) -> bool {
    !is_numeric(n)
}
fn is_name_atom(n: &Node) -> bool {
    n.hole
        || (n.ntype == NType::Atom
            && n.sym
                .as_deref()
                .map(|s| s.chars().next().map(|c| !c.is_ascii_digit()).unwrap_or(false))
                .unwrap_or(false))
}
fn is_tight_body(n: &Node) -> bool {
    is_non_numeric(n)
        && !(n.ntype == NType::Infix && loose(n.sym.as_deref()) >= reg().consts.quant)
}

/// Évalue un accept-guard sur les parts candidates (port de `_GUARDS[...]`).
pub fn guard_accept(g: Guard, parts: &[Node]) -> bool {
    match g {
        Guard::TightBody0 => is_tight_body(&parts[0]),
        Guard::NonNumeric1 => is_non_numeric(&parts[1]),
        Guard::NameAtomTail => parts[1..].iter().all(is_name_atom),
    }
}

// ── application du rendu (port de _hook / _fill / render_for) ─────────────────

/// Applique la stratégie de rendu par défaut de l'entrée (infix/prefix/postfix).
pub fn apply_render(r: &Render, a: &[String], node: Option<&Node>) -> String {
    let c = &reg().consts;
    match r {
        Render::None => String::new(),
        Render::Template(t) => fill(t, a),
        Render::TemplateImplicit { normal, implicit } => {
            let implicit_node = node.map(|n| n.implicit).unwrap_or(false);
            fill(if implicit_node { implicit } else { normal }, a)
        }
        Render::FracOrSetminus => {
            if let Some(n) = node {
                if let Some(parts) = &n.parts {
                    if parts.len() > 1 && parts[1].ntype == NType::Set {
                        return format!("{}\\setminus {}", a[0], a[1]);
                    }
                }
            }
            format!("\\frac{{{}}}{{{}}}", a[0], a[1])
        }
        Render::Neg => {
            let op0 = node.and_then(|n| n.parts.as_ref()).map(|p| p[0].sym.as_deref());
            let lz = loose(op0.flatten());
            if lz >= c.sum {
                format!("-({})", a[0])
            } else {
                format!("-{}", a[0])
            }
        }
        Render::Pos => {
            let op0 = node.and_then(|n| n.parts.as_ref()).map(|p| p[0].sym.as_deref());
            let lz = loose(op0.flatten());
            if lz >= c.sum {
                format!("+({})", a[0])
            } else {
                format!("+{}", a[0])
            }
        }
        Render::Unit => {
            let spaced = node.map(|n| n.spaced).unwrap_or(false);
            let sep = if spaced { "\\," } else { "" };
            format!("{}{}\\mathrm{{{}}}", a[0], sep, a[1])
        }
        Render::Deco { wide, narrow } => {
            if a[0].chars().count() > 1 {
                format!("\\{}{{{}}}", wide, a[0])
            } else {
                format!("\\{}{{{}}}", narrow, a[0])
            }
        }
    }
}

/// render_for(argc) : pour les n-aires, sélectionne le template de variant par
/// arité, sinon le rendu par défaut.
pub fn render_for(entry: &VocabEntry, argc: usize, a: &[String], node: Option<&Node>) -> String {
    if let Some(vs) = &entry.variants {
        for v in vs {
            if v.arity as usize == argc {
                return fill(&v.render, a);
            }
        }
    }
    apply_render(&entry.render, a, node)
}

// ── construction d'une entrée depuis le JSON ─────────────────────────────────

fn guard_of(name: &str) -> Guard {
    match name {
        "tight_body_0" => Guard::TightBody0,
        "non_numeric_1" => Guard::NonNumeric1,
        "name_atom_tail" => Guard::NameAtomTail,
        other => panic!("acceptGuard inconnu : {other}"),
    }
}

fn str_list(v: &Value) -> Vec<String> {
    v.as_array()
        .unwrap()
        .iter()
        .map(|x| x.as_str().unwrap().to_string())
        .collect()
}

pub(crate) fn build_entry(j: &Value, loose_map: &HashMap<String, f64>) -> VocabEntry {
    let mut e = VocabEntry::default();
    if let Some(s) = j.get("shape").and_then(|v| v.as_str()) {
        e.shape = Some(s.to_string());
    }
    if let Some(a) = j.get("arity").and_then(|v| v.as_i64()) {
        e.arity = a as i32;
    }
    if let Some(s) = j.get("class").and_then(|v| v.as_str()) {
        e.cls = Some(s.to_string());
    }
    if let Some(s) = j.get("looseness").and_then(|v| v.as_str()) {
        e.looseness = loose_map[s];
    }
    let flag = |k: &str| j.get(k).and_then(|v| v.as_bool()).unwrap_or(false);
    e.bracketed = flag("bracketed");
    e.cut = flag("cut");
    e.implicit = flag("implicit");
    e.sup = flag("sup");
    e.sub = flag("sub");
    e.tight = flag("tight");
    e.list = flag("list");
    e.mapping = flag("mapping");
    e.word_space = flag("wordSpace");
    e.unit_word = flag("unitWord");
    e.unit_op = flag("unitOp");
    e.apply = flag("apply");
    if let Some(s) = j.get("unary").and_then(|v| v.as_str()) {
        e.unary = Some(s.to_string());
    }
    if let Some(s) = j.get("postSign").and_then(|v| v.as_str()) {
        e.post_sign = Some(s.to_string());
    }
    if let Some(s) = j.get("lower").and_then(|v| v.as_str()) {
        e.lower = Some(s.to_string());
    }
    if let Some(s) = j.get("upper").and_then(|v| v.as_str()) {
        e.upper = Some(s.to_string());
    }
    if let Some(v) = j.get("alts") {
        e.alts = Some(str_list(v));
    }
    if let Some(v) = j.get("altsUpper") {
        e.alts_upper = Some(str_list(v));
    }
    if let Some(s) = j.get("coh").and_then(|v| v.as_str()) {
        e.coh = Some(s.to_string());
    }

    if let Some(hook) = j.get("renderHook").and_then(|v| v.as_str()) {
        e.render = match hook {
            "frac_or_setminus" => Render::FracOrSetminus,
            "neg" => Render::Neg,
            "pos" => Render::Pos,
            "unit" => Render::Unit,
            "deco" => {
                let p = j.get("hookParams").unwrap();
                Render::Deco {
                    wide: p["wide"].as_str().unwrap().to_string(),
                    narrow: p["narrow"].as_str().unwrap().to_string(),
                }
            }
            other => panic!("renderHook inconnu : {other}"),
        };
    } else if let Some(t) = j.get("render").and_then(|v| v.as_str()) {
        if let Some(ti) = j.get("renderImplicit").and_then(|v| v.as_str()) {
            e.render = Render::TemplateImplicit {
                normal: t.to_string(),
                implicit: ti.to_string(),
            };
        } else {
            e.render = Render::Template(t.to_string());
        }
    }

    if let Some(vs) = j.get("variants").and_then(|v| v.as_array()) {
        let mut out = Vec::new();
        for v in vs {
            out.push(Variant {
                arity: v["arity"].as_i64().unwrap() as i32,
                render: v["render"].as_str().unwrap().to_string(),
                accept: v
                    .get("acceptGuard")
                    .and_then(|g| g.as_str())
                    .map(guard_of),
            });
        }
        e.variants = Some(out);
    }
    e
}
