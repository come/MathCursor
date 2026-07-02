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

//! AST → StarMath (format formule natif de LibreOffice Math) — port de
//! starmath.py. Rend depuis l'AST (pas de re-parsing du LaTeX). Best-effort
//! (justesse fine confirmée visuellement dans LibreOffice) ; verrouillé par la
//! parité Python↔Rust sur fixtures.json.

use crate::culture::Culture;
use crate::node::{NType, Node};
use crate::reg;

// sameAs (×→*, ≤→<=, ·forallWord→forall…) : normalise vers la clé de base.
fn canon(sym: &str) -> &str {
    match reg().sameas.get(sym) {
        Some(base) => base,
        None => sym,
    }
}

// ── atomes : fragment LaTeX → StarMath ───────────────────────────────────────
fn atom_sm(sym: Option<&str>) -> String {
    let s = match sym {
        Some(s) => s,
        None => return "{}".into(),
    };
    match s {
        "\\infty " => return "infinity".into(),
        "\\emptyset " => return "emptyset".into(),
        "\\ldots " => return "dotslow".into(),
        "\\cdots " => return "cdots".into(),
        "\\vdots " => return "vdots".into(),
        "\\ddots " => return "ddots".into(),
        "\\square " => return "{}".into(),
        _ => {}
    }
    // \mathbb{X} (espace optionnel) → setX
    if let Some(rest) = s.strip_prefix("\\mathbb{") {
        let rest = rest.trim_end();
        if let Some(letter) = rest.strip_suffix('}') {
            if letter.len() == 1 && letter.chars().next().unwrap().is_ascii_uppercase() {
                return format!("set{letter}");
            }
        }
    }
    // \name<espace> (lettre grecque) → %name / %NAME
    if s.starts_with('\\') && s.ends_with(' ') {
        let name = &s[1..s.len() - 1];
        if !name.is_empty() && name.chars().all(|c| c.is_ascii_alphabetic()) {
            let upper = name.chars().next().unwrap().is_ascii_uppercase();
            return format!("%{}", if upper { name.to_uppercase() } else { name.to_string() });
        }
    }
    s.replace("{,}", ",")
}

/// opérande groupée : accolades StarMath si composite (atome laissé nu).
fn g(n: &Node, cu: &Culture) -> String {
    let s = sm(n, cu);
    if n.ntype == NType::Atom {
        s
    } else {
        format!("{{{s}}}")
    }
}

// substitution {0}/{1} EN UNE PASSE (un opérande peut contenir « {1} » littéral).
fn fill2(tmpl: &str, a: [&str; 2]) -> String {
    let ch: Vec<char> = tmpl.chars().collect();
    let mut out = String::new();
    let mut i = 0;
    while i < ch.len() {
        if ch[i] == '{' && i + 2 < ch.len() && (ch[i + 1] == '0' || ch[i + 1] == '1') && ch[i + 2] == '}' {
            out.push_str(a[(ch[i + 1] as usize) - ('0' as usize)]);
            i += 3;
        } else {
            out.push(ch[i]);
            i += 1;
        }
    }
    out
}

fn infix_tmpl(sym: &str) -> Option<&'static str> {
    Some(match sym {
        "+" => "{0} + {1}", "-" => "{0} - {1}",
        "." => "{0} cdot {1}", "·div" => "{0} div {1}",
        "=" => "{0} = {1}", "<" => "{0} < {1}", ">" => "{0} > {1}",
        "!=" => "{0} <> {1}", ">=" => "{0} >= {1}", "<=" => "{0} <= {1}",
        "~" => "{0} sim {1}", "->" => "{0} rightarrow {1}", "mapsto" => "{0} rightarrow {1}",
        "in" => "{0} in {1}", "notin" => "{0} notin {1}",
        "subset" => "{0} subset {1}", "subseteq" => "{0} subseteq {1}",
        "supset" => "{0} supset {1}", "supseteq" => "{0} supseteq {1}",
        "notsubset" => "{0} nsubset {1}",
        "and" => "{0} and {1}", "or" => "{0} or {1}",
        "equiv" => "{0} equiv {1}", "cong" => "{0} cong {1}",
        "approx" => "{0} approx {1}", "propto" => "{0} prop {1}",
        "union" => "{0} union {1}", "inter" => "{0} intersection {1}", "setminus" => "{0} setminus {1}",
        "mod" => "{0} mod {1}", "perp" => "{0} ortho {1}", "circ" => "{0} circ {1}",
        "pm" => "{0} +- {1}", "mp" => "{0} -+ {1}", "parallel" => "{0} parallel {1}",
        "·colon" => "{0} : {1}", "·mid" => "{0} divides {1}",
        _ => return None,
    })
}

