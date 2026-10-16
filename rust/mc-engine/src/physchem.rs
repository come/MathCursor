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

//! Mode physique-chimie — port de PhysChem.cs (ADR 2026-10-16-Feat-physics-chemistry-mode).
//!
//! Tout ce que le domaine ajoute au moteur vit ici ou dans chem.rs (grammaire des
//! formules). Données : data/engine/physchem.json (chargées dans le registre). Le
//! lexer n'appelle que ces points d'entrée ; parser, score et rendu ignorent le domaine.

use crate::node::Token;
use crate::reg;
use std::collections::HashSet;

/// Grandeur usuelle indicée en lettres (Ec → E_{\mathrm{c}}), mot entier exact.
pub fn quantity(word: &str) -> Option<&'static String> {
    reg().quantities.get(word)
}

/// Particule en `i` (e- → e⁻) : (longueur consommée, LaTeX), longueur 0 sinon.
/// Bornée comme une charge : pas de lettre avant, fin/espace/fermant après.
pub fn scan_particle(src: &[char], i: usize) -> (usize, String) {
    let before = if i > 0 { src[i - 1] } else { '\0' };
    if before.is_alphabetic() || before == '\\' {
        return (0, String::new());
    }
    for (k, v) in &reg().particles {
        let n = k.chars().count();
        if i + n <= src.len() && src[i..i + n].iter().copied().eq(k.chars()) && crate::chem::charge_end(src, i + n) {
            return (n, v.clone());
        }
    }
    (0, String::new())
}

// lettre de base d'un atome, lectures multiples comprises (R → [R, ℝ])
fn base_sym(t: &Token) -> Option<&str> {
    t.sym.as_deref().or_else(|| t.syms.as_ref().and_then(|s| s.first()).map(|s| s.as_str()))
}

/// Ordre indice/puissance des grandeurs numérotées, pour UNE saisie :
/// v0, T1 (chiffres subscriptFirst) → indice d'abord ; v2 → puissance d'abord ;
/// une lettre qui porte aussi un chiffre subscriptFirst ailleurs dans la saisie
/// lit l'indice d'abord partout (cohérence : v2 - v1 → v₂ − v₁).
pub struct ScriptOrder {
    indexed: HashSet<String>,
}

impl ScriptOrder {
    pub fn new(toks: &[Token]) -> ScriptOrder {
        let first = &reg().subscript_first;
        let mut indexed = HashSet::new();
        for w in toks.windows(2) {
            let digit = w[1].sym.as_deref().map(|d| first.contains(d)).unwrap_or(false);
            if w[0].kind == "atom" && w[1].num && !w[1].space_before && digit {
                if let Some(b) = base_sym(&w[0]) {
                    indexed.insert(b.to_string());
                }
            }
        }
        ScriptOrder { indexed }
    }

    /// Lectures ordonnées (préférée d'abord) pour la juxtaposition base+chiffre
    /// en `i`, ou None si elle reste une puissance seule (unité juste après un
    /// nombre : 5 m2 = 5 m²).
    pub fn order(&self, toks: &[Token], i: usize, unit_base: bool) -> Option<Vec<String>> {
        if unit_base && i > 0 && toks[i - 1].num {
            return None;
        }
        let r = reg();
        let (sub, sup) = (r.role["sub"].clone(), r.role["sup"].clone());
        let digit_first = toks[i + 1].sym.as_deref().map(|d| r.subscript_first.contains(d)).unwrap_or(false);
        let sub_first = digit_first || base_sym(&toks[i]).map(|b| self.indexed.contains(b)).unwrap_or(false);
        Some(if sub_first { vec![sub, sup] } else { vec![sup, sub] })
    }
}
