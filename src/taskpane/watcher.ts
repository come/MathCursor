// ============================================================
// watcher.ts — Orchestrateur
// Fast tick (50ms) : décomposition OMath + conversion Tab
// Slow tick (500ms) : suggestions dans la task pane
// ============================================================

import { ref, computed } from "vue";
import type { DocChoice } from "./conversion/types";
import { findExpression, findExpressionV2, buildMathOoxml, type Delimiter } from "./conversion/index";
import { findSymbol } from "./symbols/patterns";
import { hasOMathChars, normalizeOMath, fixOMathParens } from "./decomposition/normalize";
import { getSource } from "./storage";
import { readPara, doReplace, wordEscape } from "./wordApi";

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
// Anti-boucle undo : textes récemment convertis
// Vidé dès que le texte change sans tab (= l'utilisateur tape du neuf)
const convertedTexts = new Set<string>();

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
            // Atomique : clear + insert en un seul replace sur le range entier
            const wholeRange = data.para.getRange("Whole");
            wholeRange.insertText(sourceText + "\n", Word.InsertLocation.replace);
            await ctx.sync();

            // Repositionner le curseur
            // Recharger le paragraphe (le range a changé après replace)
            const freshParas = ctx.document.getSelection().paragraphs;
            freshParas.load("items");
            await ctx.sync();
            const freshPara = freshParas.items[0];

            if (freshPara) {
              const clampedOffset = Math.min(cursorOffset, sourceText.length);
              if (clampedOffset > 0 && clampedOffset < sourceText.length) {
                const prefix = sourceText.substring(0, clampedOffset);
                const results = freshPara.search(wordEscape(prefix), { matchCase: true, matchWholeWord: false });
                results.load("items");
                await ctx.sync();
                if (results.items.length > 0) {
                  results.items[0].getRange("End").select("Start");
                  await ctx.sync();
                }
              } else {
                freshPara.getRange("End").select("Start");
                await ctx.sync();
              }
            }

            // Protéger contre re-conversion si undo restaure l'OMath puis re-décompose
            convertedTexts.add(sourceText);
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

      // Anti-boucle undo : si on a déjà converti ce texte, skip
      if (convertedTexts.has(textBeforeTab)) {
        debugInfo.value = `undo guard: "${textBeforeTab.slice(-30)}"`;
        return;
      }

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
        hasDecomposed = true;
        convertedTexts.add(textBeforeTab);
        lastAction.value = `${symMatch.raw} \u2192 ${chosen.display}`;
        replaceCount.value++;
        lastSlowText = "";
        suggestions.value = []; selectedIdx.value = 0;
        isReplacing = false;
        return;
      }

      // 2) Expression math ? (zone detector v2)
      const zoneMatch = findExpressionV2(cleanText, debugInfo);
      if (zoneMatch) {
        try {
          const steps: string[] = [];
          const ooxml = buildMathOoxml(zoneMatch.normalized, steps);
          debugSteps.value = steps;
          const choice: DocChoice = {
            label: "expression",
            display: zoneMatch.normalized,
            replacement: zoneMatch.normalized,
            ooxml,
          };

          if (await doReplace(ctx, freshPara, freshSel, zoneMatch.raw, choice)) {
            hasDecomposed = true;
            convertedTexts.add(textBeforeTab);
            lastAction.value = `${zoneMatch.normalized} \u2192 expr`;
            replaceCount.value++;
          }
        } catch (e) {
          debugInfo.value = `Parse err: ${(e as Error).message}`;
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

      const zone = findExpressionV2(trimmed, debugInfo);
      if (zone) {
        try {
          const ooxml = buildMathOoxml(zone.normalized);
          suggestions.value = [{
            label: "expression",
            display: zone.normalized,
            replacement: zone.normalized,
            ooxml,
          }];
          matchedRaw.value = zone.raw;
        } catch {
          // parse error silencieux pour les suggestions
        }
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
