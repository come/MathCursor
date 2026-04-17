import { describe, it, expect } from "vitest";
import { UndoGuard } from "../../src/taskpane/conversion/undo-guard";

describe("UndoGuard (persistent, text-based)", () => {
  it("laisse passer un texte jamais vu", () => {
    const g = new UndoGuard();
    expect(g.shouldSkipAndUpdate("f(x)=1/x")).toBe(false);
  });

  it("bloque un texte marqué (scenario Ctrl+Z répété)", () => {
    const g = new UndoGuard();
    g.mark("f(x)=1/x");
    expect(g.shouldSkipAndUpdate("f(x)=1/x")).toBe(true);
    // Re-appeler : reste bloqué tant que le texte est le même
    expect(g.shouldSkipAndUpdate("f(x)=1/x")).toBe(true);
    expect(g.shouldSkipAndUpdate("f(x)=1/x")).toBe(true);
  });

  it("libère le guard dès que le texte change (édition utilisateur)", () => {
    const g = new UndoGuard();
    g.mark("f(x)=1/x");
    // Utilisateur édite → nouveau texte
    expect(g.shouldSkipAndUpdate("f(x)=2/x")).toBe(false);
    // Guard maintenant libéré pour le texte marqué d'origine
    expect(g.shouldSkipAndUpdate("f(x)=1/x")).toBe(false);
  });

  it("mark peut être ré-appelé pour une nouvelle conversion", () => {
    const g = new UndoGuard();
    g.mark("f(x)=1/x");
    g.mark("g(x)=2x");
    expect(g.shouldSkipAndUpdate("f(x)=1/x")).toBe(false); // ancien, non gardé
    g.mark("g(x)=2x");
    expect(g.shouldSkipAndUpdate("g(x)=2x")).toBe(true); // nouveau, gardé
  });

  it("scenario US2 complet : convert → Ctrl+Z → undo reste bloqué → édition libère", () => {
    const g = new UndoGuard();
    // 1. Convert f(x)=1/x
    g.mark("f(x)=1/x");
    // 2. Ctrl+Z : Word restaure le tab → fastTick voit "f(x)=1/x\t"
    // fastTick appelle shouldSkipAndUpdate avec textBeforeTab = "f(x)=1/x"
    expect(g.shouldSkipAndUpdate("f(x)=1/x")).toBe(true); // bloqué
    // 3. Utilisateur attend, fait rien → fastTick continue à voir la même chose
    expect(g.shouldSkipAndUpdate("f(x)=1/x")).toBe(true); // toujours bloqué, pas de loop
    // 4. Utilisateur édite : tape "y" → "f(x)=1/xy\t"
    expect(g.shouldSkipAndUpdate("f(x)=1/xy")).toBe(false); // libéré
    // 5. Conversion relancée, re-marquer
    g.mark("f(x)=1/xy");
    expect(g.shouldSkipAndUpdate("f(x)=1/xy")).toBe(true);
  });

  it("clear explicite", () => {
    const g = new UndoGuard();
    g.mark("f(x)=1/x");
    g.clear();
    expect(g.shouldSkipAndUpdate("f(x)=1/x")).toBe(false);
  });
});