fn prefix_sm(n: &Node, cu: &Culture) -> String {
    let sym = canon(n.sym.as_deref().unwrap_or(""));
    let a0 = &n.parts.as_ref().unwrap()[0];
    match sym {
        "cos" | "sin" | "tan" | "arcsin" | "arccos" | "arctan" | "ln" | "log" => {
            format!("{sym} {}", g(a0, cu))
        }
        "bar" => format!("overline {}", g(a0, cu)),
        "vec" => format!("vec {}", g(a0, cu)),
        "hat" => format!("hat {}", g(a0, cu)),
        "exp" => format!("func e^{}", g(a0, cu)),
        "sinh" | "cosh" | "tanh" | "coth" | "arg" | "Re" | "Im" | "det" | "dim" | "pgcd" | "ppcm" => {
            format!("func {sym} {}", g(a0, cu))
        }
        "sqrt" => format!("sqrt {}", g(a0, cu)),
        "abs" => format!("abs {}", g(a0, cu)),
        "norm" => format!("norm {}", g(a0, cu)),
        "floor" => format!("lfloor {} rfloor", sm(a0, cu)),
        "ceil" => format!("lceil {} rceil", sm(a0, cu)),
        "forall" | "exists" => format!("{sym} {}", g(a0, cu)),
        "nexists" => format!("not exists {}", g(a0, cu)),
        "not" => format!("neg {}", g(a0, cu)),
        "partial" => format!("partial {}", g(a0, cu)),
        "nabla" => format!("nabla {}", g(a0, cu)),
        "neg" => format!("-{}", g(a0, cu)),
        "pos" => format!("+{}", g(a0, cu)),
        "upm" => format!("+-{}", g(a0, cu)),
        "ump" => format!("-+{}", g(a0, cu)),
        other => format!("func {} {}", other, g(a0, cu)),
    }
}

fn postfix_sm(n: &Node, cu: &Culture) -> String {
    let sym = canon(n.sym.as_deref().unwrap_or(""));
    let base = g(&n.parts.as_ref().unwrap()[0], cu);
    match sym {
        "!" => format!("{base}!"),
        "'" => format!("{base}'"),
        "%" => format!("{base} \"%\""),
        "°" => format!("{base}^circ"),
        "°C" => format!("{base}^circ roman C"),
        "°F" => format!("{base}^circ roman F"),
        _ => base,
    }
}

fn nary_sm(n: &Node, cu: &Culture) -> String {
    let sym = canon(n.sym.as_deref().unwrap_or(""));
    let p = n.parts.as_ref().unwrap();
    let k = p.len();
    match sym {
        "root" => format!("nroot {} {}", g(&p[0], cu), g(&p[1], cu)),
        "binom" => format!("binom {} {}", g(&p[0], cu), g(&p[1], cu)),
        "dot" => format!("langle {} , {} rangle", sm(&p[0], cu), sm(&p[1], cu)),
        "lim" => {
            if k == 1 {
                format!("lim {}", g(&p[0], cu))
            } else {
                format!("lim from {{{} toward {}}} {}", sm(&p[0], cu), sm(&p[1], cu), g(&p[2], cu))
            }
        }
        "sum" => {
            if k == 2 {
                format!("sum from {{{}}} {}", sm(&p[0], cu), g(&p[1], cu))
            } else {
                format!("sum from {{{} = {}}} to {{{}}} {}", sm(&p[0], cu), sm(&p[1], cu), sm(&p[2], cu), g(&p[3], cu))
            }
        }
        "prod" => {
            if k == 2 {
                format!("prod from {{{}}} {}", sm(&p[0], cu), g(&p[1], cu))
            } else {
                format!("prod from {{{} = {}}} to {{{}}} {}", sm(&p[0], cu), sm(&p[1], cu), sm(&p[2], cu), g(&p[3], cu))
            }
        }
        "int" => {
            if k == 2 {
                format!("int {} \" d\"{}", g(&p[0], cu), sm(&p[1], cu))
            } else {
                format!("int from {{{}}} to {{{}}} {} \" d\"{}", sm(&p[0], cu), sm(&p[1], cu), g(&p[2], cu), sm(&p[3], cu))
            }
        }
        "iint" | "iiint" => {
            let complete = (sym == "iint" && k == 3) || (sym == "iiint" && k == 4);
            if complete {
                let diffs: String = p[1..].iter().map(|x| format!(" \" d\"{}", sm(x, cu))).collect();
                format!("{sym} {}{}", g(&p[0], cu), diffs)
            } else {
                let diffs: String = p[2..].iter().map(|x| format!(" \" d\"{}", sm(x, cu))).collect();
                format!("{sym} from {{{}}} to {{{}}} {}", sm(&p[0], cu), sm(&p[1], cu), diffs)
            }
        }
        other => {
            let args: Vec<String> = p.iter().map(|x| g(x, cu)).collect();
            format!("{} {}", other, args.join(" "))
        }
    }
}

