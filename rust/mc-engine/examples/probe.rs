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

//! Sonde de debug : `cargo run --example probe -- "<entrée>"` → décision + candidats.
use mc_engine::analyze;

fn main() {
    let src: String = std::env::args().skip(1).collect::<Vec<_>>().join(" ");
    for s in [src.clone(), format!("{src} ")] {
        let r = analyze(&s, None);
        println!("IN {s:?} -> {} (note={})", r.decision, r.has_note);
        for c in &r.ranked {
            println!("   {:.3}  {}", c.cost, c.latex);
        }
    }
}
