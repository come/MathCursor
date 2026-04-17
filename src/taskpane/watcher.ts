// ============================================================
// watcher.ts v2 — Tab = convertir l'expression math avant le curseur
//
// Flow:
//  1. User tape une expression math dans Word
//  2. Tab → on scanne à l'envers depuis le curseur
//  3. On parse avec précédence: ^ > */ > +-=
//  4. On remplace par du OMath propre
//
// Le slow tick ne gère QUE les symboles (pi, alpha, vec...)
// Le fast tick gère Tab → expression math OU symbole
// ============================================================

import { ref, computed } from "vue";

// --- État réactif ---
export const isActive = ref(true);
export const lastAction = ref("");
export const replaceCount = ref(0);
export const debugInfo = ref("Démarrage...");
export const debugSteps = ref<string[]>([]);

// --- Settings utilisateur ---
// Délimiteurs : { delim: ce qu'on cherche, replace: ce qu'on laisse après suppression }
export const mathDelimiters = ref([
  { delim: "`", replace: "" },
  { delim: "$", replace: "" },
  { delim: "  ", replace: " " },  // double espace → simple espace
]);

export interface DocChoice {
  label: string;
  display: string;
  replacement: string;
  ooxml?: string;
}

export const suggestions = ref<DocChoice[]>([]);
export const selectedIdx = ref(0);
export const matchedRaw = ref("");
export const hasSuggestions = computed(() => suggestions.value.length > 0);

export function selectSuggestion(i: number) { selectedIdx.value = i; }

// ============================================================
// OMATH HELPERS
// ============================================================

const SETS: Record<string, string> = {
  R: "\u211D", N: "\u2115", Z: "\u2124", Q: "\u211A", C: "\u2102",
};

const RFonts = `<w:rFonts w:ascii="Cambria Math" w:hAnsi="Cambria Math"/>`;
const CTRL = `<m:ctrlPr><w:rPr>${RFonts}<w:i/></w:rPr></m:ctrlPr>`;

function mr(t: string): string {
  return `<m:r><w:rPr>${RFonts}<w:i/></w:rPr><m:t>${t}</m:t></m:r>`;
}

function omathPkg(inner: string): string {
  return `<pkg:package xmlns:pkg="http://schemas.microsoft.com/office/2006/xmlPackage"><pkg:part pkg:name="/_rels/.rels" pkg:contentType="application/vnd.openxmlformats-package.relationships+xml"><pkg:xmlData><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships></pkg:xmlData></pkg:part><pkg:part pkg:name="/word/document.xml" pkg:contentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"><pkg:xmlData><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"><w:body><w:p><m:oMath>${inner}</m:oMath><w:r><w:t xml:space="preserve"> </w:t></w:r></w:p></w:body></w:document></pkg:xmlData></pkg:part></pkg:package>`;
}

function oVec(text: string): string {
  return omathPkg(
    `<m:groupChr><m:groupChrPr><m:chr m:val="\u2192"/><m:pos m:val="top"/><m:vertJc m:val="bot"/></m:groupChrPr><m:e>${mr(text)}</m:e></m:groupChr>`
  );
}

function oBar(text: string): string {
  return omathPkg(
    `<m:groupChr><m:groupChrPr><m:chr m:val="\u00AF"/><m:pos m:val="top"/><m:vertJc m:val="bot"/></m:groupChrPr><m:e>${mr(text)}</m:e></m:groupChr>`
  );
}

// ============================================================
// SCANNER — remonte depuis la fin du texte pour extraire
// l'expression math la plus longue possible
// ============================================================

