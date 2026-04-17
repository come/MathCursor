// Phase 2 : Tokens → AST
// Précédence: ^ > */ > +-=,

import type { N, Tk } from "./types";

export function parse(tks: Tk[]): N {
  const p = { i: 0 };
  return pAdd(tks, p);
}

// additive: [=|+|-] term ((+|-|=|,) term)*
function pAdd(tks: Tk[], p: { i: number }): N {
  let left: N;
  if (p.i < tks.length && tks[p.i].t === "op" && "+-=".includes(tks[p.i].v)) {
    const op = tks[p.i++].v;
    left = { k: "unary", op, child: pMul(tks, p) };
  } else {
    left = pMul(tks, p);
  }
  while (p.i < tks.length && tks[p.i].t === "op" && "+-=,".includes(tks[p.i].v)) {
    const op = tks[p.i++].v;
    left = { k: "op", op, left, right: pMul(tks, p) };
  }
  return left;
}

// multiplicative: power ((* | / | juxtaposition) power)*
function pMul(tks: Tk[], p: { i: number }): N {
  let left = pPow(tks, p);
  while (p.i < tks.length) {
    const nx = tks[p.i];
    if (nx.t === "op" && nx.v === "/") {
      p.i++;
      left = { k: "frac", num: left, den: pPow(tks, p) };
      continue;
    }
    if (nx.t === "op" && nx.v === "*") {
      p.i++;
      left = { k: "op", op: "\u00D7", left, right: pPow(tks, p) };
      continue;
    }
    if (nx.t === "n" || nx.t === "v" || nx.t === "(" || nx.t === "[") {
      const right = pPow(tks, p);
      if (left.k === "juxt") { left.parts.push(right); }
      else { left = { k: "juxt", parts: [left, right] }; }
      continue;
    }
    break;
  }
  return left;
}

// power: unary ^ power (droite-associatif)
function pPow(tks: Tk[], p: { i: number }): N {
  if (p.i < tks.length && tks[p.i].t === "op" && "+-".includes(tks[p.i].v)) {
    const op = tks[p.i++].v;
    return { k: "unary", op, child: pPow(tks, p) };
  }
  const base = pAtom(tks, p);
  if (p.i < tks.length && tks[p.i].t === "op" && tks[p.i].v === "^") {
    p.i++;
    return { k: "sup", base, exp: pPow(tks, p) };
  }
  return base;
}

// atom: nombre | variable | (expr) | [expr]
function pAtom(tks: Tk[], p: { i: number }): N {
  if (p.i >= tks.length) return { k: "empty" };
  const tk = tks[p.i];
  if (tk.t === "(" || tk.t === "[") {
    const d = tk.t as "(" | "[";
    const close = d === "(" ? ")" : "]";
    p.i++;
    const inner = pAdd(tks, p);
    if (p.i < tks.length && tks[p.i].v === close) p.i++;
    return { k: "paren", d, inner };
  }
  if (tk.t === "n") { p.i++; return { k: "num", v: tk.v }; }
  if (tk.t === "v") { p.i++; return { k: "var", v: tk.v }; }
  p.i++;
  return { k: "var", v: tk.v };
}

// Debug: AST → string
export function astToString(n: N, depth = 0): string {
  const pad = "  ".repeat(depth);
  switch (n.k) {
    case "empty": return `${pad}(empty)`;
    case "num": return `${pad}num(${n.v})`;
    case "var": return `${pad}var(${n.v})`;
    case "op": return `${pad}op(${n.op})\n${astToString(n.left, depth + 1)}\n${astToString(n.right, depth + 1)}`;
    case "unary": return `${pad}unary(${n.op})\n${astToString(n.child, depth + 1)}`;
    case "frac": return `${pad}frac\n${pad}  num:\n${astToString(n.num, depth + 2)}\n${pad}  den:\n${astToString(n.den, depth + 2)}`;
    case "sup": return `${pad}sup\n${astToString(n.base, depth + 1)}\n${astToString(n.exp, depth + 1)}`;
    case "paren": return `${pad}paren(${n.d})\n${astToString(n.inner, depth + 1)}`;
    case "juxt": return `${pad}juxt\n${n.parts.map(p => astToString(p, depth + 1)).join("\n")}`;
  }
}
