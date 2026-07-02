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

//! Couche « chaînes d'équations multilignes » — port de la couche Word
//! `adapter-vsto/Host/Blocks` (RelationMarkers, RelationLineDetector,
//! LatexTopLevelSplit, ChainComposer). PUR : aucune dépendance hôte.
//!
//! Vit HORS moteur (ADR 2026-06-10) : le moteur renvoie « erreur » sur une
//! relation en tête de ligne (volontaire) ; cette couche détecte le marqueur,
//! n'analyse que le RESTE, et compose un bloc aligné. Émet les DEUX rendus :
//! LaTeX `\begin{aligned}` (VSCode) et StarMath `matrix{…}` (LibreOffice).
//!
//! Divergence assumée vs Word : alignement UNIQUE au signe (le connecteur
//! préfixe le membre gauche), au lieu du double-alignement eqArr/OMML — plus
//! simple et robuste en LaTeX/StarMath (ADR 2026-06-25-Feat-chaining-port).

use crate::starmath::render_starmath;
use crate::{analyze, Culture};

/// (forme tapée, latex, starmath, connecteur?). Ordre = longueur DÉCROISSANTE
/// (plus-long-match : « <=> » avant « <= » avant « < »). Port de RelationMarkers.cs.
const MARKERS: &[(&str, &str, &str, bool)] = &[
    ("approx", "\\approx ", "approx", false),
    ("environ", "\\approx ", "approx", false),
    ("env", "\\approx ", "approx", false),
    ("<=>", "\\Leftrightarrow ", "dlrarrow", true),
    ("=>", "\\Rightarrow ", "drarrow", true),
    ("<=", "\\leq ", "<=", false),
    (">=", "\\geq ", ">=", false),
    ("!=", "\\neq ", "<>", false),
    ("⟺", "\\Leftrightarrow ", "dlrarrow", true),
    ("⇔", "\\Leftrightarrow ", "dlrarrow", true),
    ("⟹", "\\Rightarrow ", "drarrow", true),
    ("⇒", "\\Rightarrow ", "drarrow", true),
    ("≤", "\\leq ", "<=", false),
    ("≥", "\\geq ", ">=", false),
    ("≠", "\\neq ", "<>", false),
    ("≈", "\\approx ", "approx", false),
    ("=", "=", "=", false),
    ("<", "<", "<", false),
    (">", ">", ">", false),
];

/// Résultat de détection « ligne commence par un marqueur ».
pub struct RelMatch {
    pub typed: &'static str,
    pub marker_latex: &'static str,
    pub marker_starmath: &'static str,
    pub is_connector: bool,
    /// Le reste (l'expression à analyser), blancs de tête retirés, `\r\n` final retiré.
    pub rest: String,
}

/// Plus-long-match d'un marqueur au DÉBUT de `s` (déjà sans blancs de tête).
fn try_match(s: &str) -> Option<&'static (&'static str, &'static str, &'static str, bool)> {
    for m in MARKERS {
        let (typed, _, _, _) = *m;
        let tb = typed.as_bytes();
        let sb = s.as_bytes();
        if sb.len() < tb.len() {
            continue;
        }
        // marqueur-MOT (approx/environ/env) : insensible à la casse + frontière de mot.
        let is_word = tb.last().is_some_and(|b| b.is_ascii_alphabetic());
        let head = &sb[..tb.len()];
        let ok = if is_word {
            head.eq_ignore_ascii_case(tb)
        } else {
            head == tb
        };
        if !ok {
            continue;
        }
        if is_word {
            if let Some(c) = s[tb.len()..].chars().next() {
                if c.is_alphabetic() {
                    continue; // « approximation » ne matche pas « approx »
                }
            }
        }
        return Some(m);
    }
    None
}

/// Port de RelationLineDetector.TryDetect : `None` si la ligne ne commence pas
/// par un marqueur. Le marqueur seul (« = ») matche avec `rest` vide.
pub fn detect_relation_line(line: &str) -> Option<RelMatch> {
    let lead = line.len() - line.trim_start().len();
    let s = &line[lead..];
    if s.is_empty() {
        return None;
    }
    let &(typed, marker_latex, marker_starmath, is_connector) = try_match(s)?;
    let after = s[typed.len()..].trim_start_matches(' ');
    let rest = after.trim_end_matches(['\r', '\n']).to_string();
    Some(RelMatch {
        typed,
        marker_latex,
        marker_starmath,
        is_connector,
        rest,
    })
}

