// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme de Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
