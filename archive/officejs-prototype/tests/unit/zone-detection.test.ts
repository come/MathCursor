import { describe, it, expect } from "vitest";
import { tokenize } from "../../src/taskpane/conversion/tokenizer";
import { scoreAllTokens } from "../../src/taskpane/conversion/scorer";
import { detectMathZone } from "../../src/taskpane/conversion/zone-detector";
import corpus from "./fixtures/zone-detection-corpus.json";

function detectZone(input: string): string | null {
  const tokens = tokenize(input);
  scoreAllTokens(tokens);
  const zone = detectMathZone(tokens);
  return zone?.normalized?.trim() ?? null;
}

describe("Zone detection", () => {
  for (const c of corpus.cases) {
    it(`[${c.id}] ${c.description}: "${c.input}"`, () => {
      const result = detectZone(c.input);
      if (c.expectedZone === null) {
        expect(result).toBeNull();
      } else {
        expect(result).not.toBeNull();
        // Normaliser les espaces pour la comparaison
        const expected = c.expectedZone.replace(/\s+/g, " ").trim();
        const actual = result!.replace(/\s+/g, " ").trim();
        expect(actual).toBe(expected);
      }
    });
  }
});

describe("Tokenizer", () => {
  it("tokenize simple expression", () => {
    const tokens = tokenize("f(x) = 2x + 1");
    const texts = tokens.filter(t => !t.categories.has("whitespace")).map(t => t.normalized);
    expect(texts).toEqual(["f", "(", "x", ")", "=", "2", "x", "+", "1"]);
  });

  it("preserves positions", () => {
    const tokens = tokenize("ab + 3");
    expect(tokens[0].start).toBe(0);
    expect(tokens[0].end).toBe(2);
    expect(tokens[0].text).toBe("ab");
  });

  it("groups multi-char operators", () => {
    const tokens = tokenize("x >= 1 <=> y <= 0");
    const ops = tokens.filter(t => t.categories.has("operator")).map(t => t.text);
    expect(ops).toContain(">=");
    expect(ops).toContain("<=>");
  });

  it("normalizes math italic", () => {
    const tokens = tokenize("𝑓(𝑥)");
    const norms = tokens.filter(t => !t.categories.has("whitespace")).map(t => t.normalized);
    expect(norms).toEqual(["f", "(", "x", ")"]);
  });
});

describe("Scorer", () => {
  it("stopwords score 0", () => {
    const tokens = tokenize("on a");
    scoreAllTokens(tokens);
    const on = tokens.find(t => t.normalized === "on");
    expect(on?.mathiness).toBe(0.0);
  });

  it("math functions score high", () => {
    const tokens = tokenize("sin(x)");
    scoreAllTokens(tokens);
    const sin = tokens.find(t => t.normalized === "sin");
    expect(sin!.mathiness).toBeGreaterThanOrEqual(0.9);
  });

  it("operators score high", () => {
    const tokens = tokenize("a + b");
    scoreAllTokens(tokens);
    const plus = tokens.find(t => t.text === "+");
    expect(plus!.mathiness).toBeGreaterThanOrEqual(0.8);
  });

  it("long prose words score low", () => {
    const tokens = tokenize("Bonjour");
    scoreAllTokens(tokens);
    expect(tokens[0].mathiness).toBeLessThan(0.3);
  });
});
