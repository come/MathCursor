// ============================================================
// patterns.ts — Table complète des patterns de notation math
// ============================================================

export interface PatternChoice {
  label: string;
  display: string;
  text?: string;
  ooxml?: string;
}

export interface MathPattern {
  re: RegExp;
  rawKey: string;
  category: string;
  resolve: (match: RegExpMatchArray) => PatternChoice[];
}

export interface MatchResult {
  pattern: MathPattern;
  match: RegExpMatchArray;
  startIndex: number;
  choices: PatternChoice[];
}

// --- Ensembles standard ---
const SETS: Record<string, string> = {
  R: "\u211D", N: "\u2115", Z: "\u2124", Q: "\u211A", C: "\u2102",
};

// --- Unicode sub/superscript ---
const SUP: Record<string, string> = {
  "0": "\u2070", "1": "\u00B9", "2": "\u00B2", "3": "\u00B3", "4": "\u2074",
  "5": "\u2075", "6": "\u2076", "7": "\u2077", "8": "\u2078", "9": "\u2079",
  "+": "\u207A", "-": "\u207B", "n": "\u207F", "k": "\u1D4F",
  "a": "\u1D43", "b": "\u1D47", "i": "\u2071", "x": "\u02E3",
};
const SUB: Record<string, string> = {
  "0": "\u2080", "1": "\u2081", "2": "\u2082", "3": "\u2083", "4": "\u2084",
  "5": "\u2085", "6": "\u2086", "7": "\u2087", "8": "\u2088", "9": "\u2089",
  "a": "\u2090", "e": "\u2091", "i": "\u1D62", "o": "\u2092",
  "n": "\u2099", "k": "\u2096", "=": "\u208C",
};

function toSup(s: string): string {
  return [...s].map((c) => SUP[c] ?? c).join("");
}
function toSub(s: string): string {
  return [...s].map((c) => SUB[c] ?? c).join("");
}

// --- OMath helpers ---
function mr(text: string): string {
  return `<m:r><w:rPr><w:rFonts w:ascii="Cambria Math" w:hAnsi="Cambria Math"/></w:rPr><m:t>${text}</m:t></m:r>`;
}

function omathWrap(inner: string): string {
  return [
    `<pkg:package xmlns:pkg="http://schemas.microsoft.com/office/2006/xmlPackage">`,
    `<pkg:part pkg:name="/_rels/.rels" pkg:contentType="application/vnd.openxmlformats-package.relationships+xml"><pkg:xmlData>`,
    `<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">`,
    `<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>`,
    `</Relationships></pkg:xmlData></pkg:part>`,
    `<pkg:part pkg:name="/word/document.xml" pkg:contentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"><pkg:xmlData>`,
    `<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">`,
    `<w:body><w:p><m:oMath>${inner}</m:oMath></w:p></w:body>`,
    `</w:document></pkg:xmlData></pkg:part></pkg:package>`,
  ].join("");
}

function oFrac(num: string, den: string): string {
  return omathWrap(`<m:f><m:num>${mr(num)}</m:num><m:den>${mr(den)}</m:den></m:f>`);
}

function oSup(base: string, sup: string): string {
  return omathWrap(`<m:sSup><m:e>${mr(base)}</m:e><m:sup>${mr(sup)}</m:sup></m:sSup>`);
}

function oSub(base: string, sub: string): string {
  return omathWrap(`<m:sSub><m:e>${mr(base)}</m:e><m:sub>${mr(sub)}</m:sub></m:sSub>`);
}

function oSubSup(base: string, sub: string, sup: string): string {
  return omathWrap(
    `<m:sSubSup><m:e>${mr(base)}</m:e><m:sub>${mr(sub)}</m:sub><m:sup>${mr(sup)}</m:sup></m:sSubSup>`
  );
}

function oAccent(text: string, chr: string): string {
  return omathWrap(`<m:acc><m:accPr><m:chr m:val="${chr}"/></m:accPr><m:e>${mr(text)}</m:e></m:acc>`);
}

function oRad(content: string, degree?: string): string {
  if (degree) {
    return omathWrap(`<m:rad><m:deg>${mr(degree)}</m:deg><m:e>${mr(content)}</m:e></m:rad>`);
  }
  return omathWrap(`<m:rad><m:radPr><m:degHide m:val="1"/></m:radPr><m:deg/><m:e>${mr(content)}</m:e></m:rad>`);
}

