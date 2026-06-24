//! Service NER PERSISTANT piloté par l'extension VSCode (remplace ner-helper
//! .NET). Charge le modèle une fois puis répond aux requêtes de détection.
//! Protocole stdio IDENTIQUE au ner-helper (parité ner.ts) :
//!
//!   argv[0] = dossier du modèle (tokenizer.json + model_quantized.onnx)
//!   stdin   : DETECT\t<caret>\t<text>   |  QUIT
//!   stdout  : READY | FATAL | ZONE\t<s>\t<e> | NONE
//!
//! caret + offsets renvoyés en UNITÉS UTF-16 (comme l'hôte).

use mc_ner::NerDetector;
use std::io::{self, BufRead, Write};

fn main() {
    let model_dir = std::env::args()
        .nth(1)
        .unwrap_or_else(|| r"D:\Software\MathCursor\models\latest".to_string());

    let mut det = match NerDetector::new(&model_dir) {
        Ok(d) => d,
        Err(e) => {
            eprintln!("[ner] load fail: {e}");
            println!("FATAL");
            let _ = io::stdout().flush();
            std::process::exit(1);
        }
    };
    // Warm-up (1ʳᵉ inférence) avant d'annoncer READY.
    let _ = det.respond("x = 1", 5);

    let stdin = io::stdin();
    let mut reader = stdin.lock();
    let mut out = io::stdout();
    println!("READY");
    let _ = out.flush();

    let mut line = String::new();
    loop {
        line.clear();
        match reader.read_line(&mut line) {
            Ok(0) => break,
            Ok(_) => {}
            Err(_) => break,
        }
        let trimmed = line.trim_end_matches(['\r', '\n']);
        let mut parts = trimmed.splitn(3, '\t');
        match parts.next() {
            Some("QUIT") => break,
            Some("DETECT") => {
                let caret = parts.next().and_then(|c| c.parse::<usize>().ok()).unwrap_or(0);
                let text = parts.next().unwrap_or("");
                let resp = match det.respond(text, caret) {
                    Some((s, e)) if e > s => format!("ZONE\t{s}\t{e}"),
                    _ => "NONE".to_string(),
                };
                println!("{resp}");
                let _ = out.flush();
            }
            _ => {}
        }
    }
}