// ============================================================
// Normalisation OMath → ASCII
// Word retourne les caractères OMath en math italic (𝑔→g, 𝑥→x)
// et d'autres substitutions (×→*)
// ============================================================
function normalizeOMath(s: string): string {
  let out = "";
  for (const c of s) {
    const cp = c.codePointAt(0)!;
    // Math Italic uppercase A-Z : U+1D434–U+1D44D
    if (cp >= 0x1D434 && cp <= 0x1D44D) { out += String.fromCharCode(65 + cp - 0x1D434); continue; }
    // Math Italic lowercase a-g : U+1D44E–U+1D454
    if (cp >= 0x1D44E && cp <= 0x1D454) { out += String.fromCharCode(97 + cp - 0x1D44E); continue; }
    // Math Italic h : U+210E (Planck constant, exception)
    if (cp === 0x210E) { out += "h"; continue; }
    // Math Italic lowercase i-z : U+1D456–U+1D467
    if (cp >= 0x1D456 && cp <= 0x1D467) { out += String.fromCharCode(105 + cp - 0x1D456); continue; }
    // Math Bold uppercase : U+1D400–U+1D419
    if (cp >= 0x1D400 && cp <= 0x1D419) { out += String.fromCharCode(65 + cp - 0x1D400); continue; }
    // Math Bold lowercase : U+1D41A–U+1D433
    if (cp >= 0x1D41A && cp <= 0x1D433) { out += String.fromCharCode(97 + cp - 0x1D41A); continue; }
    // Math Bold Italic uppercase : U+1D468–U+1D481
    if (cp >= 0x1D468 && cp <= 0x1D481) { out += String.fromCharCode(65 + cp - 0x1D468); continue; }
    // Math Bold Italic lowercase : U+1D482–U+1D49B
    if (cp >= 0x1D482 && cp <= 0x1D49B) { out += String.fromCharCode(97 + cp - 0x1D482); continue; }
    // × → *
    if (cp === 0x00D7) { out += "*"; continue; }
    // − (minus sign U+2212) → -
    if (cp === 0x2212) { out += "-"; continue; }
    // – (en-dash U+2013) → -
    if (cp === 0x2013) { out += "-"; continue; }
    // — (em-dash U+2014) → -
    if (cp === 0x2014) { out += "-"; continue; }
    out += c;
  }
  return out;
}

// Deuxième passe : OMath parens
// Word rend ( → , et ) → . dans para.text pour le contenu OMath
// On détecte : si , ou . est adjacent à une lettre math italic dans l'original
function fixOMathParens(original: string, normalized: string): string {
  const origChars = [...original];
  const normChars = [...normalized];
  // Les longueurs peuvent différer (surrogate pairs) — on travaille sur l'original
  // et on map sur le normalisé via l'index codepoint
  const result = [...normChars];
  for (let i = 0; i < origChars.length && i < result.length; i++) {
    const cp = origChars[i].codePointAt(0)!;
    const isMathItalic = (cp >= 0x1D400 && cp <= 0x1D7FF) || cp === 0x210E;
    if (result[i] === "," && i + 1 < origChars.length) {
      const nextCp = origChars[i + 1].codePointAt(0)!;
      if ((nextCp >= 0x1D400 && nextCp <= 0x1D7FF) || nextCp === 0x210E) {
        result[i] = "(";
      }
    }
    if (result[i] === "." && i > 0) {
      const prevCp = origChars[i - 1].codePointAt(0)!;
      if ((prevCp >= 0x1D400 && prevCp <= 0x1D7FF) || prevCp === 0x210E || /\d/.test(origChars[i - 1])) {
        result[i] = ")";
      }
    }
  }
  return result.join("");
}

// ============================================================
// OMath XML → Texte source (reverse du render)
// Lit le OOXML du paragraphe, trouve les <m:oMath>, convertit en texte
// ============================================================
function omathXmlToText(ooxml: string): string {
  try {
    const parser = new DOMParser();
    const doc = parser.parseFromString(ooxml, "text/xml");
    // Trouver tous les éléments m:oMath
    const oMaths = doc.getElementsByTagNameNS(
      "http://schemas.openxmlformats.org/officeDocument/2006/math", "oMath"
    );
    if (oMaths.length === 0) return "";
    // Convertir le premier OMath en texte
    return omathNodeToText(oMaths[0]);
  } catch {
    return "";
  }
}

