// Phase 1 : Texte → Tokens enrichis
// Chaque token a : texte, positions, catégories Unicode, score mathiness (rempli après)

import type { Token, UnicodeCategory } from "./types";

// ============================================================
// CATÉGORISATION UNICODE
// ============================================================

const GREEK_LOWER = new Set("αβγδεζηθικλμνξπρσςτυφχψω");
const GREEK_UPPER = new Set("ΑΒΓΔΕΖΗΘΙΚΛΜΝΞΠΡΣΤΥΦΧΨΩ");
const MATH_SYMBOLS = new Set("∫∑∏∂∇∞≤≥≠≈∈∉⊂⊃⊆⊇∪∩±∓×÷∧∨¬→⟹⟺∀∃∄∅ℕℤℚℝℂ");
const OPERATORS = new Set("+-*/^=<>!");
const PARENS = new Set("()[]{}");

function classifyChar(c: string): Set<UnicodeCategory> {
  const cats = new Set<UnicodeCategory>();
  const cp = c.codePointAt(0) ?? 0;

  // Math italic (OMath dans para.text)
  if ((cp >= 0x1D400 && cp <= 0x1D7FF) || cp === 0x210E) {
    cats.add("letter");
    return cats;
  }

  if (GREEK_LOWER.has(c) || GREEK_UPPER.has(c)) { cats.add("greekLetter"); return cats; }
  if (MATH_SYMBOLS.has(c)) { cats.add("mathSymbol"); return cats; }
  if (OPERATORS.has(c)) { cats.add("operator"); return cats; }
  if (PARENS.has(c)) { cats.add("paren"); return cats; }
  if (c === ",") { cats.add("comma"); return cats; }
  if (c === ".") { cats.add("dot"); return cats; }
  if (/\d/.test(c)) { cats.add("digit"); return cats; }
  if (/[a-zA-Z]/.test(c)) { cats.add("letter"); return cats; }
  // Lettres accentuées et autres alphabets
  if (/\p{L}/u.test(c)) { cats.add("letter"); return cats; }
  if (/\s/.test(c)) { cats.add("whitespace"); return cats; }

  cats.add("unknown");
  return cats;
}

function mergeCategories(a: Set<UnicodeCategory>, b: Set<UnicodeCategory>): Set<UnicodeCategory> {
  const merged = new Set(a);
  for (const cat of b) merged.add(cat);
  return merged;
}

// ============================================================
// NORMALISATION CARACTÈRE (OMath → ASCII)
// ============================================================

export function normalizeChar(c: string): string {
  const cp = c.codePointAt(0) ?? 0;
  // Math Italic uppercase A-Z
  if (cp >= 0x1D434 && cp <= 0x1D44D) return String.fromCharCode(65 + cp - 0x1D434);
  // Math Italic lowercase a-g
  if (cp >= 0x1D44E && cp <= 0x1D454) return String.fromCharCode(97 + cp - 0x1D44E);
  // Math Italic h (Planck)
  if (cp === 0x210E) return "h";
  // Math Italic lowercase i-z
  if (cp >= 0x1D456 && cp <= 0x1D467) return String.fromCharCode(105 + cp - 0x1D456);
  // Math Bold uppercase
  if (cp >= 0x1D400 && cp <= 0x1D419) return String.fromCharCode(65 + cp - 0x1D400);
  // Math Bold lowercase
  if (cp >= 0x1D41A && cp <= 0x1D433) return String.fromCharCode(97 + cp - 0x1D41A);
  // Math Bold Italic uppercase
  if (cp >= 0x1D468 && cp <= 0x1D481) return String.fromCharCode(65 + cp - 0x1D468);
  // Math Bold Italic lowercase
  if (cp >= 0x1D482 && cp <= 0x1D49B) return String.fromCharCode(97 + cp - 0x1D482);
  // × → *
  if (cp === 0x00D7) return "*";
  // − (minus sign) → -
  if (cp === 0x2212) return "-";
  // en-dash / em-dash → -
  if (cp === 0x2013 || cp === 0x2014) return "-";
  return c;
}

// ============================================================
// TOKENIZER
// ============================================================

