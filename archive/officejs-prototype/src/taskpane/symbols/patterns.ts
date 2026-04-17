// Symboles regex — pi, alpha, vec, >=, Vx(R, etc.
// Chaque symbole est un { re, resolve } testé en fin de texte

import type { DocChoice } from "../conversion/types";
import { mr, omathPkg, oVec, oBar, SETS } from "../omath/helpers";

interface SymPattern {
  re: RegExp;
  resolve: (m: RegExpMatchArray) => DocChoice[];
}

function sym(base: string, resolve: (m: RegExpMatchArray) => DocChoice[]): SymPattern {
  return { re: new RegExp(base + "$", "i"), resolve };
}

function s1(label: string, display: string, repl?: string) {
  return () => [{ label, display, replacement: repl ?? display } as DocChoice];
}

function s1fn(label: string, fn: (m: RegExpMatchArray) => string) {
  return (m: RegExpMatchArray) => { const r = fn(m); return [{ label, display: r, replacement: r }]; };
}

export const SYMBOLS: SymPattern[] = [
  sym(`<=>`, () => [{ label: "équivalent", display: "\u27FA", replacement: "\u27FA", ooxml: omathPkg(mr("\u27FA")) }]),
  sym(`(?<![<>])=>`, () => [{ label: "implique", display: "\u27F9", replacement: "\u27F9", ooxml: omathPkg(mr("\u27F9")) }]),

  sym(`(?:V|pt|qq)\\s*([a-zA-Z](?:\\s*,\\s*[a-zA-Z])*)\\s*(?:\\(|c|dans |in |app |de )\\s*([RNZQC])`,
    (m) => {
      const vars = m[1].replace(/\s/g, "");
      const s = SETS[m[2].toUpperCase()] ?? m[2];
      const display = `\u2200${vars} \u2208 ${s}`;
      const xml = mr("\u2200") + [...vars].map(c => c === "," ? mr(",") : mr(c)).join("") + mr("\u2208") + mr(s);
      return [{ label: "pour tout", display, replacement: display, ooxml: omathPkg(xml) }];
    }),
  sym(`(?:E|ie)\\s*([a-zA-Z](?:\\s*,\\s*[a-zA-Z])*)\\s*(?:\\(|c|dans |in |app |de )\\s*([RNZQC])`,
    (m) => {
      const vars = m[1].replace(/\s/g, "");
      const s = SETS[m[2].toUpperCase()] ?? m[2];
      const display = `\u2203${vars} \u2208 ${s}`;
      const xml = mr("\u2203") + [...vars].map(c => c === "," ? mr(",") : mr(c)).join("") + mr("\u2208") + mr(s);
      return [{ label: "il existe", display, replacement: display, ooxml: omathPkg(xml) }];
    }),
  sym(`(?:E|ie)!\\s*([a-zA-Z](?:\\s*,\\s*[a-zA-Z])*)`, (m) => {
    const vars = m[1].replace(/\s/g, "");
    const display = `\u2203!${vars}`;
    const xml = mr("\u2203") + mr("!") + [...vars].map(c => c === "," ? mr(",") : mr(c)).join("");
    return [{ label: "il existe un unique", display, replacement: display, ooxml: omathPkg(xml) }];
  }),

  sym(`!(?:\\(|c)\\s*([RNZQC])`, (m) => {
    const s = SETS[m[1].toUpperCase()] ?? m[1];
    return [{ label: "n'appartient pas", display: `\u2209${s}`, replacement: `\u2209${s}`, ooxml: omathPkg(mr("\u2209") + mr(s)) }];
  }),
  sym(`\\b(?:sub|inc)\\s+([RNZQC])`, (m) => {
    const s = SETS[m[1].toUpperCase()] ?? m[1];
    return [{ label: "inclus dans", display: `\u2282${s}`, replacement: `\u2282${s}`, ooxml: omathPkg(mr("\u2282") + mr(s)) }];
  }),
  sym(`(?:\\(|(?<=[^a-zA-Z])c)\\s*([RNZQC])`, (m) => {
    const s = SETS[m[1].toUpperCase()] ?? m[1];
    return [{ label: "appartient à", display: `\u2208${s}`, replacement: `\u2208${s}`, ooxml: omathPkg(mr("\u2208") + mr(s)) }];
  }),

  sym(`([A-Z])u([A-Z])`, (m) => {
    const xml = mr(m[1]) + mr("\u222A") + mr(m[2]);
    return [{ label: "union", display: `${m[1]}\u222A${m[2]}`, replacement: `${m[1]}\u222A${m[2]}`, ooxml: omathPkg(xml) }];
  }),
  sym(`([A-Z])n([A-Z])`, (m) => {
    const xml = mr(m[1]) + mr("\u2229") + mr(m[2]);
    return [{ label: "intersection", display: `${m[1]}\u2229${m[2]}`, replacement: `${m[1]}\u2229${m[2]}`, ooxml: omathPkg(xml) }];
  }),

  sym(`>=`, () => [{ label: "supérieur ou égal", display: "\u2265", replacement: "\u2265", ooxml: omathPkg(mr("\u2265")) }]),
  sym(`(?<!<)<=`, () => [{ label: "inférieur ou égal", display: "\u2264", replacement: "\u2264", ooxml: omathPkg(mr("\u2264")) }]),
  sym(`!=`, () => [{ label: "différent", display: "\u2260", replacement: "\u2260", ooxml: omathPkg(mr("\u2260")) }]),

  sym(`\\blim\\s*->\\s*([a-zA-Z0-9]+)(\\+|-)?`, (m) => {
    const t = m[1] === "inf" ? "+\u221E" : m[1];
    const sg = m[2] === "+" ? "\u207A" : m[2] === "-" ? "\u207B" : "";
    return [{ label: "limite", display: `lim \u2192 ${t}${sg}`, replacement: `lim \u2192 ${t}${sg}` }];
  }),

  sym(`([a-zA-Z])''`, s1fn("dérivée seconde", (m) => `${m[1]}\u2033`)),
  sym(`([a-zA-Z])'`, s1fn("dérivée", (m) => `${m[1]}\u2032`)),

  sym(`\\bvec\\s+([A-Za-z]+)`, (m) => {
    const t = m[1].toUpperCase();
    return [{ label: "vecteur", display: `${t}\u20D7`, replacement: t, ooxml: oVec(t) }];
  }),
  sym(`\\bseg\\s+([A-Za-z]+)`, (m) => {
    const t = m[1].toUpperCase();
    return [{ label: "segment", display: `[${t}]`, replacement: t, ooxml: oBar(t) }];
  }),
  sym(`\\bang\\s+([A-Za-z]+)`, s1fn("angle", (m) => `\u2220${m[1].toUpperCase()}`)),

  sym(`-inf`, s1("-\u221E", "-\u221E")),
  sym(`\\+inf`, s1("+\u221E", "+\u221E")),
  sym(`\\binf`, s1("\u221E", "\u221E")),
  sym(`\\bvide`, s1("ensemble vide", "\u2205")),

  sym(`\\bepsilon`, s1("\u03B5", "\u03B5")),
  sym(`\\blambda`, s1("\u03BB", "\u03BB")),
  sym(`\\balpha`, s1("\u03B1", "\u03B1")),
  sym(`\\bdelta`, s1("\u03B4", "\u03B4")),
  sym(`\\bDelta`, s1("\u0394", "\u0394")),
  sym(`\\btheta`, s1("\u03B8", "\u03B8")),
  sym(`\\bsigma`, s1("\u03C3", "\u03C3")),
  sym(`\\bSigma`, s1("\u03A3", "\u03A3")),
  sym(`\\bomega`, s1("\u03C9", "\u03C9")),
  sym(`\\bOmega`, s1("\u03A9", "\u03A9")),
  sym(`\\bgamma`, s1("\u03B3", "\u03B3")),
  sym(`\\bbeta`, s1("\u03B2", "\u03B2")),
  sym(`\\bphi`, s1("\u03C6", "\u03C6")),
  sym(`\\bmu`, s1("\u03BC", "\u03BC")),
  sym(`\\bpi`, s1("\u03C0", "\u03C0")),
  sym(`~`, s1("négation", "\u00AC")),
];

export function findSymbol(text: string): { raw: string; choices: DocChoice[] } | null {
  const trimmed = text.replace(/[\s\t]+$/, "");
  for (const s of SYMBOLS) {
    const m = trimmed.match(s.re);
    if (m) {
      const choices = s.resolve(m);
      if (choices.length > 0) return { raw: m[0], choices };
    }
  }
  return null;
}