function omathNodeToText(node: Element): string {
  const ns = "http://schemas.openxmlformats.org/officeDocument/2006/math";
  const tag = node.localName;

  switch (tag) {
    case "oMath":
    case "oMathPara":
    case "e":     // base element
    case "num":   // numerator
    case "den":   // denominator
    case "sup":   // superscript content
    case "sub":   // subscript content
      return childrenToText(node, ns);

    case "r": { // math run → lire <m:t>
      const t = node.getElementsByTagNameNS(ns, "t")[0];
      return t?.textContent ?? "";
    }

    case "f": { // fraction → (num)/(den)
      const num = node.getElementsByTagNameNS(ns, "num")[0];
      const den = node.getElementsByTagNameNS(ns, "den")[0];
      const n = num ? omathNodeToText(num) : "?";
      const d = den ? omathNodeToText(den) : "?";
      return `(${n})/(${d})`;
    }

    case "sSup": { // superscript → base^exp
      const e = node.getElementsByTagNameNS(ns, "e")[0];
      const sup = node.getElementsByTagNameNS(ns, "sup")[0];
      const b = e ? omathNodeToText(e) : "?";
      const s = sup ? omathNodeToText(sup) : "?";
      // Si la base est un seul caractère, pas besoin de parens
      return b.length <= 1 ? `${b}^${s}` : `(${b})^${s}`;
    }

    case "sSub": { // subscript → base_sub
      const e = node.getElementsByTagNameNS(ns, "e")[0];
      const sub = node.getElementsByTagNameNS(ns, "sub")[0];
      return `${e ? omathNodeToText(e) : "?"}_${sub ? omathNodeToText(sub) : "?"}`;
    }

    case "groupChr": { // vec, seg (flèche/barre au-dessus)
      const e = node.getElementsByTagNameNS(ns, "e")[0];
      const chrPr = node.getElementsByTagNameNS(ns, "groupChrPr")[0];
      const chr = chrPr?.getElementsByTagNameNS(ns, "chr")[0];
      const chrVal = chr?.getAttributeNS(ns, "val") ?? "";
      const inner = e ? omathNodeToText(e) : "?";
      if (chrVal === "\u2192") return `vec ${inner}`;
      if (chrVal === "\u00AF") return `seg ${inner}`;
      return inner;
    }

    case "rad": { // radical √
      const e = node.getElementsByTagNameNS(ns, "e")[0];
      return `sqrt(${e ? omathNodeToText(e) : "?"})`;
    }

    default:
      return childrenToText(node, ns);
  }
}

function childrenToText(node: Element, ns: string): string {
  let out = "";
  for (const child of node.children) {
    // Ignorer les éléments de propriétés (fPr, sSupPr, etc.)
    if (child.localName?.endsWith("Pr") || child.localName === "ctrlPr") continue;
    // Ignorer les runs Word (w:r) qui sont nos espaces ajoutés
    if (child.namespaceURI !== ns) {
      // C'est un w:r (texte normal) → lire le contenu
      const t = child.getElementsByTagName("w:t")[0];
      if (t?.textContent?.trim()) out += t.textContent;
      continue;
    }
    out += omathNodeToText(child);
  }
  return out;
}