/// Commandes LaTeX de relation (point d'alignement top-level). Port de
/// LatexTopLevelSplit.RelationCommands.
const REL_CMDS: &[&str] = &[
    "leq", "geq", "neq", "sim", "equiv", "cong", "approx", "propto", "in", "notin", "subset",
    "subseteq", "supset", "supseteq", "to", "mapsto", "colon", "mid", "perp",
];

/// Scinde un LaTeX au PREMIER signe de relation top-level (profondeur 0 hors
/// `{}()[]` ; `\{`/`\}` ne comptent pas). Port de LatexTopLevelSplit.Split.
pub fn split_top_level_latex(latex: &str) -> (String, Option<String>) {
    if latex.is_empty() {
        return (String::new(), None);
    }
    let ch: Vec<char> = latex.chars().collect();
    let n = ch.len();
    let mut depth: i32 = 0;
    let mut i = 0usize;
    while i < n {
        let c = ch[i];
        if c == '\\' {
            let mut j = i + 1;
            while j < n && ch[j].is_alphabetic() {
                j += 1;
            }
            if j == i + 1 {
                i += 2; // \{ \} \, : skip le backslash + le char échappé
                continue;
            }
            let cmd: String = ch[i + 1..j].iter().collect();
            if depth == 0 && REL_CMDS.contains(&cmd.as_str()) {
                return (ch[..i].iter().collect(), Some(ch[i..].iter().collect()));
            }
            i = j;
            continue;
        }
        match c {
            '{' | '(' | '[' => depth += 1,
            '}' | ')' | ']' => depth -= 1,
            '=' | '<' | '>' if depth == 0 => {
                return (ch[..i].iter().collect(), Some(ch[i..].iter().collect()));
            }
            _ => {}
        }
        i += 1;
    }
    (latex.to_string(), None)
}

/// Tokens de relation StarMath (ce qu'émet `infix_sm`).
const SM_RELS: &[&str] = &[
    "=", "<", ">", "<=", ">=", "<>", "approx", "sim", "prop", "in", "notin", "subset", "subseteq",
    "supset", "supseteq", "nsubset", "equiv", "cong", "parallel", "ortho", "divides",
];

/// Équivalent StarMath de `split_top_level_latex` : 1er token-relation à
/// profondeur d'accolades 0 (les `{…}` groupent : `sum from {i = 1}` ignoré).
pub fn split_top_level_starmath(sm: &str) -> (String, Option<String>) {
    let toks: Vec<&str> = sm.split_whitespace().collect();
    let mut depth: i32 = 0;
    for (k, t) in toks.iter().enumerate() {
        if depth == 0 && SM_RELS.contains(t) {
            return (toks[..k].join(" "), Some(toks[k..].join(" ")));
        }
        for c in t.chars() {
            match c {
                '{' => depth += 1,
                '}' => depth -= 1,
                _ => {}
            }
        }
    }
    (sm.to_string(), None)
}

/// Une ligne source d'une chaîne : sa sténo + l'index du candidat choisi (popup).
pub struct ChainLine<'a> {
    pub steno: &'a str,
    pub index: usize,
}

/// Bloc composé : les deux rendus alignés.
pub struct ComposeResult {
    pub latex: String,
    pub starmath: String,
}

/// (latex, starmath) du candidat choisi pour `rest`. Déterministe (même
/// rest+index ⇒ même candidat) → préserve le choix popup sans le stocker.
fn pick(rest: &str, index: usize, cu: &Culture) -> (String, String) {
    let r = analyze(rest, Some(cu));
    if r.ranked.is_empty() {
        return (String::new(), String::new());
    }
    let c = &r.ranked[index.min(r.ranked.len() - 1)];
    (c.latex.clone(), render_starmath(&c.node, cu))
}