function oNary(chr: string, sub: string, sup: string): string {
  return omathWrap(
    `<m:nary><m:naryPr><m:chr m:val="${chr}"/></m:naryPr><m:sub>${mr(sub)}</m:sub><m:sup>${mr(sup)}</m:sup><m:e>${mr("\u25A1")}</m:e></m:nary>`
  );
}

// ============================================================
// Fragments réutilisables pour les regex flexibles
// ============================================================
// Quantificateur ∀ : V, pt, pour tout, pourtout, qq (quelque soit)
const Q_FORALL = `(?:V|pt|pour\\s+tout|pourtout|qq)`;
// Quantificateur ∃ : E, ie, il existe, ilexiste
const Q_EXISTS = `(?:E|ie|il\\s+existe|ilexiste)`;
// Opérateur d'appartenance ∈ : (, c, dans, in, app, de
const OP_IN = `(?:\\(|c|dans|in|app|de)`;
// Ensemble : R N Z Q C
const SET = `([RNZQC])`;

// ============================================================
// PATTERNS — ordonnés du plus spécifique au moins spécifique
// ============================================================

export const patterns: MathPattern[] = [
  // ==================== LOGIQUE COMPOSÉE ====================

  // ∀x ∈ ℝ — accepte: V x ( R, pt x c R, pour tout x dans R, qq x in N, ...
  {
    re: new RegExp(`${Q_FORALL}\\s+([a-zA-Z])\\s+${OP_IN}\\s*${SET}$`),
    rawKey: "V x ( R",
    category: "logique",
    resolve: (m) => [{
      label: "pour tout",
      display: `\u2200${m[1]} \u2208 ${SETS[m[2]]}`,
      text: `\u2200${m[1]} \u2208 ${SETS[m[2]]}`,
    }],
  },
  // ∃x ∈ ℝ — accepte: E x ( R, ie x c R, il existe x dans R, ...
  {
    re: new RegExp(`${Q_EXISTS}\\s+([a-zA-Z])\\s+${OP_IN}\\s*${SET}$`),
    rawKey: "E x ( R",
    category: "logique",
    resolve: (m) => [{
      label: "il existe",
      display: `\u2203${m[1]} \u2208 ${SETS[m[2]]}`,
      text: `\u2203${m[1]} \u2208 ${SETS[m[2]]}`,
    }],
  },
  // ∃!x — accepte: E! x, ie! x
  {
    re: new RegExp(`${Q_EXISTS}!\\s+([a-zA-Z])$`),
    rawKey: "E! x",
    category: "logique",
    resolve: (m) => [{
      label: "il existe un unique",
      display: `\u2203!${m[1]}`,
      text: `\u2203!${m[1]}`,
    }],
  },

  // ==================== ANALYSE COMPOSÉE ====================

  // sum i=1 n → Σᵢ₌₁ⁿ
  {
    re: /sum\s+(\w)=(\w+)\s+(\w+)$/,
    rawKey: "sum",
    category: "algèbre",
    resolve: (m) => [{
      label: "somme",
      display: `\u03A3${toSub(m[1] + "=" + m[2])}${toSup(m[3])}`,
      ooxml: oNary("\u2211", `${m[1]}=${m[2]}`, m[3]),
    }],
  },
  // prod i=1 n → Πᵢ₌₁ⁿ
  {
    re: /prod\s+(\w)=(\w+)\s+(\w+)$/,
    rawKey: "prod",
    category: "algèbre",
    resolve: (m) => [{
      label: "produit",
      display: `\u03A0${toSub(m[1] + "=" + m[2])}${toSup(m[3])}`,
      ooxml: oNary("\u220F", `${m[1]}=${m[2]}`, m[3]),
    }],
  },
  // lim ->0+ / lim ->0- (avec signe)
  {
    re: /lim\s+->([a-zA-Z0-9]+)(\+|-)$/,
    rawKey: "lim ->",
    category: "analyse",
    resolve: (m) => {
      const target = m[1] === "inf" ? "+\u221E" : m[1];
      const sign = m[2] === "+" ? "\u207A" : "\u207B";
      return [{
        label: "limite",
        display: `lim \u2192 ${target}${sign}`,
        text: `lim \u2192 ${target}${sign}`,
      }];
    },
  },
  // lim ->inf / lim ->0
  {
    re: /lim\s+->([a-zA-Z0-9]+)$/,
    rawKey: "lim ->",
    category: "analyse",
    resolve: (m) => {
      const target = m[1] === "inf" ? "+\u221E" : m[1] === "-inf" ? "-\u221E" : m[1];
      return [{
        label: "limite",
        display: `lim \u2192 ${target}`,
        text: `lim \u2192 ${target}`,
      }];
    },
  },
  // int a b → ∫ₐᵇ
  {
    re: /int\s+(\w+)\s+(\w+)$/,
    rawKey: "int",
    category: "analyse",
    resolve: (m) => [{
      label: "intégrale",
      display: `\u222B${toSub(m[1])}${toSup(m[2])}`,
      ooxml: oNary("\u222B", m[1], m[2]),
    }],
  },

  // ==================== GÉOMÉTRIE / VECTEURS ====================

  // vec AB → vecteur
  {
    re: /vec\s+([A-Za-z]+)$/,
    rawKey: "vec",
    category: "géométrie",
    resolve: (m) => [{
      label: "vecteur",
      display: `\u2192${m[1].toUpperCase()}`,
      ooxml: oAccent(m[1].toUpperCase(), "\u2192"),
    }],
  },
  // seg AB → segment
  {
    re: /seg\s+([A-Za-z]+)$/,
    rawKey: "seg",
    category: "géométrie",
    resolve: (m) => [{
      label: "segment",
      display: `\u0305${m[1].toUpperCase()}`,
      ooxml: oAccent(m[1].toUpperCase(), "\u00AF"),
    }],
  },
  // ang ABC → ∠ABC
  {
    re: /ang\s+([A-Za-z]+)$/,
    rawKey: "ang",
    category: "géométrie",
    resolve: (m) => [{
      label: "angle",
      display: `\u2220${m[1].toUpperCase()}`,
      text: `\u2220${m[1].toUpperCase()}`,
    }],
  },
  // ||v|| → ‖v‖
  {
    re: /\|\|([a-zA-Z0-9]+)\|\|$/,
    rawKey: "||…||",
    category: "géométrie",
    resolve: (m) => [{
      label: "norme",
      display: `\u2016${m[1]}\u2016`,
      text: `\u2016${m[1]}\u2016`,
    }],
  },

  // ==================== COMBINATOIRE ====================

  // Cn k → Cₙᵏ
  {
    re: /C([a-z0-9]+)\s+([a-z0-9]+)$/,
    rawKey: "Cn k",
    category: "algèbre",
    resolve: (m) => [{
      label: "combinaison",
      display: `C${toSub(m[1])}${toSup(m[2])}`,
      ooxml: oSubSup("C", m[1], m[2]),
    }],
  },
  // An k → Aₙᵏ
  {
    re: /A([a-z0-9]+)\s+([a-z0-9]+)$/,
    rawKey: "An k",
    category: "algèbre",
    resolve: (m) => [{
      label: "arrangement",
      display: `A${toSub(m[1])}${toSup(m[2])}`,
      ooxml: oSubSup("A", m[1], m[2]),
    }],
  },

  // ==================== ENSEMBLES ====================

  // ∉ ℝ — accepte: !( R, !c R, !dans R, !in R, !app R, napp R
  {
    re: new RegExp(`(?:!${OP_IN}|napp|n'app|notin)\\s*${SET}$`),
    rawKey: "!( R",
    category: "ensembles",
    resolve: (m) => [{
      label: "n'appartient pas",
      display: `\u2209 ${SETS[m[1]]}`,
      text: `\u2209 ${SETS[m[1]]}`,
    }],
  },
  // ⊂ ℝ — accepte: sub R, inc R, inclus R, ss R (sous-ensemble)
  {
    re: new RegExp(`(?:sub|inc|inclus|ss)\\s+${SET}$`),
    rawKey: "sub R",
    category: "ensembles",
    resolve: (m) => [{
      label: "inclus dans",
      display: `\u2282 ${SETS[m[1]]}`,
      text: `\u2282 ${SETS[m[1]]}`,
    }],
  },
  // ∈ ℝ — accepte: ( R, c R, dans R, in R, app R, de R
  // (après les composés V x ( R et E x ( R)
  {
    re: new RegExp(`${OP_IN}\\s*${SET}$`),
    rawKey: "( R",
    category: "ensembles",
    resolve: (m) => [{
      label: "appartient à",
      display: `\u2208 ${SETS[m[1]]}`,
      text: `\u2208 ${SETS[m[1]]}`,
    }],
  },

  // ==================== OPÉRATIONS ENSEMBLISTES ====================

  // AuB → A ∪ B
  {
    re: /([A-Z])u([A-Z])$/,
    rawKey: "AuB",
    category: "ensembles",
    resolve: (m) => [{
      label: "union",
      display: `${m[1]} \u222A ${m[2]}`,
      text: `${m[1]} \u222A ${m[2]}`,
    }],
  },
  // AnB → A ∩ B (attention : n minuscule entre deux majuscules)
  {
    re: /([A-Z])n([A-Z])$/,
    rawKey: "AnB",
    category: "ensembles",
    resolve: (m) => [{
      label: "intersection",
      display: `${m[1]} \u2229 ${m[2]}`,
      text: `${m[1]} \u2229 ${m[2]}`,
    }],
  },
  // A\B → A ∖ B
  {
    re: /([A-Z])\\([A-Z])$/,
    rawKey: "A\\B",
    category: "ensembles",
    resolve: (m) => [{
      label: "différence",
      display: `${m[1]} \u2216 ${m[2]}`,
      text: `${m[1]} \u2216 ${m[2]}`,
    }],
  },

  // ==================== OPÉRATEURS (par longueur décroissante) ====================

  // <=> → ⟺
  {
    re: /<=>$/,
    rawKey: "<=>",
    category: "logique",
    resolve: () => [{
      label: "équivalent",
      display: "\u27FA",
      text: "\u27FA",
    }],
  },
  // => → ⟹
  {
    re: /=>$/,
    rawKey: "=>",
    category: "logique",
    resolve: () => [{
      label: "implique",
      display: "\u27F9",
      text: "\u27F9",
    }],
  },
  // >= → ≥
  {
    re: />=$/,
    rawKey: ">=",
    category: "opérateurs",
    resolve: () => [{
      label: "supérieur ou égal",
      display: "\u2265",
      text: "\u2265",
    }],
  },
  // <= → ≤
  {
    re: /<=$/,
    rawKey: "<=",
    category: "opérateurs",
    resolve: () => [{
      label: "inférieur ou égal",
      display: "\u2264",
      text: "\u2264",
    }],
  },
  // != → ≠
  {
    re: /!=$/,
    rawKey: "!=",
    category: "opérateurs",
    resolve: () => [{
      label: "différent",
      display: "\u2260",
      text: "\u2260",
    }],
  },

  // ==================== DÉRIVÉES ====================

  // f'' → f″ (double prime AVANT simple prime)
  {
    re: /([a-zA-Z])''$/,
    rawKey: "f''",
    category: "analyse",
    resolve: (m) => [{
      label: "dérivée seconde",
      display: `${m[1]}\u2033`,
      text: `${m[1]}\u2033`,
    }],
  },
  // f' → f′
  {
    re: /([a-zA-Z])'$/,
    rawKey: "f'",
    category: "analyse",
    resolve: (m) => [{
      label: "dérivée",
      display: `${m[1]}\u2032`,
      text: `${m[1]}\u2032`,
    }],
  },

  // ==================== DOT / CROSS PRODUCT ====================

  // u.v → u·v
  {
    re: /([a-z])\.([a-z])$/,
    rawKey: "u.v",
    category: "géométrie",
    resolve: (m) => [{
      label: "produit scalaire",
      display: `${m[1]}\u00B7${m[2]}`,
      text: `${m[1]}\u00B7${m[2]}`,
    }],
  },
  // u^v → u∧v
  {
    re: /([a-z])\^([a-z])$/,
    rawKey: "u^v",
    category: "géométrie",
    resolve: (m) => [{
      label: "produit vectoriel",
      display: `${m[1]}\u2227${m[2]}`,
      text: `${m[1]}\u2227${m[2]}`,
    }],
  },

  // ==================== EXPOSANT / PRODUIT (multi-choix) ====================

  {
    re: /(\d+)\s+(\d+)$/,
    rawKey: "espace",
    category: "fractions",
    resolve: (m) => [
      {
        label: "exposant",
        display: `${m[1]}${toSup(m[2])}`,
        ooxml: oSup(m[1], m[2]),
      },
      {
        label: "produit",
        display: `${m[1]}\u00D7${m[2]}`,
        text: `${m[1]}\u00D7${m[2]}`,
      },
      {
        label: "concaténation",
        display: `${m[1]}${m[2]}`,
        text: `${m[1]}${m[2]}`,
      },
    ],
  },

  // ==================== FRACTION ====================

  {
    re: /(\d+)\/$/,
    rawKey: "/",
    category: "fractions",
    resolve: (m) => [{
      label: "fraction",
      display: `${m[1]}/\u25A1`,
      ooxml: oFrac(m[1], "\u2026"),
    }],
  },

  // ==================== MOTS-CLÉS (longueur décroissante) ====================

  // Négation
  {
    re: /~$/,
    rawKey: "~",
    category: "logique",
    resolve: () => [{
      label: "négation",
      display: "\u00AC",
      text: "\u00AC",
    }],
  },
  // Ensemble vide
  {
    re: /vide$/,
    rawKey: "vide",
    category: "ensembles",
    resolve: () => [{
      label: "ensemble vide",
      display: "\u2205",
      text: "\u2205",
    }],
  },
  // Racine carrée
  {
    re: /sqrt$/,
    rawKey: "sqrt",
    category: "fractions",
    resolve: () => [{
      label: "racine carrée",
      display: "\u221A\u25A1",
      ooxml: oRad("\u2026"),
    }],
  },
  // Racine n-ième
  {
    re: /nrt$/,
    rawKey: "nrt",
    category: "fractions",
    resolve: () => [{
      label: "racine n-ième",
      display: "\u207F\u221A\u25A1",
      ooxml: oRad("\u2026", "n"),
    }],
  },

  // ==================== INFINI ====================

  {
    re: /-inf$/,
    rawKey: "-inf",
    category: "analyse",
    resolve: () => [{
      label: "moins l'infini",
      display: "-\u221E",
      text: "-\u221E",
    }],
  },
  {
    re: /\+inf$/,
    rawKey: "+inf",
    category: "analyse",
    resolve: () => [{
      label: "plus l'infini",
      display: "+\u221E",
      text: "+\u221E",
    }],
  },
  {
    re: /inf$/,
    rawKey: "inf",
    category: "analyse",
    resolve: () => [{
      label: "infini",
      display: "\u221E",
      text: "\u221E",
    }],
  },

  // ==================== LETTRES GRECQUES (longueur décroissante) ====================

  // 7+ lettres
  {
    re: /epsilon$/,
    rawKey: "epsilon",
    category: "grec",
    resolve: () => [{ label: "\u03B5 epsilon", display: "\u03B5", text: "\u03B5" }],
  },
  // 6 lettres
  {
    re: /lambda$/,
    rawKey: "lambda",
    category: "grec",
    resolve: () => [{ label: "\u03BB lambda", display: "\u03BB", text: "\u03BB" }],
  },
  // 5 lettres
  {
    re: /alpha$/,
    rawKey: "alpha",
    category: "grec",
    resolve: () => [{ label: "\u03B1 alpha", display: "\u03B1", text: "\u03B1" }],
  },
  {
    re: /delta$/,
    rawKey: "delta",
    category: "grec",
    resolve: () => [{ label: "\u03B4 delta", display: "\u03B4", text: "\u03B4" }],
  },
  {
    re: /Delta$/,
    rawKey: "Delta",
    category: "grec",
    resolve: () => [{ label: "\u0394 Delta", display: "\u0394", text: "\u0394" }],
  },
  {
    re: /theta$/,
    rawKey: "theta",
    category: "grec",
    resolve: () => [{ label: "\u03B8 thêta", display: "\u03B8", text: "\u03B8" }],
  },
  {
    re: /sigma$/,
    rawKey: "sigma",
    category: "grec",
    resolve: () => [{ label: "\u03C3 sigma", display: "\u03C3", text: "\u03C3" }],
  },
  {
    re: /Sigma$/,
    rawKey: "Sigma",
    category: "grec",
    resolve: () => [{ label: "\u03A3 Sigma", display: "\u03A3", text: "\u03A3" }],
  },
  {
    re: /omega$/,
    rawKey: "omega",
    category: "grec",
    resolve: () => [{ label: "\u03C9 oméga", display: "\u03C9", text: "\u03C9" }],
  },
  {
    re: /Omega$/,
    rawKey: "Omega",
    category: "grec",
    resolve: () => [{ label: "\u03A9 Oméga", display: "\u03A9", text: "\u03A9" }],
  },
  // 4 lettres
  {
    re: /beta$/,
    rawKey: "beta",
    category: "grec",
    resolve: () => [{ label: "\u03B2 bêta", display: "\u03B2", text: "\u03B2" }],
  },
  {
    re: /gamma$/,
    rawKey: "gamma",
    category: "grec",
    resolve: () => [{ label: "\u03B3 gamma", display: "\u03B3", text: "\u03B3" }],
  },
  // 3 lettres
  {
    re: /phi$/,
    rawKey: "phi",
    category: "grec",
    resolve: () => [{ label: "\u03C6 phi", display: "\u03C6", text: "\u03C6" }],
  },
  // 2 lettres
  {
    re: /mu$/,
    rawKey: "mu",
    category: "grec",
    resolve: () => [{ label: "\u03BC mu", display: "\u03BC", text: "\u03BC" }],
  },
  {
    re: /pi$/,
    rawKey: "pi",
    category: "grec",
    resolve: () => [{ label: "\u03C0 pi", display: "\u03C0", text: "\u03C0" }],
  },
];

