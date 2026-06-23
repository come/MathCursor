import * as vscode from 'vscode';
import { analyze } from './engine';
import { renderSvgDataUri } from './render';

// Host VSCode (= L6). Branche le moteur pur sur la complétion native : Ctrl+Espace
// dans un .tex/.md → candidats LaTeX classés, aperçu de la formule au caret,
// insertion inline. Cf. ADR 2026-06-23-Feat-vscode-host.

const LANGS = ['latex', 'tex', 'markdown'];

export function activate(context: vscode.ExtensionContext): void {
  const provider = vscode.languages.registerCompletionItemProvider(
    LANGS.map(language => ({ language })),
    new MathCursorCompletionProvider()
  );
  context.subscriptions.push(provider);

  context.subscriptions.push(
    vscode.commands.registerCommand('mathcursor.convert', () =>
      vscode.commands.executeCommand('editor.action.triggerSuggest')
    )
  );
}

class MathCursorCompletionProvider implements vscode.CompletionItemProvider {
  async provideCompletionItems(
    document: vscode.TextDocument,
    position: vscode.Position,
    token: vscode.CancellationToken,
    ctx: vscode.CompletionContext
  ): Promise<vscode.CompletionItem[] | undefined> {
    // Déclenchement MANUEL seulement (Ctrl+Espace) — jamais au fil de la frappe.
    if (ctx.triggerKind !== vscode.CompletionTriggerKind.Invoke) {
      return undefined;
    }

    const cfg = vscode.workspace.getConfiguration('mathcursor');
    const culture = cfg.get<string>('culture', 'fr');
    const max = cfg.get<number>('maxCandidates', 3);
    const delimiters = cfg.get<string>('delimiters', 'dollar');

    const { src, range, rangeText } = extractSource(document, position);
    if (!src) {
      return undefined;
    }

    let result;
    try {
      result = await analyze(src, culture);
    } catch {
      return undefined;
    }
    if (token.isCancellationRequested || !result.ranked?.length) {
      return undefined;
    }

    const items: vscode.CompletionItem[] = [];
    const n = Math.min(max, result.ranked.length);
    for (let i = 0; i < n; i++) {
      const latex = result.ranked[i].latex;
      const inserted = wrap(latex, delimiters);

      const item = new vscode.CompletionItem(
        { label: latex, description: i === 0 ? 'MathCursor' : `MathCursor #${i + 1}` },
        vscode.CompletionItemKind.Snippet
      );
      item.insertText = inserted;
      item.range = range;            // remplace la source par le LaTeX choisi
      item.filterText = rangeText;   // == texte de range → l'item reste toujours affiché
      item.sortText = String(i).padStart(3, '0');
      item.preselect = i === 0;
      item.detail = inserted;
      item.documentation = buildDocs(latex, inserted);
      items.push(item);
    }
    return items;
  }
}

/** Aperçu : image SVG de la formule (si rendu OK) + le LaTeX inséré. */
function buildDocs(latex: string, inserted: string): vscode.MarkdownString {
  const md = new vscode.MarkdownString();
  const dataUri = renderSvgDataUri(latex);
  if (dataUri) {
    md.appendMarkdown(`![aperçu](${dataUri})\n\n`);
  }
  md.appendCodeblock(inserted, 'latex');
  return md;
}

/** Source à convertir : la sélection si non vide, sinon la ligne jusqu'au caret. */
function extractSource(
  document: vscode.TextDocument,
  position: vscode.Position
): { src: string; range: vscode.Range; rangeText: string } {
  const editor = vscode.window.activeTextEditor;
  const sel = editor?.selection;
  if (sel && !sel.isEmpty && editor?.document === document) {
    const range = new vscode.Range(sel.start, sel.end);
    const rangeText = document.getText(range);
    return { src: rangeText.trim(), range, rangeText };
  }
  const line = document.lineAt(position.line);
  const startCh = line.firstNonWhitespaceCharacterIndex;
  const range = new vscode.Range(position.line, startCh, position.line, position.character);
  const rangeText = document.getText(range);
  return { src: rangeText.trim(), range, rangeText };
}

function wrap(latex: string, delimiters: string): string {
  switch (delimiters) {
    case 'paren': return `\\(${latex}\\)`;
    case 'none': return latex;
    default: return `$${latex}$`;
  }
}

export function deactivate(): void { /* no-op */ }
