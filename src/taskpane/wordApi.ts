// Word API helpers — lecture, remplacement, insertion OMath

import type { DocChoice } from "./conversion/types";
import { storeSource } from "./storage";

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
    // ATOMIQUE : un seul insertOoxml(replace) au lieu de delete + insert
    fullRange.insertOoxml(chosen.ooxml, Word.InsertLocation.replace);
    await ctx.sync();

    // Stocker le texte source (hors undo stack — document.settings)
    para.load("uniqueLocalId");
    await ctx.sync();
    storeSource(`math_${para.uniqueLocalId}`, searchStr);

    // Curseur après l'OMath
    const fp = ctx.document.getSelection().paragraphs;
    fp.load("items");
    await ctx.sync();
    if (fp.items.length > 0) {
      fp.items[0].insertText(" ", Word.InsertLocation.end).select("End");
    }
    await ctx.sync();
  } else {
    fullRange.insertText(chosen.replacement + " ", Word.InsertLocation.replace)
      .getRange("End").select("End");
    await ctx.sync();
  }

  return true;
}