export function tokenize(text: string): Token[] {
  const codepoints = [...text]; // split en codepoints (gère les surrogate pairs)
  const tokens: Token[] = [];
  let i = 0;
  let pos = 0; // position en code units (pour le mapping Word)

  while (i < codepoints.length) {
    const c = codepoints[i];
    const charCats = classifyChar(c);

    // Whitespace → token séparé
    if (charCats.has("whitespace")) {
      const start = pos;
      let text = "";
      while (i < codepoints.length && classifyChar(codepoints[i]).has("whitespace")) {
        text += codepoints[i];
        pos += codepoints[i].length;
        i++;
      }
      tokens.push({
        text, normalized: " ", start, end: pos,
        categories: new Set(["whitespace"]), mathiness: 0,
      });
      continue;
    }

    // Nombre (digits + dot)
    if (charCats.has("digit")) {
      const start = pos;
      let text = "";
      let cats = new Set<UnicodeCategory>(["digit"]);
      while (i < codepoints.length) {
        const cc = classifyChar(codepoints[i]);
        if (!cc.has("digit") && !cc.has("dot")) break;
        text += codepoints[i];
        cats = mergeCategories(cats, cc);
        pos += codepoints[i].length;
        i++;
      }
      tokens.push({
        text, normalized: text, start, end: pos,
        categories: cats, mathiness: 0,
      });
      continue;
    }

    // Lettres (mot entier, inclut math italic et grec)
    if (charCats.has("letter") || charCats.has("greekLetter")) {
      const start = pos;
      let raw = "";
      let norm = "";
      let cats = new Set<UnicodeCategory>();
      while (i < codepoints.length) {
        const cc = classifyChar(codepoints[i]);
        if (!cc.has("letter") && !cc.has("greekLetter")) break;
        raw += codepoints[i];
        norm += normalizeChar(codepoints[i]);
        cats = mergeCategories(cats, cc);
        pos += codepoints[i].length;
        i++;
      }
      tokens.push({
        text: raw, normalized: norm, start, end: pos,
        categories: cats, mathiness: 0,
      });
      continue;
    }

    // Symbole math Unicode (∫ ∑ ∞ ≥ ∈ ...)
    if (charCats.has("mathSymbol")) {
      tokens.push({
        text: c, normalized: normalizeChar(c), start: pos, end: pos + c.length,
        categories: charCats, mathiness: 0,
      });
      pos += c.length;
      i++;
      continue;
    }

    // Opérateur
    if (charCats.has("operator")) {
      // Grouper les opérateurs multi-chars : <=, >=, !=, =>, <=>
      const start = pos;
      let op = c;
      pos += c.length; i++;
      if (i < codepoints.length) {
        const next = codepoints[i];
        const two = op + next;
        if (["<=", ">=", "!=", "=>"].includes(two)) {
          op = two; pos += next.length; i++;
          if (op === "<=" && i < codepoints.length && codepoints[i] === ">") {
            op = "<=>"; pos += 1; i++;
          }
        } else if (op === "-" && next === ">") {
          op = "->"; pos += next.length; i++;
        }
        // "!" seul (factoriel) reste "!" — ne pas grouper avec autre chose sauf "!="
      }
      tokens.push({
        text: op, normalized: op, start, end: pos,
        categories: new Set(["operator"]), mathiness: 0,
      });
      continue;
    }

    // Parenthèse / crochet
    if (charCats.has("paren")) {
      tokens.push({
        text: c, normalized: c, start: pos, end: pos + c.length,
        categories: charCats, mathiness: 0,
      });
      pos += c.length;
      i++;
      continue;
    }

    // Comma
    if (charCats.has("comma")) {
      tokens.push({
        text: c, normalized: c, start: pos, end: pos + c.length,
        categories: charCats, mathiness: 0,
      });
      pos += c.length;
      i++;
      continue;
    }

    // Dot isolé (pas dans un nombre)
    if (charCats.has("dot")) {
      tokens.push({
        text: c, normalized: c, start: pos, end: pos + c.length,
        categories: charCats, mathiness: 0,
      });
      pos += c.length;
      i++;
      continue;
    }

    // Tout autre caractère
    tokens.push({
      text: c, normalized: normalizeChar(c), start: pos, end: pos + c.length,
      categories: charCats, mathiness: 0,
    });
    pos += c.length;
    i++;
  }

  return tokens;
}
