import * as vscode from 'vscode';
// Port JS STRICT de SpanComputer (réutilisé tel quel — parité verrouillée par
// spancomputer.test.js côté web-demo). esbuild l'inline dans le bundle.
import SpanComputer from '../../../web-demo/MathCursor.Demo.WebAssembly/wwwroot/spancomputer.js';

export interface Source { src: string; range: vscode.Range; }

// Masque les zones math DÉJÀ converties ($…$, $$…$$, \(…\), \[…\]) par des
// espaces (longueur préservée → offsets intacts) pour que la détection ne les
// re-propose pas (équivalent du masquage des OMaths côté Word).
const MATH_REGIONS = [
  /(?<!\\)\$\$[\s\S]*?(?<!\\)\$\$/g,            // display $$…$$
  /(?<!\\)\$(?:\\\$|[^$\n])*?(?<!\\)\$/g,       // inline $…$ ($ échappés ignorés)
  /\\\([\s\S]*?\\\)/g,                          // \(…\)
  /\\\[[\s\S]*?\\\]/g,                          // \[…\]
];
export function maskMath(text: string): string {
  let s = text;
  for (const re of MATH_REGIONS) { s = s.replace(re, m => ' '.repeat(m.length)); }
  return s;
}

// Texte MASQUÉ de la ligne, calculé sur le DOCUMENT entier (pour couvrir les
// blocs math multi-lignes $$…$$ / \[…\] dont l'ouverture est sur une autre ligne).
// Masquage longueur-préservé → offsets de la ligne intacts.
export function lineMasked(document: vscode.TextDocument, lineNumber: number): string {
  const full = maskMath(document.getText());
  const line = document.lineAt(lineNumber);
  const start = document.offsetAt(line.range.start);
  return full.substr(start, line.text.length);
}

// Délimiteurs réduits (sans = < >) : pas de NER en VSCode → on capte l'équation
// entière (ex. f(x)=1/x d'un coup), comme la démo « mode réel ».
// Cf. ADR 2026-06-19-Feat-web-demo-real-mode-editor + 2026-06-23-Feat-vscode-host.

/** Zone math heuristique autour du caret, sur la ligne courante. */
export function detectZone(
  document: vscode.TextDocument,
  position: vscode.Position
): Source | undefined {
  // Détection sur le texte MASQUÉ ($…$ déjà convertis → espaces, blocs multi-lignes
  // inclus) ; le src final est extrait du texte ORIGINAL aux mêmes offsets.
  const zone = SpanComputer.computeZone(
    lineMasked(document, position.line), position.character, null, SpanComputer.DEMO_DELIMITERS
  );
  if (!zone || zone.end <= zone.start) { return undefined; }
  const range = new vscode.Range(position.line, zone.start, position.line, zone.end);
  const src = document.getText(range).trim();
  if (!src || containsMath(src)) { return undefined; } // ne pas reconvertir du LaTeX
  return { src, range };
}

/** La zone contient-elle déjà du LaTeX math ($…$, \(…\), \[…\]) ? */
export function containsMath(src: string): boolean {
  return /\$|\\\(|\\\)|\\\[|\\\]/.test(src);
}

/** Source à convertir : la sélection (si autorisée et non vide), sinon la zone détectée. */
export function resolveSource(
  document: vscode.TextDocument,
  position: vscode.Position,
  allowSelection: boolean
): Source | undefined {
  if (allowSelection) {
    const editor = vscode.window.activeTextEditor;
    const sel = editor?.selection;
    if (sel && !sel.isEmpty && editor?.document === document) {
      const range = new vscode.Range(sel.start, sel.end);
      return { src: document.getText(range).trim(), range };
    }
  }
  return detectZone(document, position);
}
