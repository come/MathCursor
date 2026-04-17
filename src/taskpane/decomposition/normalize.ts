// Normalisation OMath → ASCII
// Word retourne les caractères OMath en math italic (𝑔→g, 𝑥→x)

export function normalizeOMath(s: string): string {
  let out = "";
  for (const c of s) {
    const cp = c.codePointAt(0)!;
    if (cp >= 0x1D434 && cp <= 0x1D44D) { out += String.fromCharCode(65 + cp - 0x1D434); continue; }
    if (cp >= 0x1D44E && cp <= 0x1D454) { out += String.fromCharCode(97 + cp - 0x1D44E); continue; }
    if (cp === 0x210E) { out += "h"; continue; }
    if (cp >= 0x1D456 && cp <= 0x1D467) { out += String.fromCharCode(105 + cp - 0x1D456); continue; }
    if (cp >= 0x1D400 && cp <= 0x1D419) { out += String.fromCharCode(65 + cp - 0x1D400); continue; }
    if (cp >= 0x1D41A && cp <= 0x1D433) { out += String.fromCharCode(97 + cp - 0x1D41A); continue; }
    if (cp >= 0x1D468 && cp <= 0x1D481) { out += String.fromCharCode(65 + cp - 0x1D468); continue; }
    if (cp >= 0x1D482 && cp <= 0x1D49B) { out += String.fromCharCode(97 + cp - 0x1D482); continue; }
    if (cp === 0x00D7) { out += "*"; continue; }
    if (cp === 0x2212) { out += "-"; continue; }
    if (cp === 0x2013) { out += "-"; continue; }
    if (cp === 0x2014) { out += "-"; continue; }
    out += c;
  }
  return out;
}

// OMath parens : Word rend ( → , et ) → . dans para.text pour le contenu OMath
export function fixOMathParens(original: string, normalized: string): string {
  const origChars = [...original];
  const result = [...normalized];
  for (let i = 0; i < origChars.length && i < result.length; i++) {
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

// Check si un texte contient des chars OMath (math italic U+1D400+)
export function hasOMathChars(text: string): boolean {
  return [...text].some(c => (c.codePointAt(0) ?? 0) >= 0x1D400);
}
