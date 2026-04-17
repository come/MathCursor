import { describe, it, expect } from "vitest";
import { estimateCursorInSource } from "../../src/taskpane/decomposition/findOMath";

describe("estimateCursorInSource", () => {
  it("clampe à 0 si omath length est 0", () => {
    expect(estimateCursorInSource(5, 0, 8)).toBe(0);
  });

  it("renvoie 0 si curseur en début d'OMath", () => {
    expect(estimateCursorInSource(0, 10, 8)).toBe(0);
  });

  it("renvoie sourceLength si curseur en fin d'OMath", () => {
    expect(estimateCursorInSource(10, 10, 8)).toBe(8);
  });

  it("proportionnalité : curseur au milieu → milieu source", () => {
    expect(estimateCursorInSource(5, 10, 8)).toBe(4);
  });

  it("clampe au sourceLength", () => {
    expect(estimateCursorInSource(15, 10, 8)).toBe(8);
  });

  it("sourceText plus court que OMath", () => {
    // f(x)=1/x (8 chars OMath affiché) → source 8 chars mais ratio
    expect(estimateCursorInSource(4, 8, 8)).toBe(4);
  });

  it("sourceText plus long que OMath (fraction écrite compacte mais décomposée longue)", () => {
    // OMath "1/2" = 3 chars mais sourceText "(1)/(2)" = 7 chars
    expect(estimateCursorInSource(2, 3, 7)).toBe(5);
  });
});
