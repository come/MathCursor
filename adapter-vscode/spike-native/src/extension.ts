import * as vscode from 'vscode';
import { spawn } from 'child_process';
import * as path from 'path';

// SPIKE — déclenche le helper WPF natif (popup au caret) et insère le choix.
// Candidats codés en dur ; le but est de valider caret MSAA + focus + round-trip.

const EXE = path.join(
  '..', 'caret-popup', 'bin', 'Release', 'net48', 'MathCursor.CaretPopup.exe'
);

export function activate(ctx: vscode.ExtensionContext): void {
  ctx.subscriptions.push(
    vscode.commands.registerCommand('mcNative.show', () => {
      const editor = vscode.window.activeTextEditor;
      if (!editor) { return; }

      const exe = path.join(ctx.extensionPath, EXE);
      const cands = ['x^{2}+\\sqrt{2}', '\\frac{1}{2}+\\frac{3}{4}', '\\int_0^1 x^2\\,dx'];

      let out = '';
      let err = '';
      const child = spawn(exe, [], { windowsHide: false });
      child.stdout.on('data', d => (out += d.toString()));
      child.stderr.on('data', d => (err += d.toString()));
      child.on('error', e => vscode.window.showErrorMessage('MC spawn fail: ' + e.message));
      child.on('close', code => {
        console.log('[mcNative] exit', code, '| stderr:', err.trim());
        if (code === 0 && out) {
          editor.edit(b => b.insert(editor.selection.active, '$' + out + '$'));
        } else {
          vscode.window.showInformationMessage(
            `MC popup: code=${code}. ${err.trim()}`
          );
        }
      });
      child.stdin.write(cands.join('\n') + '\n');
      child.stdin.end();
    })
  );
}

export function deactivate(): void { /* no-op */ }
