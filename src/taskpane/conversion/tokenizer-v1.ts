// Tokenizer v1 — ancien format Tk pour le parser récursif descent
// Sera remplacé quand le parser sera adapté au nouveau format Token

import type { Tk } from "./types";

const WORD_SYM: Record<string, string> = {
  alpha: "\u03B1", beta: "\u03B2", gamma: "\u03B3", delta: "\u03B4",
  epsilon: "\u03B5", theta: "\u03B8", lambda: "\u03BB", mu: "\u03BC",
  pi: "\u03C0", sigma: "\u03C3", omega: "\u03C9", phi: "\u03C6",
  inf: "\u221E", infini: "\u221E", sqrt: "\u221A", vide: "\u2205",
};

export function tokenize(s: string): Tk[] {
  const p = s.replace(/([a-zA-Z\d]+)\s+(\d+)(?=\s|$|[+\-*/=)\]])/g, "$1^$2");
  const out: Tk[] = [];
  let i = 0;
  while (i < p.length) {
    if (/\s/.test(p[i])) { i++; continue; }
    if (/\d/.test(p[i])) {
      let n = "";
      while (i < p.length && /[\d.]/.test(p[i])) n += p[i++];
      out.push({ t: "n", v: n });
    } else if (/[a-zA-Z]/.test(p[i])) {
      let w = "";
      while (i < p.length && /[a-zA-Z]/.test(p[i])) w += p[i++];
      out.push({ t: "v", v: WORD_SYM[w.toLowerCase()] ?? w });
    } else if ("()[]".includes(p[i])) {
      out.push({ t: p[i] as Tk["t"], v: p[i] }); i++;
    } else if ("+-*/^=".includes(p[i])) {
      out.push({ t: "op", v: p[i] }); i++;
    } else if (p[i] === ",") {
      out.push({ t: "op", v: "," }); i++;
    } else i++;
  }
  return out;
}
