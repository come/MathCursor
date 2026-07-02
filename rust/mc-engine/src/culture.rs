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
