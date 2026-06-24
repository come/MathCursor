//! Service moteur PERSISTANT piloté par l'extension VSCode (remplace le sidecar
//! WASM .NET). Le registre (vocab/cultures) est bâti une fois au démarrage, puis
//! chaque requête répond en ~µs. Protocole stdio (parité ner-helper) :
//!
//!   stdin  : <culture>\t<src>          |  QUIT
//!   stdout : READY  puis 1 ligne JSON par requête :
//!            {"decision":"auto|popup|erreur","ranked":[{"latex":..,"cost":..}],"hasNote":bool}
//!
//! Un caractère inattendu (entrée libre) fait paniquer le lexer comme le C# lève
//! une exception : on le rattrape (catch_unwind) → "erreur", le process survit.

use mc_engine::{analyze, reg};
use serde_json::json;
use std::io::{self, BufRead, Write};
use std::panic::{catch_unwind, AssertUnwindSafe};

fn main() {
    let _ = reg(); // bâtit le registre maintenant (warm-up déterministe)
    let stdin = io::stdin();
    let mut reader = stdin.lock(); // verrou unique (buffer conservé entre requêtes)
    let mut out = io::stdout();
    let _ = writeln!(out, "READY");
    let _ = out.flush();

    let mut line = String::new();
    loop {
        line.clear();
        match reader.read_line(&mut line) {
            Ok(0) => break, // EOF
            Ok(_) => {}
            Err(_) => break,
        }
        let trimmed = line.trim_end_matches(['\r', '\n']);
        if trimmed == "QUIT" {
            break;
        }
        let mut it = trimmed.splitn(2, '\t');
        let culture = it.next().unwrap_or("fr");
        let src = it.next().unwrap_or("");
        let cu = if culture == "us" { &reg().us } else { &reg().fr };

        let json_line = match catch_unwind(AssertUnwindSafe(|| analyze(src, Some(cu)))) {
            Ok(r) => {
                let ranked: Vec<_> = r
                    .ranked
                    .iter()
                    .map(|c| json!({ "latex": c.latex, "cost": c.cost }))
                    .collect();
                json!({ "decision": r.decision, "ranked": ranked, "hasNote": r.has_note })
            }
            Err(_) => json!({ "decision": "erreur", "ranked": [], "hasNote": false }),
        };
        let _ = writeln!(out, "{json_line}");
        let _ = out.flush();
    }
}
