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

//! Orchestrateur GÉNÉRIQUE — port de ForestEngine.cs / engine.py.
//!
//! src → candidats classés + décision :
//!   lex → SEGMENTE aux coupes → forêt par segment (borné) → recombine
//!   → filtre coupes → coût → tri → dédoublonnage → popup/auto.

use crate::culture::Culture;
use crate::lexer::lex_all;
use crate::node::{NType, Node, Token};
use crate::parser;
use crate::reg;
use crate::render::render;
use crate::score;
use crate::segment;
use std::cell::Cell;

const POPUP_GAP: f64 = 2.0;
const COMBINE_CAP: usize = 128;
const MAX_SHOW: usize = 5;
const CAT: [usize; 8] = [1, 1, 2, 5, 14, 42, 132, 429];
const NOTE: &str = "expression dense/ambiguë — ajoutez des espaces ou des parenthèses pour préciser.";
const SPLIT_PENALTY: f64 = 3.0;

#[derive(Clone, Debug)]
pub struct EngineCandidate {
    pub latex: String,
    pub cost: f64,
    pub node: Node,
}

#[derive(Clone, Debug)]
pub struct AnalyzeResult {
    pub decision: String, // "auto" | "popup" | "erreur"
    pub ranked: Vec<EngineCandidate>,
    pub has_note: bool,
}

fn catalan(n: usize) -> usize {
    if n < CAT.len() {
        CAT[n]
    } else {
        429
    }
}

fn cmp_cost(a: f64, b: f64) -> std::cmp::Ordering {
    a.partial_cmp(&b).unwrap_or(std::cmp::Ordering::Equal)
}

struct Engine<'a> {
    cu: &'a Culture,
    deep_note: Cell<bool>,
}

impl<'a> Engine<'a> {
    fn on_group(&self, interior: &[Token]) -> Vec<Node> {
        let (parses, note) = self.assemble(interior);
        if note.is_some() {
            self.deep_note.set(true);
        }
        parses
    }

    fn forest(&self, toks: &[Token]) -> Vec<Node> {
        let og = |interior: &[Token]| -> Vec<Node> { self.on_group(interior) };
        parser::parse(toks, Some(&og), self.cu)
    }

    fn parses_of(&self, toks: &[Token]) -> Vec<Node> {
        self.forest(toks)
            .into_iter()
            .filter(|p| !score::crosses_cut(p))
            .collect()
    }

    fn best_of(&self, toks: &[Token]) -> Option<Node> {
        let mut best: Option<Node> = None;
        let mut bc = f64::INFINITY;
        for p in self.parses_of(toks) {
            let c = score::cost(&p);
            if c < bc {
                bc = c;
                best = Some(p);
            }
        }
        best
    }

    fn cheapest(&self, lst: &[Node]) -> Node {
        let pool: Vec<&Node> = lst.iter().filter(|p| !score::crosses_cut(p)).collect();
        let src: Vec<&Node> = if !pool.is_empty() {
            pool
        } else {
            lst.iter().collect()
        };
        let mut best = src[0];
        for p in &src[1..] {
            if score::cost(p) < score::cost(best) {
                best = p;
            }
        }
        best.clone()
    }

    fn distinct_prod(&self, lists: &[Vec<Node>]) -> usize {
        let mut m: Vec<(String, usize)> = Vec::new();
        for (i, c) in lists.iter().enumerate() {
            let k = if !c.is_empty() && c[0].sig.is_some() {
                format!("s:{}", c[0].sig.as_deref().unwrap())
            } else {
                format!("u:{i}")
            };
            if !m.iter().any(|(kk, _)| kk == &k) {
                m.push((k, c.len()));
            }
        }
        let mut p = 1usize;
        for (_, v) in &m {
            p = p.saturating_mul(*v);
        }
        p
    }

    fn window_of(&self, lst: &[Node]) -> Vec<Node> {
        let pool: Vec<&Node> = lst.iter().filter(|p| !score::crosses_cut(p)).collect();
        let src: Vec<&Node> = if !pool.is_empty() {
            pool
        } else {
            lst.iter().collect()
        };
        let mut best = f64::INFINITY;
        for p in &src {
            best = best.min(score::cost(p));
        }
        src.into_iter()
            .filter(|p| score::cost(p) < best + POPUP_GAP)
            .cloned()
            .collect()
    }

    fn recombine(
        &self,
        lists: Vec<Vec<Node>>,
        ops: &[Token],
        mut note: Option<&'static str>,
    ) -> (Vec<Node>, Option<&'static str>) {
        for c in &lists {
            if c.is_empty() {
                return (vec![], note);
            }
        }
        let mut lists = lists;
        if !ops.is_empty() {
            lists = lists.iter().map(|l| self.window_of(l)).collect();
        }
        if !ops.is_empty() {
            let br = catalan(ops.len());
            if br.saturating_mul(self.distinct_prod(&lists)) > COMBINE_CAP {
                note = Some(NOTE);
                let mut order: Vec<usize> = (0..lists.len()).collect();
                order.sort_by(|a, b| lists[*b].len().cmp(&lists[*a].len()));
                for i in order {
                    if br.saturating_mul(self.distinct_prod(&lists)) <= COMBINE_CAP {
                        break;
                    }
                    let cheap = self.cheapest(&lists[i]);
                    lists[i] = vec![cheap];
                }
            }
        }
        let parses: Vec<Node> = segment::combine(&lists, ops)
            .into_iter()
            .filter(|p| !score::crosses_cut(p))
            .collect();
        (parses, note)
    }

