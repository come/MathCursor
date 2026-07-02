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

//! Moteur « forest » porté en Rust (transcription fidèle du C#/Python).
//!
//! src → candidats LaTeX classés + décision. PUR : aucune dépendance plateforme.
//! Registre global (VOCAB, splittable, role, cultures) bâti une fois, à la
//! première analyse, depuis les data JSON embarquées (data/engine/*.json).

pub mod chain;
pub mod culture;
pub mod engine;
pub mod lexer;
pub mod node;
pub mod parser;
pub mod render;
pub mod score;
pub mod segment;
pub mod starmath;
pub mod vocab;

pub use culture::Culture;
pub use engine::{analyze, AnalyzeResult, EngineCandidate};
pub use node::{NType, Node, Token};
pub use vocab::VocabEntry;

use serde_json::Value;
use std::collections::{HashMap, HashSet};
use std::sync::OnceLock;

// Data universelle embarquée (MÊME source que le moteur C#/Python).
const SYMBOLS_JSON: &str = include_str!("../../../data/engine/symbols.json");
const CULTURES_JSON: &str = include_str!("../../../data/engine/cultures.json");
const UNITS_JSON: &str = include_str!("../../../data/engine/units.json");

/// Constantes de looseness (doivent coïncider avec symbols.json `const`).
pub struct Consts {
    pub rel: f64,
    pub sum: f64,
    pub quant: f64,
    pub prod: f64,
    pub pow: f64,
    pub app: f64,
}

pub struct Registry {
    pub vocab: HashMap<String, VocabEntry>,
    pub splittable: HashSet<String>,
    pub role: HashMap<String, String>,
    pub sep: HashMap<char, i32>,
    pub units_compound: HashMap<String, String>,
    pub sameas: HashMap<String, String>, // alias → clé de base (×→*, ≤→<=…), pour StarMath
    pub consts: Consts,
    pub fr: Culture,
    pub us: Culture,
}

static REG: OnceLock<Registry> = OnceLock::new();

/// Registre global (lazy). 'static : vit toute la durée du process.
pub fn reg() -> &'static Registry {
    REG.get_or_init(build_registry)
}

// Génère les alias de PRÉFIXE non ambigus (ADR backlog moteur #2) : pour chaque
// mot-clé Vocab / alias alphabétique, tout préfixe >= 4 lettres qui n'est pas
// déjà une forme exacte et ne préfixe qu'UNE seule cible canonique devient un
// alias. Ambigus (« arc »→arcsin/arccos, « sub »→subset/subseteq) : non générés.
fn add_prefix_aliases(
    vocab: &HashMap<String, VocabEntry>,
    aliases: &HashMap<String, String>,
) -> HashMap<String, String> {
    const MIN_PREFIX_LEN: usize = 4;
    let is_alpha_word = |s: &str| !s.is_empty() && s.chars().all(|c| c.is_ascii_alphabetic());

    let mut forms: Vec<(String, String)> = Vec::new();
    for key in vocab.keys() {
        if is_alpha_word(key) {
            forms.push((key.clone(), key.clone()));
        }
    }
    for (k, v) in aliases {
        if is_alpha_word(k) {
            forms.push((k.clone(), v.clone()));
        }
    }

    let mut prefix_canons: HashMap<String, HashSet<String>> = HashMap::new();
    for (key, value) in &forms {
        let chars: Vec<char> = key.chars().collect();
        let klen = chars.len();
        let mut len = MIN_PREFIX_LEN;
        while len < klen {
            let p: String = chars[..len].iter().collect();
            if !vocab.contains_key(&p) && !aliases.contains_key(&p) {
                prefix_canons.entry(p).or_default().insert(value.clone());
            }
            len += 1;
        }
    }

    let mut merged = aliases.clone();
    for (p, set) in &prefix_canons {
        if set.len() == 1 {
            merged.insert(p.clone(), set.iter().next().unwrap().clone());
        }
    }
    merged
}

