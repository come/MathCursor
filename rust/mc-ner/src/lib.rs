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

//! Détecteur de zone math (NER) — port de MathNerDetector.cs / ZoneRefiner.cs.
//!
//! DistilBERT-multilingual PRUNED (vocab 3569), token-classification BIO
//! (O / B-MATH / I-MATH). Tokenizer NATIF du modèle (`tokenizer.json` via crate
//! `tokenizers`) → bons ids ([CLS]=2, [SEP]=3…), contrairement au C# qui codait
//! en dur les ids du modèle 119k. Inférence ONNX via crate `ort`.
//!
//! Offsets : le crate `tokenizers` rend des offsets en OCTETS (UTF-8) ; l'hôte
//! (VSCode Range, C#) raisonne en UNITÉS UTF-16. On convertit en sortie.

pub mod zone_refiner;

use ort::session::Session;
use ort::value::Value;
use tokenizers::Tokenizer;

const MAX_TOKENS: usize = 128;
const DEFAULT_THRESHOLD: f64 = 0.85;

/// Zone détectée — offsets en UNITÉS UTF-16 (comme l'hôte).
#[derive(Clone, Debug)]
pub struct DetectedZone {
    pub start: usize,
    pub end: usize,
    pub confidence: f64,
}

pub struct NerDetector {
    tokenizer: Tokenizer,
    session: Session,
    threshold: f64,
    skip_ids: [u32; 4], // [PAD], [CLS], [SEP], [MASK] — [UNK] passe (peut être du math)
}

type BoxErr = Box<dyn std::error::Error + Send + Sync>;

impl NerDetector {
    pub fn new(model_dir: &str) -> Result<Self, BoxErr> {
        let tokenizer = Tokenizer::from_file(format!("{model_dir}/tokenizer.json"))?;
        let session = Session::builder()?.commit_from_file(format!("{model_dir}/model_quantized.onnx"))?;
        let id = |t: &str| tokenizer.token_to_id(t).unwrap_or(u32::MAX);
        let skip_ids = [id("[PAD]"), id("[CLS]"), id("[SEP]"), id("[MASK]")];
        Ok(Self { tokenizer, session, threshold: DEFAULT_THRESHOLD, skip_ids })
    }

    /// Détecte les zones math (offsets UTF-16). Renvoie vide en cas d'erreur
    /// (parité C# : Detect attrape et renvoie une liste vide).
    pub fn detect(&mut self, text: &str) -> Vec<DetectedZone> {
        if text.is_empty() {
            return Vec::new();
        }
        let enc = match self.tokenizer.encode(text, true) {
            Ok(e) => e,
            Err(_) => return Vec::new(),
        };
        let ids_all = enc.get_ids();
        if ids_all.is_empty() {
            return Vec::new();
        }
        let n = ids_all.len().min(MAX_TOKENS);
        let ids: Vec<i64> = ids_all[..n].iter().map(|&x| x as i64).collect();
        let mask: Vec<i64> = vec![1; n];

        let ids_v = match Value::from_array(([1usize, n], ids)) {
            Ok(v) => v,
            Err(_) => return Vec::new(),
        };
        let mask_v = match Value::from_array(([1usize, n], mask)) {
            Ok(v) => v,
            Err(_) => return Vec::new(),
        };
        let outputs = match self
            .session
            .run(ort::inputs!["input_ids" => ids_v, "attention_mask" => mask_v])
        {
            Ok(o) => o,
            Err(_) => return Vec::new(),
        };
        let (shape, data) = match outputs[0].try_extract_tensor::<f32>() {
            Ok(t) => t,
            Err(_) => return Vec::new(),
        };
        let classes = shape[shape.len() - 1] as usize;

        let b2u = byte_to_utf16(text);
        let offsets = enc.get_offsets();

        // Décodage BIO → spans (offsets UTF-16), confiance = moyenne des tokens.
        let mut spans: Vec<DetectedZone> = Vec::new();
        let mut cur: Option<(usize, usize, f64, usize)> = None; // start, end, conf_sum, count
        let push = |cur: &mut Option<(usize, usize, f64, usize)>, spans: &mut Vec<DetectedZone>| {
            if let Some((s, e, sum, cnt)) = cur.take() {
                spans.push(DetectedZone { start: s, end: e, confidence: if cnt > 0 { sum / cnt as f64 } else { 0.0 } });
            }
        };

        for i in 0..n {
            if self.skip_ids.contains(&ids_all[i]) {
                continue;
            }
            let row = &data[i * classes..i * classes + classes];
            let (label, conf) = argmax_softmax(row);
            let (b0, b1) = offsets[i];
            let s = b2u[b0.min(b2u.len() - 1)];
            let e = b2u[b1.min(b2u.len() - 1)];
            match label {
                1 => {
                    push(&mut cur, &mut spans); // B-MATH ferme le span courant
                    cur = Some((s, e, conf, 1));
                }
                2 => {
                    if let Some(c) = cur.as_mut() {
                        c.1 = e;
                        c.2 += conf;
                        c.3 += 1;
                    }
                }
                _ => push(&mut cur, &mut spans), // O
            }
        }
        push(&mut cur, &mut spans);

        spans.into_iter().filter(|z| z.confidence >= self.threshold).collect()
    }

    /// Pipeline complet (parité Program.Respond du ner-helper) : détecte →
    /// zone la plus proche du caret → fusion fragments → extension au caret →
    /// récupération du mot-clé en tête. Caret + retour en UNITÉS UTF-16.
    pub fn respond(&mut self, text: &str, caret: usize) -> Option<(usize, usize)> {
        let zones = self.detect(text);
        let units: Vec<u16> = text.encode_utf16().collect();
        let pick = zone_refiner::pick_nearest_zone(&zones, caret)?;
        let z = zone_refiner::merge_whitespace_adjacent(&zones, &units, pick, 3);
        let z = zone_refiner::try_extend_forward_whitespace(&units, z, caret);
        let z = zone_refiner::extend_backward_with_keyword(&units, z);
        Some((z.start, z.end))
    }
}

/// argmax + softmax sur les 3 logits d'un token (mêmes règles de départage que
/// MathNerDetector.ArgmaxSoftmax).
fn argmax_softmax(row: &[f32]) -> (usize, f64) {
    let l0 = row[0];
    let l1 = row[1];
    let l2 = row[2];
    let max = l0.max(l1).max(l2);
    let e0 = ((l0 - max) as f64).exp();
    let e1 = ((l1 - max) as f64).exp();
    let e2 = ((l2 - max) as f64).exp();
    let sum = e0 + e1 + e2;
    let p0 = e0 / sum;
    let p1 = e1 / sum;
    let p2 = e2 / sum;
    if p0 >= p1 && p0 >= p2 {
        (0, p0)
    } else if p1 >= p2 {
        (1, p1)
    } else {
        (2, p2)
    }
}

/// Table octet→unité-UTF-16 : pour chaque position d'octet 0..=len, l'offset
/// UTF-16 correspondant. Les positions internes à un char pointent sur son début.
fn byte_to_utf16(text: &str) -> Vec<usize> {
    let mut map = vec![0usize; text.len() + 1];
    let mut u16pos = 0usize;
    let mut last = 0usize;
    for (b, ch) in text.char_indices() {
        for slot in map.iter_mut().take(b + 1).skip(last) {
            *slot = u16pos;
        }
        u16pos += ch.len_utf16();
        last = b + ch.len_utf8();
    }
    for slot in map.iter_mut().skip(last) {
        *slot = u16pos;
    }
    map
}
