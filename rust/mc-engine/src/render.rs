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

//! AST → LaTeX — port de LatexRenderer.cs / render.py.
//!
//! Générique : dispatche vers le `render` déclaré par chaque symbole (data) ; la
//! culture (env matriciel, séparateur d'intervalle) est threadée en paramètre.

use crate::culture::Culture;
use crate::node::{NType, Node};
use crate::reg;
use crate::vocab::{apply_render, render_for, VocabEntry};

fn child(c: &Node, parent: &VocabEntry, cu: &Culture) -> String {
    if c.ntype == NType::Paren {
        let inner = &c.parts.as_ref().unwrap()[0];
        return if parent.bracketed { render(inner, cu) } else { render(c, cu) };
    }
    let s = render(c, cu);
    if parent.bracketed {
        return s;
    }
    if c.ntype == NType::Atom && !parent.apply {
        return s;
    }
    let self_grouped = c
        .sym
        .as_deref()
        .and_then(|sy| reg().vocab.get(sy))
        .map(|e| e.bracketed)
        .unwrap_or(false);
    let looser = c.ntype == NType::Infix
        && c.sym
            .as_deref()
            .and_then(|sy| reg().vocab.get(sy))
            .map(|e| e.looseness > parent.looseness)
            .unwrap_or(false);
    if (c.grouped && !self_grouped) || looser {
        return format!("({})", s);
    }
    s
}

pub fn render(n: &Node, cu: &Culture) -> String {
    match n.ntype {
        NType::Atom => n.sym.clone().unwrap_or_default(),
        NType::Matrix => {
            let env = &cu.matrix_env;
            let rows = n.rows.as_ref().unwrap();
            let body = rows
                .iter()
                .map(|row| row.iter().map(|x| render(x, cu)).collect::<Vec<_>>().join(" & "))
                .collect::<Vec<_>>()
                .join(" \\\\ ");
            format!("\\begin{{{env}}}{body}\\end{{{env}}}")
        }
        NType::Interval => {
            let p = n.parts.as_ref().unwrap();
            format!(
                "{}{}{}{}{}",
                n.lb.as_deref().unwrap_or(""),
                render(&p[0], cu),
                cu.interval_sep,
                render(&p[1], cu),
                n.rb.as_deref().unwrap_or("")
            )
        }
        NType::List => n
            .parts
            .as_ref()
            .unwrap()
            .iter()
            .map(|x| render(x, cu))
            .collect::<Vec<_>>()
            .join(","),
        NType::Tuple => format!(
            "({})",
            n.parts
                .as_ref()
                .unwrap()
                .iter()
                .map(|x| render(x, cu))
                .collect::<Vec<_>>()
                .join(",")
        ),
        NType::Set => format!(
            "\\{{{}\\}}",
            n.parts
                .as_ref()
                .unwrap()
                .iter()
                .map(|x| render(x, cu))
                .collect::<Vec<_>>()
                .join(",")
        ),
        NType::Paren => format!(
            "{}{}{}",
            n.lb.as_deref().unwrap_or("("),
            render(&n.parts.as_ref().unwrap()[0], cu),
            n.rb.as_deref().unwrap_or(")")
        ),
        NType::Postfix => {
            let c = &n.parts.as_ref().unwrap()[0];
            let s = render(c, cu);
            let wrap = matches!(c.ntype, NType::Infix | NType::Prefix | NType::Nary);
            let arg = if wrap { format!("({s})") } else { s };
            let d = &reg().vocab[n.sym.as_deref().unwrap()];
            apply_render(&d.render, &[arg], None)
        }
        _ => {
            // infix / prefix / nary
            let d = &reg().vocab[n.sym.as_deref().unwrap()];
            let parts = n.parts.as_ref().unwrap();
            if d.shape.as_deref() == Some("infix") {
                let a: Vec<String> = if d.sup || d.sub {
                    vec![child(&parts[0], d, cu), render(&parts[1], cu)]
                } else {
                    parts.iter().map(|c| child(c, d, cu)).collect()
                };
                return apply_render(&d.render, &a, Some(n));
            }
            // prefix / nary : l'argument paren se dissout (la fonction fournit ses délimiteurs)
            let args: Vec<String> = parts
                .iter()
                .map(|p| {
                    if p.ntype == NType::Paren {
                        render(&p.parts.as_ref().unwrap()[0], cu)
                    } else {
                        render(p, cu)
                    }
                })
                .collect();
            if d.shape.as_deref() == Some("nary") {
                render_for(d, parts.len(), &args, Some(n))
            } else {
                apply_render(&d.render, &args, Some(n))
            }
        }
    }
}
