// Conversion : texte → OMath
// Façade qui combine scanner + tokenizer + parser + render
// Le tokenizer v2 (enrichi) alimente le zone-detector
// Le parser v1 (récursif) utilise encore l'ancien format Tk — un adaptateur fait le pont

import type { DocChoice, Tk, Token, MathZone } from "./types";
import type { Delimiter } from "./scanner";
import { scanMathExpr } from "./scanner";
import { tokenize as tokenizeV2 } from "./tokenizer";
import { scoreAllTokens } from "./scorer";
import { detectMathZone } from "./zone-detector";
import { tokenize as tokenizeV1 } from "./tokenizer-v1";
import { parse, astToString } from "./parser";
import { render } from "./render";
import { omathPkg } from "../omath/helpers";

export type { DocChoice, Delimiter, Token, MathZone };
export { scanMathExpr, tokenizeV2, scoreAllTokens, detectMathZone };

// ============================================================
// BUILD OOXML (utilise le parser v1 pour l'instant)
// ============================================================

export function buildMathOoxml(raw: string, debugSteps?: string[]): string {
  debugSteps?.push(`1. Input: "${raw}"`);

  const tks = tokenizeV1(raw);
  debugSteps?.push(`2. Tokens: ${tks.map(t => t.v).join(" ")}`);

  const ast = parse(tks);
  debugSteps?.push(`3. AST:\n${astToString(ast)}`);

  const xml = render(ast);
  debugSteps?.push(`4. XML (${xml.length} chars): ${xml.substring(0, 200)}...`);

  const ooxml = omathPkg(xml);
  debugSteps?.push(`5. OOXML total: ${ooxml.length} chars`);

  return ooxml;
}

// ============================================================
// FIND EXPRESSION — v2 avec zone detection
// ============================================================

export function findExpressionV2(
  text: string,
  debugInfo?: { value: string },
): { raw: string; normalized: string; zone: MathZone } | null {
  const tokens = tokenizeV2(text);
  scoreAllTokens(tokens);
  const zone = detectMathZone(tokens);
  if (!zone) return null;

  if (debugInfo) {
    const scores = zone.tokens
      .filter(t => !t.categories.has("whitespace"))
      .map(t => `${t.normalized}(${t.mathiness.toFixed(1)})`)
      .join(" ");
    debugInfo.value = `Zone: ${scores}`;
  }

  return { raw: zone.raw, normalized: zone.normalized, zone };
}

// ============================================================
// FIND EXPRESSION — v1 compat (scanner heuristique)
// ============================================================

export function findExpression(
  text: string,
  delimiters: Delimiter[],
  debugInfo?: { value: string },
): { raw: string; choice: DocChoice } | null {
  const scan = scanMathExpr(text, delimiters);
  if (!scan) return null;
  const { raw, normalized } = scan;

  if (!/[\/\^*]/.test(normalized) && !/\d\s+\d/.test(normalized) &&
      !/[a-zA-Z]\(/.test(normalized) && !/[a-zA-Z]\s+\d/.test(normalized)) {
    return null;
  }

  try {
    const ooxml = buildMathOoxml(normalized);
    return {
      raw,
      choice: { label: "expression", display: normalized, replacement: normalized, ooxml },
    };
  } catch (e) {
    if (debugInfo) debugInfo.value = `Parse err: ${(e as Error).message}`;
    return null;
  }
}