/// Compose les lignes en UN bloc aligné (LaTeX `aligned` + StarMath `matrix`).
/// Alignement unique au signe ; connecteur (⟺…) préfixé au membre gauche.
pub fn compose_chain(lines: &[ChainLine], cu: &Culture) -> ComposeResult {
    let mut latex_rows: Vec<String> = Vec::with_capacity(lines.len());
    let mut sm_rows: Vec<String> = Vec::with_capacity(lines.len());

    for ln in lines {
        // LaTeX : (membre gauche complet, membre droit avec son signe).
        // StarMath : 2 colonnes (lhs ; relation+rhs avec opérande gauche fantôme
        // `{}` — sinon l'opérateur StarMath rend un glyphe d'erreur).
        // conn_op = opérateur StarMath du connecteur (préfixe le lhs), ou None.
        let (left_l, rhs_l, conn_op, sl, relrhs): (String, String, Option<&str>, String, String) =
            match detect_relation_line(ln.steno) {
                // Équation complète (1ʳᵉ ligne) : scinde au signe top-level.
                None => {
                    let (lx, sx) = pick(ln.steno, ln.index, cu);
                    let (ll, lr) = split_top_level_latex(&lx);
                    let (s_lhs, s_rhs) = split_top_level_starmath(&sx);
                    (ll, lr.unwrap_or_default(), None, s_lhs, s_rhs.unwrap_or_default())
                }
                // Marqueur-RELATION (« = 2x ») : le signe EST l'alignement, lhs vide.
                Some(d) if !d.is_connector => {
                    let (lx, sx) = pick(&d.rest, ln.index, cu);
                    (
                        String::new(),
                        format!("{}{}", d.marker_latex, lx),
                        None,
                        String::new(),
                        format!("{} {}", d.marker_starmath, sx),
                    )
                }
                // Connecteur (« ⟺ x=3 ») : la flèche préfixe le membre gauche.
                Some(d) => {
                    let (lx, sx) = pick(&d.rest, ln.index, cu);
                    let (ll, lr) = split_top_level_latex(&lx);
                    let (s_lhs, s_rhs) = split_top_level_starmath(&sx);
                    (
                        format!("{}{}", d.marker_latex, ll),
                        lr.unwrap_or_default(),
                        Some(d.marker_starmath),
                        s_lhs,
                        s_rhs.unwrap_or_default(),
                    )
                }
            };

        // LaTeX : « {lhs} &{rhs} » (le & juste avant le signe de relation).
        latex_rows.push(format!("{left_l} &{rhs_l}"));

        // StarMath 2 colonnes : « alignr {lhs} # alignl {{} = rhs} ».
        let col1 = match conn_op {
            Some(op) => format!("{{}} {op} {{{sl}}}"), // connecteur (opérateur fantôme) + lhs
            None => sl,
        };
        let col2 = if relrhs.is_empty() {
            "{}".to_string()
        } else {
            format!("{{}} {relrhs}") // opérande gauche fantôme → l'opérateur de relation rend
        };
        sm_rows.push(format!("alignr {{{col1}}} # alignl {{{col2}}}"));
    }

    ComposeResult {
        latex: format!("\\begin{{aligned}}\n{}\n\\end{{aligned}}", latex_rows.join(" \\\\\n")),
        starmath: format!("matrix{{ {} }}", sm_rows.join(" ## ")),
    }
}

/// Position (octet) d'une accolade `{` NON fermée dans `text` (la plus externe),
/// ou None. Sert à reconnaître un ouvreur de système n'importe où (`f(x) = {…`).
pub fn find_unclosed_brace(text: &str) -> Option<usize> {
    let mut depth = 0i32;
    let mut pos = None;
    for (i, &b) in text.as_bytes().iter().enumerate() {
        if b == b'{' {
            if depth == 0 {
                pos = Some(i);
            }
            depth += 1;
        } else if b == b'}' && depth > 0 {
            depth -= 1;
            if depth == 0 {
                pos = None;
            }
        }
    }
    if depth > 0 {
        pos
    } else {
        None
    }
}

/// Détache une relation FINALE du préfixe (« f(x) = » → lhs « f(x) », « = »).
fn split_trailing_relation(prefix: &str) -> Option<(&str, &'static str, &'static str)> {
    let t = prefix.trim_end();
    for &(typed, latex, starmath, _is_conn) in MARKERS {
        if let Some(lhs) = t.strip_suffix(typed) {
            // marqueur-MOT : frontière de mot avant (sinon « xapprox » matcherait).
            if typed.bytes().last().is_some_and(|b| b.is_ascii_alphabetic())
                && lhs.chars().last().is_some_and(|c| c.is_alphabetic())
            {
                continue;
            }
            return Some((lhs.trim_end(), latex, starmath));
        }
    }
    None
}

