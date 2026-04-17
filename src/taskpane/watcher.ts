// ============================================================
// watcher.ts — Orchestrateur
// Fast tick (50ms) : décomposition OMath + conversion Tab
// Slow tick (500ms) : suggestions dans la task pane
// ============================================================

import { ref, computed } from "vue";
import type { DocChoice } from "./conversion/types";
import { findExpression, type Delimiter } from "./conversion/index";
import { findSymbol } from "./symbols/patterns";
import { hasOMathChars, normalizeOMath, fixOMathParens } from "./decomposition/normalize";
import { getSource } from "./storage";
import { readPara, doReplace, insertOMathWithTag, wordEscape } from "./wordApi";

// --- État réactif ---
export const isActive = ref(true);
export const lastAction = ref("");
export const replaceCount = ref(0);
export const debugInfo = ref("Démarrage...");
export const debugSteps = ref<string[]>([]);

export const suggestions = ref<DocChoice[]>([]);
export const selectedIdx = ref(0);
export const matchedRaw = ref("");
export const hasSuggestions = computed(() => suggestions.value.length > 0);

export function selectSuggestion(i: number) { selectedIdx.value = i; }

// Re-export pour App.vue
export type { DocChoice };

// --- Settings ---
export const mathDelimiters = ref<Delimiter[]>([
  { delim: "`", replace: "" },
  { delim: "$", replace: "" },
  { delim: "  ", replace: " " },
]);

// --- State ---
let fastId: ReturnType<typeof setInterval> | null = null;
let slowId: ReturnType<typeof setInterval> | null = null;
let isBusyFast = false;
let isBusySlow = false;
let isReplacing = false;
let lastSlowText = "";
let hasDecomposed = false;

// ============================================================
// FAST TICK (50ms)
// 1. Détection OMath → décomposition
// 2. Détection Tab → conversion
// ============================================================

async function fastTick(): Promise<void> {
  if (isBusyFast || isReplacing || !isActive.value) return;
  if (typeof Word === "undefined" || !Word.run) return;

  isBusyFast = true;
  try {
    await Word.run(async (ctx) => {
      const data = await readPara(ctx);
      if (!data?.text) return;

      // === DÉCOMPOSITION : curseur dans un OMath ? ===
      if (hasOMathChars(data.text)) {
        data.sel.font.load("name");
        await ctx.sync();
        const fontName = data.sel.font.name ?? "";

        if (!fontName.includes("Cambria Math")) {
          hasDecomposed = false;
        } else if (!hasDecomposed) {
          hasDecomposed = true;
          isReplacing = true;

          // Mesurer la position du curseur avant décomposition
          const paraStart = data.para.getRange("Start");
          const cursorPos = data.sel.getRange("Start");
          const beforeCursor = paraStart.expandTo(cursorPos);
          beforeCursor.load("text");
          data.para.load("uniqueLocalId");
          await ctx.sync();

          const textBeforeCursor = beforeCursor.text ?? "";
          const normalizedBefore = fixOMathParens(textBeforeCursor, normalizeOMath(textBeforeCursor));
          const cursorOffset = normalizedBefore.length;

          // Lire le texte source stocké
          const sourceText = getSource(`math_${data.para.uniqueLocalId}`);
          debugInfo.value = `\u21A9 source: "${sourceText ?? "?"}" | offset: ${cursorOffset}`;

          if (sourceText) {
            data.para.clear();
            await ctx.sync();
            data.para.insertText(sourceText, Word.InsertLocation.start);
            await ctx.sync();

            // Repositionner le curseur
            const clampedOffset = Math.min(cursorOffset, sourceText.length);
            if (clampedOffset > 0 && clampedOffset < sourceText.length) {
              const prefix = sourceText.substring(0, clampedOffset);
              const results = data.para.search(wordEscape(prefix), { matchCase: true, matchWholeWord: false });
              results.load("items");
              await ctx.sync();
              if (results.items.length > 0) {
                results.items[0].getRange("End").select("Start");
                await ctx.sync();
              }
            } else {
              ctx.document.getSelection().getRange("End").select("End");
              await ctx.sync();
            }

            lastSlowText = "";
            lastAction.value = `\u21A9 ${sourceText}`;
          }
          isReplacing = false;
          return;
        }
      } else {
        hasDecomposed = false;
      }

      // === CONVERSION : Tab détecté ? ===
      if (!/\t/.test(data.text)) return;

      const textBeforeTab = data.text.replace(/\t[\s\S]*$/, "");

      // Heuristique rapide (JS pur)
      const hasSugg = suggestions.value.length > 0;
      const hasDelim = mathDelimiters.value.some(d => textBeforeTab.includes(d.delim));
      const hasMathOps = /[\/\^*]/.test(textBeforeTab);
      const hasFuncCall = /[a-zA-Z]\(/.test(textBeforeTab);
      const hasExponent = /[a-zA-Z\d]\s+\d/.test(textBeforeTab);
      const hasSymbolMatch = findSymbol(textBeforeTab) !== null;

      if (!hasSugg && !hasDelim && !hasMathOps && !hasFuncCall && !hasExponent && !hasSymbolMatch) {
        return;
      }

      isReplacing = true;
      debugInfo.value = `TAB: "${textBeforeTab.slice(-40)}"`;

      // Supprimer le tab
      const tabSearch = data.para.search("^t", { matchCase: false, matchWholeWord: false });
      tabSearch.load("items");
      await ctx.sync();
      if (tabSearch.items.length > 0) {
        tabSearch.items[tabSearch.items.length - 1].delete();
        await ctx.sync();
      }

      // Relire le texte propre
      data.para.load("text");
      const freshSel = ctx.document.getSelection();
      const freshParas = freshSel.paragraphs;
      freshParas.load("items");
      await ctx.sync();
      const cleanText = data.para.text ?? "";
      const freshPara = freshParas.items[0] ?? data.para;

      // 1) Symbole ?
      const symMatch = findSymbol(cleanText);
      if (symMatch) {
        const chosen = symMatch.choices[selectedIdx.value] ?? symMatch.choices[0];
        await doReplace(ctx, freshPara, freshSel, symMatch.raw, chosen);
        lastAction.value = `${symMatch.raw} \u2192 ${chosen.display}`;
        replaceCount.value++;
        lastSlowText = "";
        suggestions.value = []; selectedIdx.value = 0;
        isReplacing = false;
        return;
      }

      // 2) Expression math ?
      const exprMatch = findExpression(cleanText, mathDelimiters.value, debugInfo);
      if (exprMatch) {
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
          if (await doReplace(ctx, freshPara, freshSel, exprMatch.raw, exprMatch.choice)) {
            lastAction.value = `${exprMatch.raw} \u2192 expr`;
            replaceCount.value++;
          }
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

// ============================================================
// SLOW TICK (500ms) — suggestions dans la task pane
// ============================================================

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

      const sym = findSymbol(trimmed);
      if (sym) {
        suggestions.value = sym.choices;
        matchedRaw.value = sym.raw;
        if (selectedIdx.value >= sym.choices.length) selectedIdx.value = 0;
        return;
      }

      const expr = findExpression(trimmed, mathDelimiters.value);
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

// ============================================================
// START / STOP
// ============================================================

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
