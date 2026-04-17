// Scanner backward — extrait l'expression math depuis la fin du texte
// Niveau 1 : délimiteurs explicites (`, $, double espace)
// Niveau 2 : heuristique (mots math connus, frontière OMath)

import { normalizeOMath } from "../decomposition/normalize";
import { fixOMathParens } from "../decomposition/normalize";

const MATH_WORDS = new Set([
  "sin", "cos", "tan", "log", "ln", "exp", "lim", "det", "dim", "max", "min",
  "alpha", "beta", "gamma", "delta", "epsilon", "theta", "lambda", "mu", "pi", "sigma", "omega", "phi",
  "inf", "sqrt", "vide", "vec", "seg", "ang",
]);

export interface Delimiter {
  delim: string;
  replace: string;
}

export function scanMathExpr(
  text: string,
  delimiters: Delimiter[],
): { raw: string; normalized: string } | null {
  const original = text.replace(/[\t\r\n]+$/, "").trimEnd();
  if (original.length < 1) return null;

  // Niveau 1 : délimiteur explicite
  for (const { delim } of delimiters) {
    const idx = original.lastIndexOf(delim);
    if (idx >= 0) {
      const raw = original.slice(idx + delim.length).trim();
      if (raw.length < 1) continue;
      return { raw, normalized: normalizeOMath(raw) };
    }
  }

  // Niveau 2 : scanner heuristique
  const chars = [...original];
  const norm = chars.map(c => normalizeOMath(c));

  let i = chars.length - 1;
  let parenDepth = 0;
  let bracketDepth = 0;
  let seenOMath = false;

  while (i >= 0) {
    const c = norm[i];

    if (c === ")" || c === "]") {
      if (c === ")") parenDepth++; else bracketDepth++;
      i--; continue;
    }
    if (c === "(" || c === "[") {
      if (c === "(") {
        if (parenDepth > 0) { parenDepth--; i--; continue; }
      } else {
        if (bracketDepth > 0) { bracketDepth--; i--; continue; }
      }
      break;
    }

    if (parenDepth > 0 || bracketDepth > 0) { i--; continue; }

    if (/[\d._]/.test(c)) { i--; continue; }

    if (/[a-zA-Z]/.test(c)) {
      const origCp = chars[i].codePointAt(0)!;
      if (origCp >= 0x1D400 && origCp <= 0x1D7FF) seenOMath = true;

      let ws = i;
      while (ws > 0 && /[a-zA-Z]/.test(norm[ws - 1])) ws--;
      const word = norm.slice(ws, i + 1).join("");

      if (seenOMath && word.length > 0) {
        let wordIsRegularText = true;
        for (let k = ws; k <= i; k++) {
          const cp = chars[k].codePointAt(0)!;
          if (cp >= 0x1D400 && cp <= 0x1D7FF) { wordIsRegularText = false; break; }
        }
        if (wordIsRegularText) break;
      }

      if (word.length > 1 && !MATH_WORDS.has(word.toLowerCase())) break;

      i = ws - 1;
      continue;
    }

    if ("+-*/^=".includes(c)) { i--; continue; }
    if (c === ",") { i--; continue; }

    if (c === " ") {
      if (i === 0) break;
      if (".;:!?".includes(norm[i - 1])) break;
      i--; continue;
    }

    break;
  }

  const start = i + 1;
  const rawExpr = chars.slice(start).join("").trim();
  const normExpr = norm.slice(start).join("").trim();
  if (normExpr.length < 1) return null;

  const fixed = fixOMathParens(rawExpr, normExpr);
  return { raw: rawExpr, normalized: fixed };
}
