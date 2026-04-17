// Mathiness Scorer — attribue un score 0.0→1.0 à chaque token
// Plus le score est haut, plus le token est probablement du math

import type { Token } from "./types";

// ============================================================
// DONNÉES
// ============================================================

// Fonctions math connues (universel, pas lié à une langue)
const MATH_FUNCTIONS = new Set([
  "sin", "cos", "tan", "cot", "sec", "csc",
  "log", "ln", "exp",
  "lim", "sum", "prod", "int",
  "max", "min", "sup", "inf",
  "det", "dim", "ker", "arg", "mod", "gcd", "lcm",
  "sqrt", "abs",
]);

// Mots-clés math (raccourcis qu'on reconnaît)
const MATH_KEYWORDS = new Set([
  "alpha", "beta", "gamma", "delta", "epsilon", "theta", "lambda",
  "mu", "pi", "sigma", "omega", "phi", "psi", "rho", "tau",
  "vec", "seg", "ang", "vide",
]);

// Stopwords multilingues ultra-courts (1-3 lettres) qui collisionnent avec des variables
// Mots de prose courants qui NE SONT PAS des variables math
const STOPWORDS = new Set([
  // FR
  "on", "un", "une", "le", "la", "les", "de", "du", "des", "et", "ou", "en",
  "il", "ce", "se", "ne", "pas", "que", "qui", "est", "son", "sa", "ses",
  "au", "aux", "par", "sur", "dans", "pour", "avec", "soit", "car", "donc",
  "mais", "the",
  // EN
  "the", "is", "it", "in", "on", "at", "to", "an", "of", "or", "and",
  "we", "he", "she", "be", "do", "if", "so", "no", "not", "has", "had",
  "was", "are", "its", "but", "for", "let", "set",
  // DE
  "es", "im", "zu", "ob", "da", "so", "um", "am", "ab",
  "ist", "ein", "und", "der", "die", "das", "dem", "den", "des",
  "sei", "mit", "aus", "auf", "bei", "vor",
  // ES
  "el", "la", "lo", "en", "es", "de", "al", "del", "por", "con",
  "sea", "que", "los", "las", "una",
  // IT
  "il", "lo", "la", "le", "di", "da", "in", "su", "per", "con",
  // PT
  "um", "em", "de", "da", "do", "no", "na", "os", "as", "ao",
]);

// ============================================================
// SCORER
// ============================================================

export function scoreMathiness(token: Token, prevToken?: Token, nextToken?: Token): number {
  const cats = token.categories;
  const norm = token.normalized.toLowerCase();

  // Whitespace → neutre
  if (cats.has("whitespace")) return 0.5;

  // Symbole math Unicode (∫ ∑ ∞ ≥ ∈ ∀) → certain
  if (cats.has("mathSymbol")) return 1.0;

  // Lettre grecque (α β γ) → très probable
  if (cats.has("greekLetter")) return 0.95;

  // Opérateur math (+ - * / ^ = < >) → très probable
  if (cats.has("operator")) return 0.9;

  // Parenthèses → dépend du contexte
  if (cats.has("paren")) {
    // ( après une lettre = probable function call f(x)
    if (token.text === "(" && prevToken?.categories.has("letter")) return 0.9;
    return 0.7;
  }

  // Virgule → modérément math (peut être séparateur de liste)
  if (cats.has("comma")) return 0.5;

  // Nombre → probable math
  if (cats.has("digit")) return 0.8;

  // Dot → contexte
  if (cats.has("dot")) {
    // Après un nombre = nombre décimal
    if (prevToken?.categories.has("digit")) return 0.8;
    // Sinon = fin de phrase = pas math
    return 0.1;
  }

  // Lettres → le cas complexe
  if (cats.has("letter")) {
    // Fonction math connue
    if (MATH_FUNCTIONS.has(norm)) return 0.95;

    // Mot-clé math (alpha, vec, ...)
    if (MATH_KEYWORDS.has(norm)) return 0.95;

    // Stopword → pas math (forcé)
    if (STOPWORDS.has(norm)) return 0.0;

    // Variable 1 lettre → probable math
    if (norm.length === 1) {
      // Encore plus probable si entouré de math
      const prevMath = prevToken && prevToken.mathiness >= 0.5;
      const nextMath = nextToken && nextToken.mathiness >= 0.5;
      if (prevMath || nextMath) return 0.8;
      return 0.4;
    }

    // Mot 2 lettres → ambigü
    if (norm.length === 2) return 0.2;

    // Mot 3+ lettres avec ratio voyelles typique de prose → pas math
    if (norm.length >= 3) {
      const vowels = norm.split("").filter(c => "aeiouy".includes(c)).length;
      const ratio = vowels / norm.length;
      if (ratio >= 0.3 && ratio <= 0.6) return 0.1; // mot de prose typique
      return 0.15;
    }

    return 0.3;
  }

  return 0.3;
}

// ============================================================
// SCORE ALL TOKENS (2 passes pour le contexte)
// ============================================================

export function scoreAllTokens(tokens: Token[]): void {
  // Passe 1 : score sans contexte
  for (let i = 0; i < tokens.length; i++) {
    tokens[i].mathiness = scoreMathiness(tokens[i]);
  }

  // Passe 2 : re-score avec contexte (voisins)
  for (let i = 0; i < tokens.length; i++) {
    const prev = i > 0 ? tokens[i - 1] : undefined;
    const next = i < tokens.length - 1 ? tokens[i + 1] : undefined;
    tokens[i].mathiness = scoreMathiness(tokens[i], prev, next);
  }
}