function scanMathExpr(text: string): { raw: string; normalized: string } | null {
  const original = text.replace(/[\t\r\n]+$/, "").trimEnd();
  if (original.length < 1) return null;

  // Niveau 1 : délimiteur explicite (sur l'original)
  for (const { delim } of mathDelimiters.value) {
    const idx = original.lastIndexOf(delim);
    if (idx >= 0) {
      const raw = original.slice(idx + delim.length).trim();
      if (raw.length < 1) continue;
      return { raw, normalized: normalizeOMath(raw) };
    }
  }

  // Niveau 2 : scanner heuristique
  // On travaille sur les codepoints (gère les surrogate pairs des chars OMath)
  // normChars[i] = version ASCII du i-ème codepoint pour les décisions
  // chars[i] = codepoint original pour la tranche de recherche Word
  const chars = [...original];
  const norm = chars.map(c => normalizeOMath(c));

  let i = chars.length - 1;
  let parenDepth = 0;
  let bracketDepth = 0;
  let seenOMath = false; // avons-nous traversé du contenu OMath ?

  while (i >= 0) {
    const c = norm[i]; // version normalisée pour les décisions

    // Parenthèses
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

    // Dans des parenthèses : tout est valide
    if (parenDepth > 0 || bracketDepth > 0) { i--; continue; }

    // Chiffres, point, underscore
    if (/[\d._]/.test(c)) { i--; continue; }

    // Lettres
    if (/[a-zA-Z]/.test(c)) {
      // Vérifier si le char original est math italic (= contenu OMath existant)
      const origCp = chars[i].codePointAt(0)!;
      if (origCp >= 0x1D400 && origCp <= 0x1D7FF) seenOMath = true;

      let ws = i;
      while (ws > 0 && /[a-zA-Z]/.test(norm[ws - 1])) ws--;
      const word = norm.slice(ws, i + 1).join("");

      // Si on a déjà traversé de l'OMath et ce mot est en ASCII normal → c'est du texte → stop
      if (seenOMath && word.length > 0) {
        let wordIsRegularText = true;
        for (let k = ws; k <= i; k++) {
          const cp = chars[k].codePointAt(0)!;
          if (cp >= 0x1D400 && cp <= 0x1D7FF) { wordIsRegularText = false; break; }
        }
        if (wordIsRegularText) break; // "et" avant l'OMath → stop
      }

      // Mot de 2+ lettres non-math connu → stop (heuristique sans OMath)
      if (word.length > 1) {
        const mathWords = new Set(["sin","cos","tan","log","ln","exp","lim","det","dim","max","min",
          "alpha","beta","gamma","delta","epsilon","theta","lambda","mu","pi","sigma","omega","phi",
          "inf","sqrt","vide","vec","seg","ang"]);
        if (!mathWords.has(word.toLowerCase())) break;
      }
      i = ws - 1;
      continue;
    }

    // Opérateurs math
    if ("+-*/^=".includes(c)) { i--; continue; }

    // Virgule
    if (c === ",") { i--; continue; }

    // Espace
    if (c === " ") {
      if (i === 0) break;
      if (".;:!?".includes(norm[i - 1])) break;
      i--; continue;
    }

    // Tout autre caractère → stop
    break;
  }

  const start = i + 1;
  const rawExpr = chars.slice(start).join("").trim();
  const normExpr = norm.slice(start).join("").trim();
  if (normExpr.length < 1) return null;

  // Fixer les parens OMath (, → ( et . → ) si adjacentes à des chars math italic)
  const fixed = fixOMathParens(rawExpr, normExpr);

  return { raw: rawExpr, normalized: fixed };
}

// ============================================================
// TOKENIZER → AST → RENDER
// Phase 1 : Texte → Tokens
// Phase 2 : Tokens → AST (arbre, pas de XML)
// Phase 3 : AST → XML OMath (un seul passage propre)
// ============================================================

// --- AST Node types ---
type N =
  | { k: "num"; v: string }
  | { k: "var"; v: string }
  | { k: "op"; op: string; left: N; right: N }
  | { k: "unary"; op: string; child: N }
  | { k: "frac"; num: N; den: N }
  | { k: "sup"; base: N; exp: N }
  | { k: "paren"; d: "(" | "["; inner: N }
  | { k: "juxt"; parts: N[] }
  | { k: "empty" };

// --- Symboles résolus au tokenize ---
const WORD_SYM: Record<string, string> = {
  alpha: "\u03B1", beta: "\u03B2", gamma: "\u03B3", delta: "\u03B4",
  epsilon: "\u03B5", theta: "\u03B8", lambda: "\u03BB", mu: "\u03BC",
  pi: "\u03C0", sigma: "\u03C3", omega: "\u03C9", phi: "\u03C6",
  inf: "\u221E", infini: "\u221E", sqrt: "\u221A", vide: "\u2205",
};

// --- Phase 1 : Tokenize ---
type Tk = { t: "n" | "v" | "op" | "(" | ")" | "[" | "]"; v: string };

