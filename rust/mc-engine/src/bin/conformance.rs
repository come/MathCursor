//! Runner de conformité Phase 2 : rejoue engine/tests/.../fixtures.json contre le
//! moteur Rust et compare décision + candidats (LaTeX) + note au contrat.
//! Vise 465/465 (anti-dérive C#/Python/Rust). Cf. ADR rust-unified-toolkit.

use mc_engine::{analyze, reg};
use serde_json::Value;
use std::panic::{catch_unwind, AssertUnwindSafe};

fn main() {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../engine/tests/MathCursor.Engine.Tests/fixtures.json"
    );
    let text = std::fs::read_to_string(path).expect("lecture fixtures.json");
    let fixtures: Vec<Value> = serde_json::from_str(&text).expect("parse fixtures.json");

    let mut fails: Vec<String> = Vec::new();
    let total = fixtures.len();

    for f in &fixtures {
        let input = f["in"].as_str().unwrap();
        let cu = if f.get("culture").and_then(|v| v.as_str()) == Some("us") {
            &reg().us
        } else {
            &reg().fr
        };
        let exp_dec = f["decision"].as_str().unwrap();
        let exp_cands: Vec<String> = f["cands"]
            .as_array()
            .unwrap()
            .iter()
            .map(|x| x.as_str().unwrap().to_string())
            .collect();
        let exp_note = f.get("note").and_then(|v| v.as_bool()).unwrap_or(false);

        let result = catch_unwind(AssertUnwindSafe(|| analyze(input, Some(cu))));
        let (got_dec, got_cands, got_note) = match result {
            Ok(r) => (
                r.decision.clone(),
                r.ranked.iter().map(|c| c.latex.clone()).collect::<Vec<_>>(),
                r.has_note,
            ),
            Err(_) => ("EXCEPTION".to_string(), vec!["<panic>".to_string()], false),
        };

        if got_dec != exp_dec || got_cands != exp_cands || got_note != exp_note {
            let culture_tag = f
                .get("culture")
                .and_then(|v| v.as_str())
                .unwrap_or("fr");
            fails.push(format!(
                "  IN {input:?} [{culture_tag}]\n     attendu {exp_dec} {exp_cands:?} note={exp_note}\n     obtenu  {got_dec} {got_cands:?} note={got_note}"
            ));
        }
    }

    println!("{}/{} OK ({} échec(s))", total - fails.len(), total, fails.len());
    for x in fails.iter().take(25) {
        println!("{x}");
    }
    std::process::exit(if fails.is_empty() { 0 } else { 1 });
}