fn build_registry() -> Registry {
    let symbols: Value = serde_json::from_str(SYMBOLS_JSON).expect("symbols.json");
    let cultures: Value = serde_json::from_str(CULTURES_JSON).expect("cultures.json");
    let units: Value = serde_json::from_str(UNITS_JSON).expect("units.json");

    // consts.looseness
    let loose_obj = &symbols["const"]["looseness"];
    let mut loose_map: HashMap<String, f64> = HashMap::new();
    for (k, v) in loose_obj.as_object().unwrap() {
        loose_map.insert(k.clone(), v.as_f64().unwrap());
    }
    let consts = Consts {
        rel: loose_map["REL"],
        sum: loose_map["SUM"],
        quant: loose_map["QUANT"],
        prod: loose_map["PROD"],
        pow: loose_map["POW"],
        app: loose_map["APP"],
    };

    let mut vocab: HashMap<String, VocabEntry> = HashMap::new();
    let syms = symbols["symbols"].as_object().unwrap();

    // pass 1 : entrées directes
    for (key, j) in syms {
        if j.get("sameAs").is_none() {
            vocab.insert(key.clone(), vocab::build_entry(j, &loose_map));
        }
    }
    // pass 2 : sameAs = clone + overrides (+ table alias→base pour StarMath)
    let mut sameas: HashMap<String, String> = HashMap::new();
    for (key, j) in syms {
        if let Some(base) = j.get("sameAs").and_then(|v| v.as_str()) {
            let mut e = vocab[base].clone();
            if let Some(ws) = j.get("wordSpace").and_then(|v| v.as_bool()) {
                e.word_space = ws;
            }
            vocab.insert(key.clone(), e);
            sameas.insert(key.clone(), base.to_string());
        }
    }

    // unités : un SYMBOLE prime sur l'unité homonyme (ne remplir que le libre).
    let mut seen: HashSet<String> = HashSet::new();
    for cat in units["categories"].as_array().unwrap() {
        for w in cat.as_array().unwrap() {
            let w = w.as_str().unwrap().to_string();
            if seen.insert(w.clone()) && !vocab.contains_key(&w) {
                vocab.insert(
                    w,
                    VocabEntry {
                        unit_word: true,
                        ..Default::default()
                    },
                );
            }
        }
    }

    // splittable (liste data)
    let mut splittable: HashSet<String> = HashSet::new();
    for w in symbols["splittable"].as_array().unwrap() {
        splittable.insert(w.as_str().unwrap().to_string());
    }

    // role : entrée canonique par rôle (chaque rôle est porté par une seule clé)
    let mut role: HashMap<String, String> = HashMap::new();
    for (key, e) in &vocab {
        if e.implicit {
            role.insert("implicit".into(), key.clone());
        }
        if e.sup {
            role.insert("sup".into(), key.clone());
        }
        if e.sub {
            role.insert("sub".into(), key.clone());
        }
        if e.unit_op {
            role.insert("unitOp".into(), key.clone());
        }
        if e.apply {
            role.insert("apply".into(), key.clone());
        }
    }

    // sep
    let mut sep: HashMap<char, i32> = HashMap::new();
    sep.insert(',', 0);
    sep.insert(';', 2);

    // units compound
    let mut units_compound: HashMap<String, String> = HashMap::new();
    for (k, v) in units["compound"].as_object().unwrap() {
        units_compound.insert(k.clone(), v.as_str().unwrap().to_string());
    }

    // cultures
    let aliases = &cultures["aliases"];
    let make_aliases = |lang_key: &str| -> HashMap<String, String> {
        let mut merged: HashMap<String, String> = HashMap::new();
        for (k, v) in aliases["generic"].as_object().unwrap() {
            merged.insert(k.clone(), v.as_str().unwrap().to_string());
        }
        for (k, v) in aliases[lang_key].as_object().unwrap() {
            merged.insert(k.clone(), v.as_str().unwrap().to_string());
        }
        for (k, target) in &merged {
            if !vocab.contains_key(target) {
                panic!("alias '{k}' -> cible inconnue '{target}'");
            }
        }
        add_prefix_aliases(&vocab, &merged)
    };
    let make_culture = |key: &str, lang_key: &str| -> Culture {
        let c = &cultures["cultures"][key];
        Culture {
            decimals_in: c["decimalsIn"]
                .as_array()
                .unwrap()
                .iter()
                .map(|x| x.as_str().unwrap().chars().next().unwrap())
                .collect(),
            decimal_tex: c["decimalTex"].as_str().unwrap().to_string(),
            interval_sep: c["intervalSep"].as_str().unwrap().to_string(),
            matrix_env: c["matrixEnv"].as_str().unwrap().to_string(),
            aliases: make_aliases(lang_key),
        }
    };
    let fr = make_culture("fr", "fr");
    let us = make_culture("us", "en");

    Registry {
        vocab,
        splittable,
        role,
        sep,
        units_compound,
        sameas,
        consts,
        fr,
        us,
    }
}