function tokenize(s: string): Tk[] {
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

// --- Phase 2 : Parse → AST ---
function parse(tks: Tk[]): N {
  const p = { i: 0 };
  const node = pAdd(tks, p);
  return node;
}

function pAdd(tks: Tk[], p: { i: number }): N {
  // Leading operator
  let left: N;
  if (p.i < tks.length && tks[p.i].t === "op" && "+-=".includes(tks[p.i].v)) {
    const op = tks[p.i++].v;
    left = { k: "unary", op, child: pMul(tks, p) };
  } else {
    left = pMul(tks, p);
  }
  while (p.i < tks.length && tks[p.i].t === "op" && "+-=,".includes(tks[p.i].v)) {
    const op = tks[p.i++].v;
    const right = pMul(tks, p);
    left = { k: "op", op, left, right };
  }
  return left;
}

function pMul(tks: Tk[], p: { i: number }): N {
  let left = pPow(tks, p);
  while (p.i < tks.length) {
    const nx = tks[p.i];
    if (nx.t === "op" && nx.v === "/") {
      p.i++;
      const right = pPow(tks, p);
      left = { k: "frac", num: left, den: right };
      continue;
    }
    if (nx.t === "op" && nx.v === "*") {
      p.i++;
      const right = pPow(tks, p);
      left = { k: "op", op: "\u00D7", left, right };
      continue;
    }
    // Juxtaposition : F(x), 2x, 3(a+b)
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

function pPow(tks: Tk[], p: { i: number }): N {
  if (p.i < tks.length && tks[p.i].t === "op" && "+-".includes(tks[p.i].v)) {
    const op = tks[p.i++].v;
    return { k: "unary", op, child: pPow(tks, p) };
  }
  const base = pAtom(tks, p);
  if (p.i < tks.length && tks[p.i].t === "op" && tks[p.i].v === "^") {
    p.i++;
    const exp = pPow(tks, p);
    return { k: "sup", base, exp };
  }
  return base;
}

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

// --- Stockage texte source : bookmark + document.settings (persiste) ---
let mathBookmarkCounter = 0;

function storeSource(bmName: string, sourceText: string) {
  Office.context.document.settings.set(bmName, sourceText);
  Office.context.document.settings.saveAsync();
}

function getSource(bmName: string): string | null {
  return Office.context.document.settings.get(bmName) as string | null;
}

function deleteSource(bmName: string) {
  Office.context.document.settings.remove(bmName);
  Office.context.document.settings.saveAsync();
}

// Insère un OMath et le wrappe dans un content control avec le texte source
async function insertOMathWithTag(
  ctx: Word.RequestContext,
  range: Word.Range,
  ooxml: string,
  sourceText: string,
  location: Word.InsertLocation,
): Promise<void> {
  const inserted = range.insertOoxml(ooxml, location);
  await ctx.sync();
  // Poser un bookmark sur l'OMath + stocker le texte source
  const bmName = `mathAddon_${++mathBookmarkCounter}`;
  inserted.insertBookmark(bmName);
  storeSource(bmName, sourceText);
  await ctx.sync();
  // Curseur après (fin du paragraphe, hors OMath)
  const fp = ctx.document.getSelection().paragraphs;
  fp.load("items");
  await ctx.sync();
  if (fp.items.length > 0) {
    fp.items[0].insertText(" ", Word.InsertLocation.end).select("End");
  }
  await ctx.sync();
}

// --- Word search escaping ---
// Dans Word search (matchWildcards=false), ^ est spécial (^t=tab, ^p=para...)
// ^^ = littéral ^
function wordEscape(s: string): string {
  return s.replace(/\^/g, "^^");
}

// --- Phase 3 : AST → OMath XML ---
function render(n: N): string {
  switch (n.k) {
    case "empty": return "";
    case "num": return mr(n.v);
    case "var": return mr(n.v);
    case "op":
      return render(n.left) + mr(n.op) + render(n.right);
    case "unary":
      return mr(n.op) + render(n.child);
    case "frac":
      // Les enfants directs de type paren perdent leurs parens (la barre groupe)
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

// Pour num/den de fraction : retire les parens du noeud racine seulement
function renderFracChild(n: N): string {
  if (n.k === "paren") return render(n.inner); // pas de parens visibles
  return render(n);
}

function astToString(n: N, depth = 0): string {
  const pad = "  ".repeat(depth);
  switch (n.k) {
    case "empty": return `${pad}(empty)`;
    case "num": return `${pad}num(${n.v})`;
    case "var": return `${pad}var(${n.v})`;
    case "op": return `${pad}op(${n.op})\n${astToString(n.left, depth+1)}\n${astToString(n.right, depth+1)}`;
    case "unary": return `${pad}unary(${n.op})\n${astToString(n.child, depth+1)}`;
    case "frac": return `${pad}frac\n${pad}  num:\n${astToString(n.num, depth+2)}\n${pad}  den:\n${astToString(n.den, depth+2)}`;
    case "sup": return `${pad}sup\n${astToString(n.base, depth+1)}\n${astToString(n.exp, depth+1)}`;
    case "paren": return `${pad}paren(${n.d})\n${astToString(n.inner, depth+1)}`;
    case "juxt": return `${pad}juxt\n${n.parts.map(p => astToString(p, depth+1)).join("\n")}`;
  }
}

function buildMathOoxml(raw: string): string {
  const steps: string[] = [];

  steps.push(`1. Input: "${raw}"`);
  steps.push(`1b. Normalized: "${normalizeOMath(raw)}"`);

  const tks = tokenize(raw);
  steps.push(`2. Tokens: ${tks.map(t => t.v).join(" ")}`);

  const ast = parse(tks);
  steps.push(`3. AST:\n${astToString(ast)}`);

  const xml = render(ast);
  steps.push(`4. XML (${xml.length} chars): ${xml.substring(0, 200)}...`);

  const ooxml = omathPkg(xml);
  steps.push(`5. OOXML total: ${ooxml.length} chars`);

  debugSteps.value = steps;
  return ooxml;
}

// ============================================================
// SYMBOL PATTERNS (regex) — pi, alpha, vec, >=, etc.
// Restent en regex car ce sont des mots-clés, pas des expressions
// ============================================================

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

const SYMBOLS: SymPattern[] = [
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

// ============================================================
// MATCHING — cherche symbole ou expression math
// ============================================================

function findSymbol(text: string): { raw: string; choices: DocChoice[] } | null {
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

function findExpression(text: string): { raw: string; choice: DocChoice } | null {
  const scan = scanMathExpr(text);
  if (!scan) return null;
  const { raw, normalized } = scan;
  // Doit contenir au moins un truc "math"
  if (!/[\/\^*]/.test(normalized) && !/\d\s+\d/.test(normalized) && !/[a-zA-Z]\(/.test(normalized) && !/[a-zA-Z]\s+\d/.test(normalized)) {
    return null;
  }
  try {
    // Parser travaille sur le normalisé, recherche Word utilise le raw
    const ooxml = buildMathOoxml(normalized);
    return {
      raw, // texte original pour la recherche Word
      choice: { label: "expression", display: normalized, replacement: normalized, ooxml },
    };
  } catch (e) {
    debugInfo.value = `Parse err: ${(e as Error).message}`;
    return null;
  }
}

// ============================================================
// WORD API — lecture + remplacement
// ============================================================

async function readPara(ctx: Word.RequestContext) {
  const sel = ctx.document.getSelection();
  const paras = sel.paragraphs;
  paras.load("items");
  await ctx.sync();
  if (paras.items.length === 0) return null;
  const para = paras.items[0];
  para.load("text");
  await ctx.sync();
  return { sel, para, text: para.text ?? "" };
}

async function doReplace(
  ctx: Word.RequestContext,
  para: Word.Paragraph,
  sel: Word.Range,
  searchStr: string,
  chosen: DocChoice,
): Promise<boolean> {
  const results = para.search(wordEscape(searchStr), { matchCase: false, matchWholeWord: false });
  results.load("items");
  await ctx.sync();

  if (results.items.length === 0) return false;

  const found = results.items[results.items.length - 1];
  const fullRange = found.expandTo(sel.getRange("End"));

  if (chosen.ooxml) {
    fullRange.delete();
    await ctx.sync();
    await insertOMathWithTag(ctx, ctx.document.getSelection(), chosen.ooxml, searchStr, Word.InsertLocation.replace);
  } else {
    fullRange.insertText(chosen.replacement + " ", Word.InsertLocation.replace)
      .getRange("End").select("End");
  }
  await ctx.sync();
  lastAction.value = `${searchStr} \u2192 ${chosen.display}`;
  replaceCount.value++;
  return true;
}

// ============================================================
// POLLING
// ============================================================

let fastId: ReturnType<typeof setInterval> | null = null;
let slowId: ReturnType<typeof setInterval> | null = null;
let isBusyFast = false;
let isBusySlow = false;
let isReplacing = false;
let lastSlowText = "";

// --- FAST TICK (50ms) : Tab → intercept + remplace ---
// Phase 1 : lire le texte, détecter \t, vérifier si y'a du math → supprimer le \t
// Phase 2 : analyser, parser, remplacer (peut être plus lent)
async function fastTick(): Promise<void> {
  if (isBusyFast || isReplacing || !isActive.value) return;
  if (typeof Word === "undefined" || !Word.run) return;

  isBusyFast = true;
  try {
    await Word.run(async (ctx) => {
      // === CHECK OMATH : curseur dans un OMath tagué ? → décomposer ===
      const data = await readPara(ctx);
      if (!data?.text) return;

      // Étape 1 : le paragraphe contient des chars OMath ? (JS pur, 0 sync)
      const hasOMathChars = [...data.text].some(c => (c.codePointAt(0) ?? 0) >= 0x1D400);

      if (hasOMathChars) {
        // Étape 2 : le curseur est dans l'OMath ? (1 sync)
        data.sel.font.load("name");
        await ctx.sync();

        const fontName = data.sel.font.name ?? "";

        if (fontName.includes("Cambria Math")) {
          isReplacing = true; // bloquer les ticks suivants
          // Curseur dans OMath → lire le XML, extraire le texte source
          data.para.load("text");
          const ooxmlResult = data.para.getOoxml();
          await ctx.sync();

          const sourceText = omathXmlToText(ooxmlResult.value);
          debugInfo.value = `DANS OMath \u2192 "${sourceText.slice(-40)}"`;

          if (sourceText) {
            const fullText = data.para.text ?? "";
            const normalized = fixOMathParens(fullText, normalizeOMath(fullText));
            // Remplacer + reset font (sinon Cambria Math persiste et reboucle)
            const newRange = data.para.insertText(normalized, Word.InsertLocation.replace);
            newRange.font.set({ name: "" }); // reset au défaut du document
            await ctx.sync();
            ctx.document.getSelection().getRange("End").select("End");
            await ctx.sync();
            lastSlowText = "";
            lastAction.value = `\u21A9 ${sourceText}`;
          }
          isReplacing = false;
          return;
        }
      }

      // === TAB : convertir texte → OMath ===
      if (!/\t/.test(data.text)) return;

      const textBeforeTab = data.text.replace(/\t[\s\S]*$/, "");

      // Heuristique rapide (JS pur, 0 API call) : y'a-t-il du math ?
      const hasSugg = suggestions.value.length > 0;
      const hasDelim = mathDelimiters.value.some(d => textBeforeTab.includes(d.delim));
      const hasMathOps = /[\/\^*]/.test(textBeforeTab);
      const hasFuncCall = /[a-zA-Z]\(/.test(textBeforeTab);
      const hasExponent = /[a-zA-Z\d]\s+\d/.test(textBeforeTab);
      const hasSymbol = findSymbol(textBeforeTab) !== null;

      if (!hasSugg && !hasDelim && !hasMathOps && !hasFuncCall && !hasExponent && !hasSymbol) {
        return; // rien de math → tab normal
      }

      // === MODE CONVERSION : texte → OMath ===
      isReplacing = true;
      debugInfo.value = `TAB: "${textBeforeTab.slice(-40)}"`;

      const tabSearch = data.para.search("^t", { matchCase: false, matchWholeWord: false });
      tabSearch.load("items");
      await ctx.sync();
      if (tabSearch.items.length > 0) {
        tabSearch.items[tabSearch.items.length - 1].delete();
        await ctx.sync();
      }

      // === PHASE 2 : analyser et remplacer ===

      // Relire le texte propre (tab supprimé)
      data.para.load("text");
      const freshSel = ctx.document.getSelection();
      const freshParas = freshSel.paragraphs;
      freshParas.load("items");
      await ctx.sync();
      const cleanText = data.para.text ?? "";
      const freshPara = freshParas.items[0] ?? data.para;

      // 1) Symbole ? → passe par doReplace (un seul chemin)
      const symMatch = findSymbol(cleanText);
      if (symMatch) {
        const chosen = symMatch.choices[selectedIdx.value] ?? symMatch.choices[0];
        await doReplace(ctx, freshPara, freshSel, symMatch.raw, chosen);
        lastSlowText = "";
        return;
      }

      // 2) Expression math ?
      const exprMatch = findExpression(cleanText);
      if (exprMatch) {
        // Gérer le délimiteur : inclure dans la recherche
        let searchStr = exprMatch.raw;
        let delimReplace = "";
        for (const { delim, replace } of mathDelimiters.value) {
          if (cleanText.includes(delim)) {
            searchStr = delim + exprMatch.raw;
            delimReplace = replace;
            break;
          }
        }

        if (delimReplace) {
          // Avec délimiteur : remplacer delim+expr par delimReplace+OMath
          const results = freshPara.search(wordEscape(searchStr), { matchCase: false, matchWholeWord: false });
          results.load("items");
          await ctx.sync();
          if (results.items.length > 0) {
            const found = results.items[results.items.length - 1];
            if (exprMatch.choice.ooxml) {
              const replaced = found.insertText(delimReplace, Word.InsertLocation.replace);
              await insertOMathWithTag(ctx, replaced.getRange("End"), exprMatch.choice.ooxml, exprMatch.choice.display, Word.InsertLocation.after);
            } else {
              found.insertText(delimReplace + exprMatch.choice.replacement + " ", Word.InsertLocation.replace)
                .getRange("End").select("End");
              await ctx.sync();
            }
            lastAction.value = `${exprMatch.choice.display} \u2192 expr`;
            replaceCount.value++;
          }
        } else {
          // Sans délimiteur : passe par doReplace (un seul chemin)
          await doReplace(ctx, freshPara, freshSel, exprMatch.raw, exprMatch.choice);
        }

        suggestions.value = []; selectedIdx.value = 0; lastSlowText = "";
        isReplacing = false;
        return;
      }

      isReplacing = false;
    });
  } catch (err) {
    debugInfo.value = `Err: ${(err as Error).message}`;
    isReplacing = false;
  } finally {
    isBusyFast = false;
  }
}

// --- SLOW TICK (500ms) : preview suggestions ---
async function slowTick(): Promise<void> {
  if (isBusySlow || isReplacing || !isActive.value) return;
  if (typeof Word === "undefined" || !Word.run) return;

  isBusySlow = true;
  try {
    await Word.run(async (ctx) => {
      const data = await readPara(ctx);
      if (!data?.text) { suggestions.value = []; lastSlowText = ""; return; }
      if (data.text === lastSlowText) return;
      lastSlowText = data.text;

      const trimmed = data.text.replace(/[\s\t]+$/, "");
      debugInfo.value = `"${trimmed.slice(-40)}"`;

      // 1) Symbole ?
      const sym = findSymbol(trimmed);
      if (sym) {
        suggestions.value = sym.choices;
        matchedRaw.value = sym.raw;
        if (selectedIdx.value >= sym.choices.length) selectedIdx.value = 0;
        return;
      }

      // 2) Expression math ?
      const expr = findExpression(trimmed);
      if (expr) {
        suggestions.value = [expr.choice];
        matchedRaw.value = expr.raw;
        return;
      }

      suggestions.value = [];
      matchedRaw.value = "";
    });
  } catch (err) {
    debugInfo.value = `Err: ${(err as Error).message}`;
  } finally {
    isBusySlow = false;
  }
}

// --- Start / Stop ---
export function startWatcher(): void {
  if (fastId) return;
  fastId = setInterval(fastTick, 50);
  slowId = setInterval(slowTick, 500);
  isActive.value = true;
}

export function stopWatcher(): void {
  if (fastId) { clearInterval(fastId); fastId = null; }
  if (slowId) { clearInterval(slowId); slowId = null; }
  isActive.value = false;
  suggestions.value = [];
}

export function toggleWatcher(): void {
  if (isActive.value) stopWatcher(); else startWatcher();
}
