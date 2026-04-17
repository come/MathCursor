// Trouve l'OMath contenant le curseur, via parsing de l'OOXML du paragraphe
// Et reconstruit le paragraphe en remplaçant un OMath ciblé par du texte source

import { omathXmlToText } from "../omath/xmlToText";

const W_NS = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
const M_NS = "http://schemas.openxmlformats.org/officeDocument/2006/math";

export interface OMathHit {
  index: number;       // index 0-based de l'OMath dans le paragraphe
  startOffset: number; // offset dans para.text (inclusif)
  endOffset: number;   // offset dans para.text (exclusif)
  sourceText: string;  // texte source reconstitué depuis l'OOXML
  paraText: string;    // reconstitution locale de para.text (toutes sections concaténées)
}

// Parse du paragraphe OOXML, retourne le DOM et l'élément <w:p>
function parseParagraph(paraOoxml: string): { doc: Document; para: Element } | null {
  const doc = new DOMParser().parseFromString(paraOoxml, "text/xml");
  const paras = doc.getElementsByTagNameNS(W_NS, "p");
  if (paras.length === 0) return null;
  return { doc, para: paras[0] };
}

// Longueur texte (somme des m:t descendants) d'un élément m:oMath
function oMathTextLength(el: Element): number {
  const ts = el.getElementsByTagNameNS(M_NS, "t");
  let total = 0;
  for (let i = 0; i < ts.length; i++) {
    total += ts[i].textContent?.length ?? 0;
  }
  return total;
}

// Longueur texte (somme des w:t) d'un run w:r
function runTextLength(el: Element): number {
  const ts = el.getElementsByTagNameNS(W_NS, "t");
  let total = 0;
  for (let i = 0; i < ts.length; i++) {
    total += ts[i].textContent?.length ?? 0;
  }
  return total;
}

// Liste ordonnée des OMath d'un paragraphe avec leurs offsets dans para.text
// et l'élément XML correspondant pour sérialisation
interface OMathSpan {
  index: number;
  start: number;
  end: number;
  el: Element;
}

function walkParagraph(para: Element): { spans: OMathSpan[]; paraText: string } {
  const spans: OMathSpan[] = [];
  let paraText = "";
  let index = 0;

  for (const child of Array.from(para.children)) {
    if (child.namespaceURI === W_NS && child.localName === "r") {
      const ts = child.getElementsByTagNameNS(W_NS, "t");
      for (let i = 0; i < ts.length; i++) paraText += ts[i].textContent ?? "";
    } else if (child.namespaceURI === M_NS && child.localName === "oMath") {
      const start = paraText.length;
      const ts = child.getElementsByTagNameNS(M_NS, "t");
      for (let i = 0; i < ts.length; i++) paraText += ts[i].textContent ?? "";
      spans.push({ index, start, end: paraText.length, el: child });
      index++;
    } else if (child.namespaceURI === M_NS && child.localName === "oMathPara") {
      const oMaths = child.getElementsByTagNameNS(M_NS, "oMath");
      for (let i = 0; i < oMaths.length; i++) {
        const om = oMaths[i];
        const start = paraText.length;
        const ts = om.getElementsByTagNameNS(M_NS, "t");
        for (let j = 0; j < ts.length; j++) paraText += ts[j].textContent ?? "";
        spans.push({ index, start, end: paraText.length, el: om });
        index++;
      }
    }
    // Les autres enfants (bookmarkStart, pPr, ...) ne contribuent pas au texte
  }

  return { spans, paraText };
}

// Retourne l'OMath contenant l'offset curseur, ou null si pas de match
export function findOMathAtOffset(paraOoxml: string, cursorOffset: number): OMathHit | null {
  const parsed = parseParagraph(paraOoxml);
  if (!parsed) return null;
  const { spans, paraText } = walkParagraph(parsed.para);

  // On accepte cursorOffset sur les bornes [start, end]
  for (const s of spans) {
    if (cursorOffset >= s.start && cursorOffset <= s.end) {
      const xml = new XMLSerializer().serializeToString(s.el);
      const sourceText = omathXmlToText(xml);
      return {
        index: s.index,
        startOffset: s.start,
        endOffset: s.end,
        sourceText,
        paraText,
      };
    }
  }
  return null;
}

// Reconstruit le paragraphe OOXML en remplaçant le Nième OMath par un texte source
// Retourne le nouveau OOXML à passer à insertOoxml(replace) sur le range du paragraphe
export function replaceOMathWithText(
  paraOoxml: string,
  index: number,
  sourceText: string,
): string | null {
  const parsed = parseParagraph(paraOoxml);
  if (!parsed) return null;
  const { doc, para } = parsed;

  // Retrouver le Nième oMath en document order
  let currentIdx = 0;
  let target: Element | null = null;
  let parentOMathPara: Element | null = null;

  for (const child of Array.from(para.children)) {
    if (child.namespaceURI === M_NS && child.localName === "oMath") {
      if (currentIdx === index) { target = child; break; }
      currentIdx++;
    } else if (child.namespaceURI === M_NS && child.localName === "oMathPara") {
      const oMaths = Array.from(child.getElementsByTagNameNS(M_NS, "oMath"));
      for (const om of oMaths) {
        if (currentIdx === index) {
          target = om;
          parentOMathPara = child;
          break;
        }
        currentIdx++;
      }
      if (target) break;
    }
  }

  if (!target) return null;

  // Nouveau run w:r > w:t contenant sourceText (xml:space="preserve" pour garder les espaces)
  const newRun = doc.createElementNS(W_NS, "w:r");
  const newT = doc.createElementNS(W_NS, "w:t");
  newT.setAttributeNS("http://www.w3.org/XML/1998/namespace", "xml:space", "preserve");
  newT.textContent = sourceText;
  newRun.appendChild(newT);

  // Remplacer l'OMath (ou l'oMathPara entier s'il ne contenait que cet OMath)
  if (parentOMathPara && parentOMathPara.getElementsByTagNameNS(M_NS, "oMath").length === 1) {
    parentOMathPara.parentNode?.replaceChild(newRun, parentOMathPara);
  } else {
    target.parentNode?.replaceChild(newRun, target);
  }

  return new XMLSerializer().serializeToString(doc);
}

// Position approximative du curseur dans le sourceText après décomposition
// cursorOffsetInOMath = offset depuis le début de l'OMath dans para.text
// On clampe à la longueur du sourceText
export function estimateCursorInSource(
  cursorOffsetInOMath: number,
  omathTextLength: number,
  sourceTextLength: number,
): number {
  if (omathTextLength <= 0) return 0;
  const ratio = cursorOffsetInOMath / omathTextLength;
  return Math.max(0, Math.min(sourceTextLength, Math.round(ratio * sourceTextLength)));
}
