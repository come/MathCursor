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

// Insérer un OMath + stocker le texte source (clé = paragraph ID)
export async function insertOMathWithTag(
  ctx: Word.RequestContext,
  range: Word.Range,
  ooxml: string,
  sourceText: string,
  location: Word.InsertLocation,
): Promise<void> {
  range.insertOoxml(ooxml, location);
  await ctx.sync();
  // Stocker le texte source
  const paras = ctx.document.getSelection().paragraphs;
  paras.load("items");
  await ctx.sync();
  if (paras.items.length > 0) {
    const para = paras.items[0];
    para.load("uniqueLocalId");
    await ctx.sync();
    storeSource(`math_${para.uniqueLocalId}`, sourceText);
  }
  // Curseur après
  const fp = ctx.document.getSelection().paragraphs;
  fp.load("items");
  await ctx.sync();
  if (fp.items.length > 0) {
    fp.items[0].insertText(" ", Word.InsertLocation.end).select("End");
  }
  await ctx.sync();
}

// Remplacement : chercher le texte + expandTo curseur + insérer OMath ou texte
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
  const fullRange = found.expandTo(sel.getRange("End"));

  if (chosen.ooxml) {
    fullRange.delete();
    await ctx.sync();
    await insertOMathWithTag(ctx, ctx.document.getSelection(), chosen.ooxml, searchStr, Word.InsertLocation.replace);
  } else {
    fullRange.insertText(chosen.replacement + " ", Word.InsertLocation.replace)
      .getRange("End").select("End");
  }
  await ctx.sync();
  return true;
}
