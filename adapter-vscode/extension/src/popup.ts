// MathCursor: capturing mathematical intent from linear keyboard input.
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
import { spawn, ChildProcess } from 'child_process';
import * as path from 'path';

// Pilote la coquille popup Rust (mc-popup, wry+tao+KaTeX) en MODE ACTIF : la
// coquille lit elle-même le caret (MSAA) et capte le clavier (↑↓/Entrée/Échap) —
// VSCode n'expose ni l'un ni l'autre. L'extension n'envoie que les candidats et
// reçoit commit/dismiss. Protocole : 1 JSON par ligne (stdin/stdout).
// Cf. ADR 2026-06-24-Feat-rust-unified-toolkit (Phase 1).
//
// Volontairement Windows-only au palier 2 cross-OS : le mode actif lit le caret
// (MSAA) que VSCode n'expose pas, et le mode passif exigerait des coords écran
// que l'API VSCode ne donne pas. mac/linux (mode actif AX API / AT-SPI) = palier 3.
// Cf. ADR 2026-06-25-Feat-vscode-marketplace-publishing-model.

export function isHelperAvailable(): boolean {
  return process.platform === 'win32';
}

function themeName(): string {
  const k = vscode.window.activeColorTheme.kind;
  return (k === vscode.ColorThemeKind.Light || k === vscode.ColorThemeKind.HighContrastLight)
    ? 'light' : 'dark';
}

export interface ShowArgs {
  candidates: string[];
  colDelta: number;    // nb de caractères du début de zone au caret
  fontSize: number;    // editor.fontSize (logique)
  reposition: boolean; // recalculer l'ancrage (nouvelle zone) ou figer
  showLatex: boolean;  // afficher la ligne LaTeX sous la formule
  collapseAt: number;  // nb de candidats avant la ligne « voir plus » (0 = tout)
}

export class PopupController {
  private proc: ChildProcess | undefined;
  private buf = '';
  private shown = false;
  private ready = false;
  private pending: ShowArgs | undefined;   // show en attente du 'ready' (1er lancement)
  private failures = 0;                     // spawns morts avant 'ready' (anti-boucle)
  private dead = false;                     // abandon après trop d'échecs

  constructor(
    private readonly ctx: vscode.ExtensionContext,
    private readonly onCommit: (index: number) => void,
    private readonly onDismiss: () => void
  ) {}

  get isShown(): boolean { return this.shown; }

  show(args: ShowArgs): void {
    const p = this.ensureProc();
    if (!p || args.candidates.length === 0) { return; }
    if (!this.ready) { this.pending = args; return; } // attend que la page charge
    this.sendShow(p, args);
  }

  private sendShow(p: ChildProcess, a: ShowArgs): void {
    const msg = {
      cmd: 'show',
      candidates: a.candidates.map(latex => ({ latex })),
      x: 0, y: 0, lineHeight: 0,   // ignorés en mode actif (coquille lit le caret)
      selectedIndex: -1,           // nav-mode opt-in (aucun surlignage au départ)
      theme: themeName(),          // dark / light (suit le thème VSCode)
      colDelta: a.colDelta,        // ancrage au début du texte reconnu
      fontSize: a.fontSize,
      reposition: a.reposition,
      showLatex: a.showLatex,      // ligne LaTeX optionnelle
      collapseAt: a.collapseAt,    // repli « voir plus »
      moreLabel: vscode.l10n.t('show more'), // phrase localisée de la ligne « voir plus » (EN/FR selon locale)
      commitFirstKey: 'Tab',       // Tab valide le 1er candidat sans nav (façon Word) ;
                                   // Entrée sans nav redescend à l'éditeur (saut de ligne).
    };
    try { p.stdin!.write(JSON.stringify(msg) + '\n'); this.shown = true; }
    catch { /* ignore */ }
  }

  hide(): void {
    this.pending = undefined;
    if (this.proc && this.shown) {
      try { this.proc.stdin!.write('{"cmd":"close"}\n'); } catch { /* ignore */ }
    }
    this.shown = false;
  }

  dispose(): void {
    if (this.proc) {
      try { this.proc.stdin!.write('{"cmd":"quit"}\n'); } catch { /* ignore */ }
      this.proc.kill();
      this.proc = undefined;
    }
    this.shown = false;
  }

  private ensureProc(): ChildProcess | undefined {
    if (!isHelperAvailable() || this.dead) { return undefined; }
    if (this.proc && !this.proc.killed) { return this.proc; }

    const exe = path.join(this.ctx.extensionPath, 'out', 'popup', 'mc-popup.exe');
    const html = path.join(this.ctx.extensionPath, 'out', 'popup', 'assets', 'popup', 'index.html');
    const p = spawn(exe, [html, '--active'], { windowsHide: true });
    p.stdout!.setEncoding('utf8');
    p.stdout!.on('data', d => this.onData(d));
    p.stderr!.setEncoding('utf8');
    p.stderr!.on('data', d => console.error('[mathcursor] popup stderr:', d.toString().trim()));
    p.on('error', e => console.error('[mathcursor] popup spawn:', e.message));
    p.on('exit', () => {
      if (this.proc !== p) { return; }
      // Mort avant d'avoir été prête = échec ; au bout de 3, on abandonne (pas de
      // boucle de re-spawn invisible si WebView2/desktop indisponible).
      if (!this.ready && ++this.failures >= 3) { this.dead = true; }
      this.proc = undefined; this.shown = false; this.ready = false; this.pending = undefined;
    });
    this.proc = p;
    return p;
  }

  private onData(d: string): void {
    this.buf += d;
    let nl: number;
    while ((nl = this.buf.indexOf('\n')) >= 0) {
      const line = this.buf.slice(0, nl).trim();
      this.buf = this.buf.slice(nl + 1);
      if (!line) { continue; }
      let evt: { evt?: string; index?: number; msg?: string };
      try { evt = JSON.parse(line); } catch { continue; }
      switch (evt.evt) {
        case 'ready':
          this.ready = true; this.failures = 0;
          if (this.pending && this.proc) { const c = this.pending; this.pending = undefined; this.sendShow(this.proc, c); }
          break;
        case 'commit': this.shown = false; this.onCommit(Number(evt.index)); break;
        case 'dismiss': this.shown = false; this.onDismiss(); break;
        // 'closed' = fermeture spontanée (perte de focus) : resync SANS marquer
        // la zone comme rejetée (≠ Échap), pour qu'elle réapparaisse au retour.
        case 'closed': this.shown = false; break;
        case 'error': console.warn('[mathcursor] popup:', evt.msg); break;
      }
    }
  }
}