    fn fold(&self, seg: &[Token]) -> Option<Node> {
        let (operands, ops) = segment::split_operands(seg);
        let mut node = self.best_of(&operands[0]);
        let mut k = 0;
        while k < ops.len() && node.is_some() {
            let right = self.best_of(&operands[k + 1]);
            match right {
                None => return None,
                Some(r) => {
                    node = Some(Node {
                        ntype: NType::Infix,
                        sym: ops[k].sym.clone(),
                        spaced: ops[k].spaced,
                        parts: Some(vec![node.take().unwrap(), r]),
                        ..Default::default()
                    });
                }
            }
            k += 1;
        }
        node
    }

    fn fold_smart(&self, seg: &[Token]) -> (Vec<Node>, Option<&'static str>) {
        let (parts, ops) = segment::split_loosest(seg);
        if !ops.is_empty() && parts.iter().all(|p| !p.is_empty()) {
            let mut lists: Vec<Vec<Node>> = Vec::new();
            let mut ok = true;
            for p in &parts {
                let (pr, _n) = self.assemble(p);
                if pr.is_empty() {
                    ok = false;
                    break;
                }
                lists.push(pr);
            }
            if ok {
                if ops.len() < segment::MAX_CHAIN {
                    let (rec, rnote) = self.recombine(lists, &ops, Some(NOTE));
                    if !rec.is_empty() {
                        return (rec, rnote);
                    }
                } else {
                    let mut node = self.cheapest(&lists[0]);
                    for k in 0..ops.len() {
                        node = Node {
                            ntype: NType::Infix,
                            sym: ops[k].sym.clone(),
                            spaced: ops[k].spaced,
                            parts: Some(vec![node, self.cheapest(&lists[k + 1])]),
                            ..Default::default()
                        };
                    }
                    return (vec![node], Some(NOTE));
                }
            }
        }
        match self.fold(seg) {
            Some(f) => (vec![f], Some(NOTE)),
            None => (vec![], Some(NOTE)),
        }
    }

    fn assemble(&self, toks: &[Token]) -> (Vec<Node>, Option<&'static str>) {
        // 1) RELATIONS = coupe la plus forte
        let (rel_segs, rel_ops) = segment::split_rel(toks);
        if !rel_ops.is_empty() {
            if rel_segs[0].is_empty() {
                return (vec![], None);
            }
            if rel_ops.len() >= segment::MAX_CHAIN {
                return self.fold_smart(toks);
            }
            let mut note: Option<&'static str> = None;
            let mut lists: Vec<Vec<Node>> = Vec::new();
            for s in &rel_segs {
                let (pr, n) = self.assemble(s);
                if n.is_some() {
                    note = n;
                }
                lists.push(pr);
            }
            return self.recombine(lists, &rel_ops, note);
        }

        // 2) N-AIRE en tête : forêt entière, pas de segmentation
        if !toks.is_empty() && toks[0].kind == "nary" && segment::chain_len(toks) < segment::MAX_CHAIN {
            let parses = self.parses_of(toks);
            return (parses, None);
        }

        // 3) segmentation aux signes espacés + repli si trop long
        let (segs, ops) = segment::split(toks);
        if ops.len() >= segment::MAX_CHAIN {
            return self.fold_smart(toks);
        }
        let mut note3: Option<&'static str> = None;
        let mut lists3: Vec<Vec<Node>> = Vec::new();
        for seg in &segs {
            if segment::chain_len(seg) < segment::MAX_CHAIN {
                lists3.push(self.forest(seg)); // forêt BRUTE (recombine filtrera)
            } else {
                note3 = Some(NOTE);
                lists3.push(self.fold_smart(seg).0);
            }
        }
        self.recombine(lists3, &ops, note3)
    }

