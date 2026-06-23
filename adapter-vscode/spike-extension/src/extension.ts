import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';

// SPIKE JETABLE — répond à UNE question : VSCode rend-il une image (SVG MathJax,
// data-URI) dans la doc d'un CompletionItem du widget de suggestion natif ?
// 3 variantes pour voir laquelle marche. Tape "mc" + Ctrl+Espace, puis regarde
// le panneau de détails à droite de la liste (chevron / Ctrl+Espace à nouveau).

interface Pre { latex: string; svg: string; }

export function activate(context: vscode.ExtensionContext) {
  const data: Pre[] = JSON.parse(
    fs.readFileSync(path.join(context.extensionPath, 'spike-prerender.json'), 'utf8')
  );

  const provider = vscode.languages.registerCompletionItemProvider(
    ['latex', 'tex', 'markdown', 'plaintext'],
    {
      provideCompletionItems() {
        const items: vscode.CompletionItem[] = [];

        // Variante A — image markdown ![]()
        const a = new vscode.CompletionItem('mc A — image markdown', vscode.CompletionItemKind.Snippet);
        const mdA = new vscode.MarkdownString(`**A — markdown image**\n\n![preview](${data[0].svg})\n\n\`${data[0].latex}\``);
        mdA.isTrusted = true; mdA.supportHtml = true;
        a.documentation = mdA;
        a.insertText = data[0].latex;
        a.filterText = 'mc'; a.sortText = '0';
        items.push(a);

        // Variante B — balise HTML <img>
        const b = new vscode.CompletionItem('mc B — image html', vscode.CompletionItemKind.Snippet);
        const mdB = new vscode.MarkdownString(`**B — html img**<br><img src="${data[1].svg}" /><br>\`${data[1].latex}\``);
        mdB.isTrusted = true; mdB.supportHtml = true;
        b.documentation = mdB;
        b.insertText = data[1].latex;
        b.filterText = 'mc'; b.sortText = '1';
        items.push(b);

        // Contrôle — pas d'image
        const c = new vscode.CompletionItem('mc C — contrôle texte', vscode.CompletionItemKind.Snippet);
        c.documentation = new vscode.MarkdownString(`**C — contrôle**\n\nPas d'image, juste le LaTeX : \`${data[0].latex}\``);
        c.insertText = data[0].latex;
        c.filterText = 'mc'; c.sortText = '2';
        items.push(c);

        return items;
      }
    }
  );
  context.subscriptions.push(provider);

  context.subscriptions.push(
    vscode.commands.registerCommand('mathcursor.spikeHello', () => {
      vscode.window.showInformationMessage(
        'MathCursor spike actif. Dans un fichier .tex/.md, tape "mc" puis Ctrl+Espace, et regarde le panneau de détails.'
      );
    })
  );

  vscode.window.showInformationMessage('MathCursor spike chargé — tape "mc" + Ctrl+Espace dans un .tex/.md.');
}

export function deactivate() {}
