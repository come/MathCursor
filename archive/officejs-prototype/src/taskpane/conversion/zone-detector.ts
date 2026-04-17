// Zone Detector — trouve la frontière prose/math dans une séquence de tokens
// Remonte depuis la fin (position du curseur) et cherche la transition score < 0.5 → ≥ 0.5

import type { Token, MathZone } from "./types";

const MATH_THRESHOLD = 0.5;

export function detectMathZone(tokens: Token[]): MathZone | null {
  if (tokens.length === 0) return null;

  // Ignorer les whitespaces en fin
  let end = tokens.length - 1;
  while (end >= 0 && tokens[end].categories.has("whitespace")) end--;
  if (end < 0) return null;

  // Remonter depuis la fin : chercher la dernière transition prose → math
  // La zone math commence au premier token avec score ≥ threshold
  // après un token avec score < threshold (ou début de texte)
  let start = end;

  // Phase 1 : remonter tant que c'est du math (score ≥ threshold ou whitespace entre math)
  while (start > 0) {
    const prev = start - 1;
    const prevToken = tokens[prev];

    // Virgule = boundary SAUF si elle est dans des parenthèses
    if (prevToken.categories.has("comma")) {
      // Compter les parens ouvrantes/fermantes de prev vers end
      // Si on croise plus de ) que de ( → on est dans des parens
      let depth = 0;
      for (let j = prev + 1; j <= end; j++) {
        if (tokens[j].text === ")" || tokens[j].text === "]") depth++;
        if (tokens[j].text === "(" || tokens[j].text === "[") depth--;
      }
      if (depth <= 0) break; // virgule hors parens → boundary
      start = prev;
      continue;
    }

    // Whitespace : on regarde ce qu'il y a encore avant
    if (prevToken.categories.has("whitespace")) {
      let lookback = prev - 1;
      while (lookback >= 0 && tokens[lookback].categories.has("whitespace")) lookback--;

      if (lookback < 0) break;

      // Virgule avant le whitespace → boundary (sauf dans des parens)
      if (lookback >= 0 && tokens[lookback].categories.has("comma")) {
        let depth = 0;
        for (let j = lookback + 1; j <= end; j++) {
          if (tokens[j].text === ")" || tokens[j].text === "]") depth++;
          if (tokens[j].text === "(" || tokens[j].text === "[") depth--;
        }
        if (depth <= 0) break;
      }

      if (tokens[lookback].mathiness >= MATH_THRESHOLD) {
        start = lookback;
        continue;
      }
      break;
    }

    // Token avec score < threshold → frontière
    if (prevToken.mathiness < MATH_THRESHOLD) break;

    start = prev;
  }

  // Vérifier qu'on a trouvé du math
  const zoneTokens = tokens.slice(start, end + 1);
  const mathTokens = zoneTokens.filter(t =>
    !t.categories.has("whitespace") && t.mathiness >= MATH_THRESHOLD
  );

  if (mathTokens.length === 0) return null;

  // Calculer la confiance globale (moyenne des scores non-whitespace)
  const nonWs = zoneTokens.filter(t => !t.categories.has("whitespace"));
  const avgScore = nonWs.reduce((sum, t) => sum + t.mathiness, 0) / nonWs.length;

  // Doit contenir au moins un "feature" math (opérateur, paren, fonction, symbole)
  // Un nombre seul ou une variable seule ne suffit pas
  const hasMathFeature = zoneTokens.some(t =>
    t.categories.has("operator") ||
    t.categories.has("mathSymbol") ||
    t.categories.has("greekLetter") ||
    (t.categories.has("paren") && t.mathiness >= 0.7)
  );

  if (!hasMathFeature) return null;

  return {
    startToken: start,
    endToken: end,
    confidence: avgScore,
    tokens: zoneTokens,
    raw: zoneTokens.map(t => t.text).join(""),
    normalized: zoneTokens.map(t => t.normalized).join(""),
  };
}
