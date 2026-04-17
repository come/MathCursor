// Phase 3 : AST → OMath XML

import type { N } from "./types";
import { mr, CTRL } from "../omath/helpers";

export function render(n: N): string {
  switch (n.k) {
    case "empty": return "";
    case "num": return mr(n.v);
    case "var": return mr(n.v);
    case "op":
      return render(n.left) + mr(n.op) + render(n.right);
    case "unary":
      return mr(n.op) + render(n.child);
    case "frac":
      return `<m:f><m:fPr>${CTRL}</m:fPr><m:num>${renderFracChild(n.num)}</m:num><m:den>${renderFracChild(n.den)}</m:den></m:f>`;
    case "sup":
      return `<m:sSup><m:sSupPr>${CTRL}</m:sSupPr><m:e>${render(n.base)}</m:e><m:sup>${render(n.exp)}</m:sup></m:sSup>`;
    case "paren": {
      const open = n.d === "(" ? "(" : "[";
      const close = n.d === "(" ? ")" : "]";
      return mr(open) + render(n.inner) + mr(close);
    }
    case "juxt":
      return n.parts.map(render).join("");
  }
}

function renderFracChild(n: N): string {
  if (n.k === "paren") return render(n.inner);
  return render(n);
}
