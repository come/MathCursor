// Word API helpers — lecture, remplacement, insertion OMath

import type { DocChoice } from "./conversion/types";

// Échapper ^ en ^^ pour Word search
export function wordEscape(s: string): string {
  return s.replace(/\^/g, "^^");
}

// Lire le paragraphe courant
export async function readPara(ctx: Word.RequestContext) {
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

// Remplacement atomique : 1 seul appel → 1 seul undo
export async function doReplace(
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
  const fullRange = found.expandTo(sel.getRange("End")); // inclut le \t

  if (chosen.ooxml) {
    // ATOMIQUE : un seul insertOoxml(replace) sur le range qui inclut le tab.
    // Le package n'a plus de <w:r> trailing pour éviter le split de paragraphe.
    // Le curseur est placé APRÈS l'OMath (hors du contexte italic) via "After".
    const inserted = fullRange.insertOoxml(chosen.ooxml, Word.InsertLocation.replace);
    inserted.getRange("After").select("Start");
    await ctx.sync();
  } else {
    fullRange.insertText(chosen.replacement + " ", Word.InsertLocation.replace)
      .getRange("End").select("End");
    await ctx.sync();
  }

  return true;
}