    fn pair_skeletons(
        &self,
        allp: &[(Node, f64)],
        kept: Vec<(String, f64, Node)>,
    ) -> Vec<(String, f64, Node)> {
        let mut best_idx = 0usize;
        let mut best_c = f64::INFINITY;
        for (idx, (n, off)) in allp.iter().enumerate() {
            let c = score::cost(n) + off;
            if c < best_c {
                best_c = c;
                best_idx = idx;
            }
        }
        let n = &allp[best_idx].0;
        if n.ntype != NType::Nary || n.sym.is_none() || n.parts.is_none() {
            return kept;
        }
        if reg().vocab[n.sym.as_deref().unwrap()].variants.is_none() {
            return kept;
        }
        let n_parts = n.parts.as_ref().unwrap();
        // Le frère est TOUJOURS un squelette (à trous). Selon que le meilleur parse
        // a des trous (INCOMPLET, ADR 2026-06-12 : frère d'AUTRE arité) ou est une
        // forme COURTE complète (ADR 2026-06-22 : frère PLUS LONG), on ne cherche
        // pas la même chose.
        let incomplete = n_parts.iter().any(|c| c.hole);

        let mut sib_idx: Option<usize> = None;
        let mut sib_c = f64::INFINITY;
        for (idx, (m, off)) in allp.iter().enumerate() {
            if m.ntype != NType::Nary || m.sym != n.sym || m.parts.is_none() {
                continue;
            }
            let m_parts = m.parts.as_ref().unwrap();
            if !m_parts.iter().any(|c| c.hole) {
                continue;
            }
            let skip = if incomplete {
                m_parts.len() == n_parts.len()
            } else {
                m_parts.len() <= n_parts.len()
            };
            if skip {
                continue;
            }
            let c = score::cost(m) + off;
            if c < sib_c {
                sib_c = c;
                sib_idx = Some(idx);
            }
        }
        let sib_idx = match sib_idx {
            Some(i) => i,
            None => return kept,
        };

        // INCOMPLET : forme LONGUE présélectionnée (sélection de gabarit).
        // COMPLET : forme COURTE (ce qui est tapé) en tête, longue en alternative.
        let mut pair_src: Vec<(usize, f64)> = vec![(best_idx, best_c), (sib_idx, sib_c)];
        if incomplete {
            pair_src.sort_by(|a, b| {
                let la = allp[a.0].0.parts.as_ref().map(|p| p.len()).unwrap_or(0);
                let lb = allp[b.0].0.parts.as_ref().map(|p| p.len()).unwrap_or(0);
                lb.cmp(&la)
            });
        }
        let pair: Vec<(String, f64, Node)> = pair_src
            .iter()
            .map(|(i, c)| {
                let node = allp[*i].0.clone();
                (render(&node, self.cu), *c, node)
            })
            .collect();
        let mut outk = pair;
        for k in kept {
            if outk.iter().all(|o| o.0 != k.0) {
                outk.push(k);
            }
        }
        outk.truncate(MAX_SHOW);
        outk
    }

    fn finish(&self, allp: Vec<(Node, f64)>, note: Option<&'static str>) -> AnalyzeResult {
        if allp.is_empty() {
            return AnalyzeResult {
                decision: "erreur".into(),
                ranked: vec![],
                has_note: false,
            };
        }
        let mut cands: Vec<(String, f64, Node)> = allp
            .iter()
            .map(|(n, off)| (render(n, self.cu), score::cost(n) + off, n.clone()))
            .collect();
        cands.sort_by(|a, b| cmp_cost(a.1, b.1)); // tri stable

        let mut seen: std::collections::HashSet<String> = std::collections::HashSet::new();
        let mut ranked: Vec<(String, f64, Node)> = Vec::new();
        for (latex, c, nd) in cands {
            if seen.insert(latex.clone()) {
                ranked.push((latex, c, nd));
            }
        }
        let best = ranked[0].1;
        let win: Vec<&(String, f64, Node)> =
            ranked.iter().filter(|r| r.1 < best + POPUP_GAP).collect();
        let kept_n = win.len().min(MAX_SHOW);
        let kept: Vec<(String, f64, Node)> = win[..kept_n].iter().map(|r| (*r).clone()).collect();
        let has_note = note.is_some() || win.len() > MAX_SHOW;
        let kept = self.pair_skeletons(&allp, kept);
        let decision = if kept.len() > 1 { "popup" } else { "auto" };
        AnalyzeResult {
            decision: decision.into(),
            ranked: kept
                .into_iter()
                .map(|(latex, cost, node)| EngineCandidate { latex, cost, node })
                .collect(),
            has_note,
        }
    }

    fn run(&self, src: &str) -> AnalyzeResult {
        self.deep_note.set(false);
        let mut streams: Vec<(Vec<Node>, usize, Option<&'static str>)> = Vec::new();
        let mut max_s = 0usize;
        for (toks, splits) in lex_all(src, self.cu) {
            let (parses, note) = self.assemble(&toks);
            if parses.is_empty() {
                continue;
            }
            streams.push((parses, splits, note));
            if splits > max_s {
                max_s = splits;
            }
        }
        let mut allp: Vec<(Node, f64)> = Vec::new();
        let mut note: Option<&'static str> = None;
        for (parses, splits, n) in streams {
            let off = SPLIT_PENALTY * (max_s - splits) as f64;
            for p in parses {
                allp.push((p, off));
            }
            if splits == max_s && n.is_some() {
                note = n;
            }
        }
        let final_note = note.or(if self.deep_note.get() { Some(NOTE) } else { None });
        self.finish(allp, final_note)
    }
}

/// Analyse `src` selon la culture `cu` (FR par défaut).
pub fn analyze(src: &str, cu: Option<&Culture>) -> AnalyzeResult {
    let culture = cu.unwrap_or(&reg().fr);
    let eng = Engine {
        cu: culture,
        deep_note: Cell::new(false),
    };
    eng.run(src)
}
