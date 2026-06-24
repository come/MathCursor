import * as vscode from 'vscode';
import { spawn, ChildProcess } from 'child_process';
import * as path from 'path';

// Pilote le helper natif PERSISTANT (popup WPF au caret, façon Word). Un seul
// process reste en vie ; on lui envoie SHOW/HIDE et il renvoie COMMIT/DISMISS.
// Protocole : lignes, champs séparés par TAB (le LaTeX ne contient ni tab ni \n).
// Windows-only ; ailleurs isAvailable=false (l'appelant retombe sur la complétion).

export function isHelperAvailable(): boolean {
  return process.platform === 'win32';
}

export class PopupController {
  private proc: ChildProcess | undefined;
  private buf = '';
  private shown = false;

  constructor(
    private readonly ctx: vscode.ExtensionContext,
    private readonly onCommit: (index: number) => void,
    private readonly onDismiss: () => void
  ) {}

  get isShown(): boolean { return this.shown; }

  // reposition=false → garde la position courante (zone inchangée → pas de
  // dérive en frappe). colDelta/charWidth ne servent qu'au (re)positionnement.
  show(candidates: string[], theme: string, reposition: boolean, colDelta: number, charWidth: number): void {
    const p = this.ensureProc();
    if (!p || candidates.length === 0) { return; }
    // Garde-fou : pas de TAB/retour ligne dans le LaTeX (sinon casse le cadrage).
    const safe = candidates.map(c => c.replace(/[\t\r\n]/g, ' '));
    p.stdin!.write(
      `SHOW\t${theme}\t${reposition ? 1 : 0}\t${colDelta}\t${charWidth.toFixed(2)}\t${safe.join('\t')}\n`
    );
    this.shown = true;
  }

  hide(): void {
    if (this.proc && this.shown) { this.proc.stdin!.write('HIDE\n'); }
    this.shown = false;
  }

  dispose(): void {
    if (this.proc) {
      try { this.proc.stdin!.write('QUIT\n'); } catch { /* ignore */ }
      this.proc.kill();
      this.proc = undefined;
    }
    this.shown = false;
  }

  private ensureProc(): ChildProcess | undefined {
    if (!isHelperAvailable()) { return undefined; }
    if (this.proc && !this.proc.killed) { return this.proc; }

    const exe = path.join(this.ctx.extensionPath, 'out', 'bin', 'MathCursor.CaretPopup.exe');
    const p = spawn(exe, [], { windowsHide: true });
    p.stdout!.setEncoding('utf8');
    p.stdout!.on('data', d => this.onData(d));
    p.on('error', e => console.error('[mathcursor] helper:', e.message));
    p.on('exit', () => { if (this.proc === p) { this.proc = undefined; this.shown = false; } });
    this.proc = p;
    return p;
  }

  private onData(d: string): void {
    this.buf += d;
    let nl: number;
    while ((nl = this.buf.indexOf('\n')) >= 0) {
      const line = this.buf.slice(0, nl).replace(/\r$/, '');
      this.buf = this.buf.slice(nl + 1);
      if (!line) { continue; }
      const parts = line.split('\t');
      if (parts[0] === 'COMMIT') { this.shown = false; this.onCommit(parseInt(parts[1], 10)); }
      else if (parts[0] === 'DISMISS') { this.shown = false; this.onDismiss(); }
    }
  }
}
