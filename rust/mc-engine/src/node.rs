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

//! Node de la forêt (AST) + Token du lexer — port de Node.cs / node.py.
//!
//! Types de nœud : atom | infix | prefix | nary | postfix | matrix | interval
//! | list | set | tuple | paren.

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum NType {
    Atom,
    Infix,
    Prefix,
    Nary,
    Postfix,
    Matrix,
    Interval,
    List,
    Set,
    Tuple,
    Paren,
}

impl NType {
    /// Nom textuel (utilisé par `score::shape` quand le nœud n'a pas de `sym`).
    pub fn name(&self) -> &'static str {
        match self {
            NType::Atom => "atom",
            NType::Infix => "infix",
            NType::Prefix => "prefix",
            NType::Nary => "nary",
            NType::Postfix => "postfix",
            NType::Matrix => "matrix",
            NType::Interval => "interval",
            NType::List => "list",
            NType::Set => "set",
            NType::Tuple => "tuple",
            NType::Paren => "paren",
        }
    }
}

#[derive(Clone, Debug)]
pub struct Node {
    pub ntype: NType,
    pub sym: Option<String>,
    pub parts: Option<Vec<Node>>,
    pub rows: Option<Vec<Vec<Node>>>, // matrix : lignes de cellules
    pub spaced: bool,
    pub implicit: bool,
    pub grouped: bool,
    pub hole: bool,
    pub choice: bool,
    pub sig: Option<String>,
    pub coh: Option<String>,
    pub ai: i32,
    pub delim: Option<String>,
    pub alt: bool,
    pub lb: Option<String>, // interval/paren : délimiteur gauche
    pub rb: Option<String>, // interval/paren : délimiteur droit
}

impl Default for Node {
    fn default() -> Self {
        Node {
            ntype: NType::Atom,
            sym: None,
            parts: None,
            rows: None,
            spaced: false,
            implicit: false,
            grouped: false,
            hole: false,
            choice: false,
            sig: None,
            coh: None,
            ai: 0,
            delim: None,
            alt: false,
            lb: None,
            rb: None,
        }
    }
}

impl Node {
    /// parts(n) de score : matrix → cellules aplaties ; sinon parts ; sinon vide.
    pub fn effective_parts(&self) -> Vec<&Node> {
        if let Some(p) = &self.parts {
            p.iter().collect()
        } else if let Some(r) = &self.rows {
            r.iter().flatten().collect()
        } else {
            Vec::new()
        }
    }
}

/// Sortie du lexer — port de Token.cs.
#[derive(Clone, Debug)]
pub struct Token {
    pub kind: String,
    pub sym: Option<String>,
    pub syms: Option<Vec<String>>, // lectures multiples (x2 → ^/_ ; R → [R, ℝ])
    pub spaced: bool,
    pub space_before: bool,
    pub virtual_: bool, // ) ou ] ajouté en fin de frappe
    pub num: bool,
    pub sticky: bool,
    pub implicit: bool,
    pub rank: i32, // sep : 0 = "," , 2 = ";"
    pub unit_word: bool,
    pub sign_sup: bool, // sup issu d'un postSign (0⁺) : jamais un chapeau orphelin
    pub coh: Option<String>,
}

impl Default for Token {
    fn default() -> Self {
        Token {
            kind: String::new(),
            sym: None,
            syms: None,
            spaced: false,
            space_before: false,
            virtual_: false,
            num: false,
            sticky: false,
            implicit: false,
            rank: 0,
            unit_word: false,
            sign_sup: false,
            coh: None,
        }
    }
}