// ============================================================
// findMatch — collecte TOUS les patterns qui matchent, fusionne
// les choix dans un picker unique. Seuls les matches qui démarrent
// au point le plus tôt (= les plus spécifiques) sont retenus.
// ============================================================

export function findMatch(text: string): MatchResult | null {
  let earliestStart = Infinity;
  const candidates: {
    pattern: MathPattern;
    match: RegExpMatchArray;
    startIndex: number;
    choices: PatternChoice[];
  }[] = [];

  for (const pattern of patterns) {
    const match = text.match(pattern.re);
    if (match) {
      const start = match.index ?? 0;
      if (start < earliestStart) earliestStart = start;
      candidates.push({
        pattern,
        match,
        startIndex: start,
        choices: pattern.resolve(match),
      });
    }
  }

  if (candidates.length === 0) return null;

  // Ne garder que les matches au point le plus tôt (les plus spécifiques)
  const relevant = candidates.filter((c) => c.startIndex === earliestStart);
  const anchor = relevant[0];

  // Fusionner tous les choix, dédupliquer par display
  const seen = new Set<string>();
  const allChoices: PatternChoice[] = [];
  for (const c of relevant) {
    for (const choice of c.choices) {
      if (!seen.has(choice.display)) {
        seen.add(choice.display);
        allChoices.push(choice);
      }
    }
  }

  return {
    pattern: anchor.pattern,
    match: anchor.match,
    startIndex: anchor.startIndex,
    choices: allChoices,
  };
}

// ============================================================
// Détection de matière (heuristique document)
// ============================================================

const MATH_KEYWORDS = [
  "fonction", "dérivée", "intégrale", "limite", "vecteur",
  "équation", "inéquation", "polynôme", "matrice", "probabilité",
  "théorème", "démonstration", "soit", "pour tout", "il existe",
  "chapitre", "exercice", "maths", "mathématiques", "géométrie",
  "algèbre", "analyse", "trigonométrie", "logarithme", "exponentielle",
];

const MATH_TITLE_KEYWORDS = ["maths", "math", "géo", "algo"];

export function detectMathContext(text: string): boolean {
  const lower = text.toLowerCase().substring(0, 300);
  let count = 0;
  for (const kw of MATH_KEYWORDS) {
    if (lower.includes(kw)) count++;
    if (count >= 2) return true;
  }
  return false;
}

export function detectMathTitle(title: string): boolean {
  const lower = title.toLowerCase();
  return MATH_TITLE_KEYWORDS.some((kw) => lower.includes(kw));
}