/// Opérande d'une multiplication/application : parenthèses VISIBLES si c'est un
/// composite GROUPÉ (parenthésé à l'origine). Sinon StarMath ne mettrait que des
/// `{}` invisibles → parenthèses perdues (« f(x+1) » → « f x+1 »). Aligne le
/// StarMath sur le LaTeX (cf. render.rs `child()` : `c.grouped` → `( … )`).
fn mul_operand_sm(n: &Node, cu: &Culture) -> String {
    let composite = !matches!(
        n.ntype,
        NType::Atom | NType::Paren | NType::Tuple | NType::Matrix | NType::Set | NType::Interval
    );
    if composite && n.grouped {
        format!("{{left ( {} right )}}", sm(n, cu))
    } else {
        g(n, cu)
    }
}

fn infix_sm(n: &Node, cu: &Culture) -> String {
    let sym = canon(n.sym.as_deref().unwrap_or(""));
    let p = n.parts.as_ref().unwrap();
    if sym == "/" {
        return format!("{{{}}} over {{{}}}", sm(&p[0], cu), sm(&p[1], cu));
    }
    if sym == "·parmi" {
        return format!("binom {} {}", g(&p[1], cu), g(&p[0], cu));
    }
    if sym == "^" {
        return format!("{}^{}", g(&p[0], cu), g(&p[1], cu));
    }
    if sym == "_" {
        return format!("{}_{}", g(&p[0], cu), g(&p[1], cu));
    }
    if sym == "*" || sym == "·apply" {
        let a0 = mul_operand_sm(&p[0], cu);
        let a1 = mul_operand_sm(&p[1], cu);
        return if n.implicit || sym == "·apply" {
            format!("{} {}", a0, a1)
        } else {
            format!("{} times {}", a0, a1)
        };
    }
    if sym == "·unit" {
        return format!("{} roman {}", sm(&p[0], cu), atom_sm(p[1].sym.as_deref()));
    }
    let a0 = g(&p[0], cu);
    let a1 = g(&p[1], cu);
    if let Some(t) = infix_tmpl(sym) {
        return fill2(t, [&a0, &a1]);
    }
    format!("{} {} {}", a0, sym, a1)
}

fn sm(n: &Node, cu: &Culture) -> String {
    match n.ntype {
        NType::Atom => atom_sm(n.sym.as_deref()),
        NType::Paren => {
            let p = n.parts.as_ref().unwrap();
            format!(
                "left {} {} right {}",
                n.lb.as_deref().unwrap_or("("),
                sm(&p[0], cu),
                n.rb.as_deref().unwrap_or(")")
            )
        }
        NType::Tuple => {
            let parts: Vec<String> = n.parts.as_ref().unwrap().iter().map(|x| sm(x, cu)).collect();
            format!("left ( {} right )", parts.join(" , "))
        }
        NType::List => n.parts.as_ref().unwrap().iter().map(|x| sm(x, cu)).collect::<Vec<_>>().join(" , "),
        NType::Set => {
            let parts: Vec<String> = n.parts.as_ref().unwrap().iter().map(|x| sm(x, cu)).collect();
            format!("lbrace {} rbrace", parts.join(" , "))
        }
        NType::Interval => {
            let p = n.parts.as_ref().unwrap();
            format!(
                "left {} {} {} {} right {}",
                n.lb.as_deref().unwrap_or(""),
                sm(&p[0], cu),
                cu.interval_sep,
                sm(&p[1], cu),
                n.rb.as_deref().unwrap_or("")
            )
        }
        NType::Matrix => {
            let (lb, rb) = if cu.matrix_env == "bmatrix" { ("[", "]") } else { ("(", ")") };
            let body = n
                .rows
                .as_ref()
                .unwrap()
                .iter()
                .map(|row| row.iter().map(|x| sm(x, cu)).collect::<Vec<_>>().join(" # "))
                .collect::<Vec<_>>()
                .join(" ## ");
            format!("left {} matrix{{ {} }} right {}", lb, body, rb)
        }
        NType::Postfix => postfix_sm(n, cu),
        NType::Prefix => prefix_sm(n, cu),
        NType::Nary => nary_sm(n, cu),
        NType::Infix => infix_sm(n, cu),
    }
}

/// AST → StarMath (entrée publique).
pub fn render_starmath(node: &Node, cu: &Culture) -> String {
    sm(node, cu)
}

#[cfg(test)]
mod tests {
    use crate::{analyze, reg};

    fn sm_of(src: &str) -> String {
        let cu = &reg().fr;
        let r = analyze(src, Some(cu));
        super::render_starmath(&r.ranked[0].node, cu)
    }

    #[test]
    fn apply_keeps_visible_parens_around_composite_arg() {
        // f(x+1) ne doit PAS perdre les parenthèses (bug « f x+1 »).
        assert_eq!(sm_of("f(x+1)"), "f {left ( x + 1 right )}");
        assert_eq!(sm_of("g(2x)"), "g {left ( 2 x right )}");
        assert_eq!(sm_of("2(x+1)"), "2 {left ( x + 1 right )}");
        // pas de sur-parenthésage des cas simples.
        assert_eq!(sm_of("f(x)"), "f {left ( x right )}");
        assert_eq!(sm_of("2x"), "2 x");
    }
}