/// Rendu du préfixe avant l'accolade (analysé « comme d'hab » par le moteur ;
/// relation finale rattachée). (latex, starmath), tous deux suffixés d'un espace.
fn render_prefix(prefix: &str, cu: &Culture) -> (String, String) {
    if prefix.is_empty() {
        return (String::new(), String::new());
    }
    match split_trailing_relation(prefix) {
        Some((lhs, rel_l, rel_s)) => {
            let (lx, sx) = pick(lhs, 0, cu);
            (format!("{lx}{rel_l} "), format!("{sx} {rel_s} "))
        }
        None => {
            let (lx, sx) = pick(prefix, 0, cu);
            (format!("{lx} "), format!("{sx} "))
        }
    }
}

/// Système d'équations : `line` contient une accolade `{` non fermée (ouvreur,
/// n'importe où — pas seulement en tête). Le PRÉFIXE (avant `{`) est analysé par
/// le moteur ; le RESTE (après `{`) est découpé par `;` en lignes (l'adapter
/// convertit les sauts de ligne Maj+Entrée en `;`), aligné au signe, et enveloppé
/// d'une accolade gauche seule. Hors moteur (ADR 2026-06-26-Feat-systems-matrix-model).
pub fn compose_system(line: &str, cu: &Culture) -> ComposeResult {
    let pos = match find_unclosed_brace(line) {
        Some(p) => p,
        None => return ComposeResult { latex: String::new(), starmath: String::new() },
    };
    let prefix = line[..pos].trim();
    let rows: Vec<ChainLine> = line[pos + 1..]
        .split(';')
        .map(|r| r.trim())
        .filter(|r| !r.is_empty())
        .map(|r| ChainLine { steno: r, index: 0 })
        .collect();
    if rows.is_empty() {
        return ComposeResult { latex: String::new(), starmath: String::new() };
    }
    let inner = compose_chain(&rows, cu);
    let (pre_l, pre_s) = render_prefix(prefix, cu);
    ComposeResult {
        // \right. = délimiteur droit invisible ; le préfixe (ex. « f(x) = ») précède.
        latex: format!("{pre_l}\\left\\{{ {} \\right.", inner.latex),
        // right none = pas de délimiteur droit (StarMath).
        starmath: format!("{pre_s}left lbrace {} right none", inner.starmath),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::reg;

    fn fr() -> &'static Culture {
        &reg().fr
    }

    #[test]
    fn detect_basic() {
        let d = detect_relation_line("= 2x").unwrap();
        assert_eq!(d.typed, "=");
        assert_eq!(d.rest, "2x");
        assert!(!d.is_connector);

        let d = detect_relation_line("<=> x = 3").unwrap();
        assert_eq!(d.typed, "<=>");
        assert_eq!(d.rest, "x = 3");
        assert!(d.is_connector);

        let d = detect_relation_line("≈ 0,5").unwrap();
        assert_eq!(d.marker_latex, "\\approx ");
        assert_eq!(d.rest, "0,5");
    }

    #[test]
    fn detect_greedy_and_word_boundary() {
        assert_eq!(detect_relation_line("<=> y").unwrap().typed, "<=>");
        assert_eq!(detect_relation_line("=> y").unwrap().typed, "=>");
        assert_eq!(detect_relation_line("approx 3,14").unwrap().typed, "approx");
        // frontière de mot : « approximation » / « environnement » ne matchent pas.
        assert!(detect_relation_line("approximation de pi").is_none());
        assert!(detect_relation_line("environnement").is_none());
    }

    #[test]
    fn detect_non_chain_lines() {
        assert!(detect_relation_line("f(x) = 2x").is_none()); // relation au milieu
        assert!(detect_relation_line("2x+1").is_none());
        assert!(detect_relation_line("-> 1/x").is_none()); // flèche fonction, pas marqueur
        assert!(detect_relation_line("   ").is_none());
        assert!(detect_relation_line("").is_none());
    }

    #[test]
    fn detect_marker_only_and_crlf() {
        assert_eq!(detect_relation_line("= ").unwrap().rest, "");
        assert_eq!(detect_relation_line("= 2x\r").unwrap().rest, "2x");
    }

    #[test]
    fn split_latex_cases() {
        assert_eq!(
            split_top_level_latex("f(x)=\\frac{1}{2}"),
            ("f(x)".into(), Some("=\\frac{1}{2}".into()))
        );
        assert_eq!(split_top_level_latex("x\\leq 5"), ("x".into(), Some("\\leq 5".into())));
        assert_eq!(split_top_level_latex("2x+y=5"), ("2x+y".into(), Some("=5".into())));
        assert_eq!(split_top_level_latex("x<1"), ("x".into(), Some("<1".into())));
        // pas de relation top-level (nichée / parenthésée / échappée).
        assert_eq!(split_top_level_latex("\\frac{a=b}{2}"), ("\\frac{a=b}{2}".into(), None));
        assert_eq!(split_top_level_latex("(a=b)"), ("(a=b)".into(), None));
        // accolades échappées ne masquent pas une relation réelle.
        assert_eq!(split_top_level_latex("\\{1,2\\}=S"), ("\\{1,2\\}".into(), Some("=S".into())));
    }

    #[test]
    fn split_starmath_top_level() {
        assert_eq!(split_top_level_starmath("x = 5"), ("x".into(), Some("= 5".into())));
        // relation nichée dans accolades (sum) ignorée.
        let (lhs, rhs) = split_top_level_starmath("sum from {i = 1} to {n} i = S");
        assert_eq!(rhs, Some("= S".into()));
        assert_eq!(lhs, "sum from {i = 1} to {n} i");
    }

    #[test]
    fn compose_two_line_chain() {
        // f(x)=2x+2-2  /  = 2x
        let lines = [
            ChainLine { steno: "f(x)=2x+2-2", index: 0 },
            ChainLine { steno: "= 2x", index: 0 },
        ];
        let r = compose_chain(&lines, fr());
        assert!(r.latex.starts_with("\\begin{aligned}"));
        assert!(r.latex.contains(" \\\\\n")); // séparateur de ligne
        assert!(r.latex.contains("&=")); // alignement au signe
        assert!(r.starmath.starts_with("matrix{"));
        assert!(r.starmath.contains(" ## ")); // séparateur de ligne matrix
        assert!(r.starmath.contains("alignr") && r.starmath.contains("alignl"));
        assert!(r.starmath.contains("{} =")); // relation = opérateur StarMath + opérande fantôme
    }

    #[test]
    fn compose_system_wraps_left_brace() {
        // { 2x+y=5 ; x-y=1  →  accolade gauche + lignes alignées au signe.
        let r = compose_system("{ 2x+y=5 ; x-y=1", fr());
        assert!(r.latex.starts_with("\\left\\{ \\begin{aligned}"));
        assert!(r.latex.ends_with("\\end{aligned} \\right."));
        assert!(r.latex.contains("&=")); // alignement au signe
        assert!(r.starmath.starts_with("left lbrace matrix{"));
        assert!(r.starmath.ends_with("} right none"));
        assert!(r.starmath.contains(" ## ")); // 2 lignes
    }

    #[test]
    fn compose_system_drops_empty_rows() {
        // ; en trop / espaces → lignes vides ignorées.
        let r = compose_system("{ x=1 ; ; ", fr());
        assert!(r.latex.starts_with("\\left\\{"));
        assert!(!r.starmath.contains(" ## ")); // une seule ligne
    }

    #[test]
    fn compose_system_generic_brace_with_prefix() {
        // f(x) = { 2x ; x  →  préfixe « f(x) = » rendu avant l'accolade.
        let r = compose_system("f(x) = { 2x ; x", fr());
        assert!(r.latex.contains("\\left\\{")); // accolade
        assert!(r.latex.starts_with("f(x)= ")); // préfixe + relation finale rattachée
        assert!(r.starmath.contains("left lbrace"));
        assert!(r.starmath.starts_with("f ")); // f(x) rendu en StarMath avant l'accolade
        // A { …  → préfixe « A » sans relation.
        let r2 = compose_system("A { 1 ; 2", fr());
        assert!(r2.latex.starts_with("A \\left\\{"));
    }

    #[test]
    fn find_unclosed_brace_cases() {
        assert_eq!(find_unclosed_brace("f(x) = { 2x ; x"), Some(7));
        assert_eq!(find_unclosed_brace("{ a ; b"), Some(0));
        assert_eq!(find_unclosed_brace("{a,b}"), None); // accolade fermée = ensemble
        assert_eq!(find_unclosed_brace("2x+1"), None);
    }

    #[test]
    fn compose_connector_prefixes_lhs() {
        let lines = [
            ChainLine { steno: "x=3", index: 0 },
            ChainLine { steno: "<=> 2x=6", index: 0 },
        ];
        let r = compose_chain(&lines, fr());
        assert!(r.latex.contains("\\Leftrightarrow")); // connecteur rendu, préfixe le lhs
        assert!(r.starmath.contains("dlrarrow")); // connecteur = opérateur StarMath (préfixe le lhs)
    }
}
