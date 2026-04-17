// OMath XML → Texte source (reverse du render)
// Lit le OOXML, trouve les <m:oMath>, convertit en texte

const NS = "http://schemas.openxmlformats.org/officeDocument/2006/math";

export function omathXmlToText(ooxml: string): string {
  try {
    const parser = new DOMParser();
    const doc = parser.parseFromString(ooxml, "text/xml");
    const oMaths = doc.getElementsByTagNameNS(NS, "oMath");
    if (oMaths.length === 0) return "";
    return nodeToText(oMaths[0]);
  } catch {
    return "";
  }
}

function nodeToText(node: Element): string {
  const tag = node.localName;

  switch (tag) {
    case "oMath":
    case "oMathPara":
    case "e":
    case "num":
    case "den":
    case "sup":
    case "sub":
      return childrenToText(node);

    case "r": {
      const t = node.getElementsByTagNameNS(NS, "t")[0];
      return t?.textContent ?? "";
    }

    case "f": {
      const num = node.getElementsByTagNameNS(NS, "num")[0];
      const den = node.getElementsByTagNameNS(NS, "den")[0];
      const n = num ? nodeToText(num) : "?";
      const d = den ? nodeToText(den) : "?";
      return `(${n})/(${d})`;
    }

    case "sSup": {
      const e = node.getElementsByTagNameNS(NS, "e")[0];
      const sup = node.getElementsByTagNameNS(NS, "sup")[0];
      const b = e ? nodeToText(e) : "?";
      const s = sup ? nodeToText(sup) : "?";
      return b.length <= 1 ? `${b}^${s}` : `(${b})^${s}`;
    }

    case "sSub": {
      const e = node.getElementsByTagNameNS(NS, "e")[0];
      const sub = node.getElementsByTagNameNS(NS, "sub")[0];
      return `${e ? nodeToText(e) : "?"}_${sub ? nodeToText(sub) : "?"}`;
    }

    case "groupChr": {
      const e = node.getElementsByTagNameNS(NS, "e")[0];
      const chrPr = node.getElementsByTagNameNS(NS, "groupChrPr")[0];
      const chr = chrPr?.getElementsByTagNameNS(NS, "chr")[0];
      const chrVal = chr?.getAttributeNS(NS, "val") ?? "";
      const inner = e ? nodeToText(e) : "?";
      if (chrVal === "\u2192") return `vec ${inner}`;
      if (chrVal === "\u00AF") return `seg ${inner}`;
      return inner;
    }

    case "rad": {
      const e = node.getElementsByTagNameNS(NS, "e")[0];
      return `sqrt(${e ? nodeToText(e) : "?"})`;
    }

    default:
      return childrenToText(node);
  }
}

function childrenToText(node: Element): string {
  let out = "";
  for (const child of node.children) {
    if (child.localName?.endsWith("Pr") || child.localName === "ctrlPr") continue;
    if (child.namespaceURI !== NS) {
      const t = child.getElementsByTagName("w:t")[0];
      if (t?.textContent?.trim()) out += t.textContent;
      continue;
    }
    out += nodeToText(child);
  }
  return out;
}
