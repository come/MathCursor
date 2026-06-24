import * as vscode from 'vscode';

// Assure que le préambule LaTeX importe les packages dont la sortie MathCursor a
// besoin (amsmath : matrices/cases/\boxed/\text… ; amssymb : \mathbb…). Ajout
// idempotent juste après \documentclass. Fichiers .tex uniquement (Markdown :
// KaTeX/MathJax connaissent ces commandes nativement).

const LATEX_LANGS = ['latex', 'tex'];
const REQUIRED = ['amsmath', 'amssymb'];

export async function ensurePackages(editor: vscode.TextEditor): Promise<void> {
  const doc = editor.document;
  if (!LATEX_LANGS.includes(doc.languageId)) { return; }

  const text = doc.getText();
  const missing = REQUIRED.filter(pkg =>
    !new RegExp(`\\\\usepackage(\\[[^\\]]*\\])?\\{[^}]*\\b${pkg}\\b[^}]*\\}`).test(text));
  if (missing.length === 0) { return; }

  // Insère après la ligne \documentclass… ; sans préambule identifiable, on s'abstient.
  const m = /\\documentclass[^\n]*\n/.exec(text);
  if (!m) { return; }

  const pos = doc.positionAt(m.index + m[0].length);
  const lines = missing.map(p => `\\usepackage{${p}}`).join('\n') + '\n';
  await editor.edit(b => b.insert(pos, lines));
}
