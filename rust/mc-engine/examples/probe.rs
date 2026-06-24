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
