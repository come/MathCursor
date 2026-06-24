//! Culture moteur — port d'EngineCulture.cs / culture.py (réglages + alias).
//!
//! Construite depuis data/engine/cultures.json. La validation « chaque cible
//! d'alias est une clé canonique de Vocab » est faite au build du registre.

use std::collections::HashMap;

#[derive(Clone, Debug)]
pub struct Culture {
    pub decimals_in: Vec<char>,
    pub decimal_tex: String,
    pub interval_sep: String,
    pub matrix_env: String,
    pub aliases: HashMap<String, String>,
}

impl Culture {
    pub fn canon(&self, w: &str) -> String {
        self.aliases
            .get(w)
            .cloned()
            .unwrap_or_else(|| w.to_string())
    }
}
