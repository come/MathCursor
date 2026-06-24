//! Smoke test de la stack NER : charge tokenizer.json + model_quantized.onnx,
//! tokenise une phrase, lance l'inférence, imprime les labels BIO par token.
//! `cargo run --example smoke -p mc-ner -- "<phrase>"`
use ort::session::Session;
use ort::value::Value;
use tokenizers::Tokenizer;

fn main() -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let model_dir = concat!(env!("CARGO_MANIFEST_DIR"), "/../../models/latest");
    let text: String = {
        let a: Vec<String> = std::env::args().skip(1).collect();
        if a.is_empty() { "on a f(x) = 2x + 1 et c'est tout".into() } else { a.join(" ") }
    };

    let tok = Tokenizer::from_file(format!("{model_dir}/tokenizer.json"))?;
    let enc = tok.encode(text.as_str(), true)?;
    let ids: Vec<i64> = enc.get_ids().iter().map(|&x| x as i64).collect();
    let offs = enc.get_offsets();
    let n = ids.len();
    let mask: Vec<i64> = vec![1; n];

    println!("tokens={n}");
    for (i, t) in enc.get_tokens().iter().enumerate() {
        println!("  [{i}] id={} off={:?} tok={t:?}", ids[i], offs[i]);
    }

    let mut session = Session::builder()?.commit_from_file(format!("{model_dir}/model_quantized.onnx"))?;
    let ids_v = Value::from_array(([1usize, n], ids))?;
    let mask_v = Value::from_array(([1usize, n], mask))?;
    let outputs = session.run(ort::inputs!["input_ids" => ids_v, "attention_mask" => mask_v])?;

    let (shape, data) = outputs[0].try_extract_tensor::<f32>()?;
    println!("logits shape={shape:?}");
    let classes = shape[shape.len() - 1] as usize;
    let labels = ["O", "B-MATH", "I-MATH"];
    for i in 0..n {
        let base = i * classes;
        let row = &data[base..base + classes];
        let (am, _) = row.iter().enumerate().fold((0usize, f32::MIN), |(bi, bv), (j, &v)| if v > bv { (j, v) } else { (bi, bv) });
        println!("  [{i}] {} {:?}", labels.get(am).unwrap_or(&"?"), row);
    }
    Ok(())
}
