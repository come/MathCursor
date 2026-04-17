// Conversion : texte → OMath
// Façade qui combine scanner + tokenizer + parser + render

import type { DocChoice } from "./types";
import type { Delimiter } from "./scanner";
import { scanMathExpr } from "./scanner";
import { tokenize } from "./tokenizer";
import { parse, astToString } from "./parser";
import { render } from "./render";
import { omathPkg } from "../omath/helpers";

export type { DocChoice, Delimiter };
export { scanMathExpr };

export function buildMathOoxml(raw: string, debugSteps?: string[]): string {
  debugSteps?.push(`1. Input: "${raw}"`);

  const tks = tokenize(raw);
  debugSteps?.push(`2. Tokens: ${tks.map(t => t.v).join(" ")}`);

  const ast = parse(tks);
  debugSteps?.push(`3. AST:\n${astToString(ast)}`);

  const xml = render(ast);
  debugSteps?.push(`4. XML (${xml.length} chars): ${xml.substring(0, 200)}...`);

  const ooxml = omathPkg(xml);
  debugSteps?.push(`5. OOXML total: ${ooxml.length} chars`);

  return ooxml;
}

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
