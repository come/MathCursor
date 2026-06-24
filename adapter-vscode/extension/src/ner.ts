import * as vscode from 'vscode';
import { spawn, ChildProcess } from 'child_process';
import * as path from 'path';
import * as fs from 'fs';

// Pilote le helper NER PERSISTANT (MathNerDetector ONNX, parité Word). Charge le
// modèle une fois (~1,1 s) puis répond en ~7 ms. Robuste : chaque requête a un
// timeout ; si le helper se bloque, on le tue et on le re-spawn au prochain
// appel (auto-réparation) → jamais de blocage durable de l'UI.
// Windows + modèle présent ; sinon isAvailable=false → repli SpanComputer.

type Resolver = (z: { start: number; end: number } | undefined) => void;
interface Pending { resolve: Resolver; timer: ReturnType<typeof setTimeout>; }

// > au chargement à froid du modèle (~1,1 s) pour ne pas tuer la 1re requête.
const REQUEST_TIMEOUT_MS = 3000;

export class NerController {
  private proc: ChildProcess | undefined;
  private buf = '';
  private queue: Pending[] = [];   // FIFO (le helper répond dans l'ordre)
  private ready = false;
  private dead = false;            // indispo définitive (exe/modèle absent, FATAL)
  private restarting = false;      // kill volontaire (timeout) ≠ crash

  constructor(private readonly ctx: vscode.ExtensionContext) {}

  get isAvailable(): boolean {
    return process.platform === 'win32' && !this.dead && this.modelExists();
  }

  warmup(): void { this.ensureProc(); }

  detect(text: string, caret: number): Promise<{ start: number; end: number } | undefined> {
    const p = this.ensureProc();
    if (!p || this.dead) { return Promise.resolve(undefined); }
    const safe = text.replace(/[\t\r\n]/g, ' '); // 1:1 longueur → offsets préservés
    return new Promise<{ start: number; end: number } | undefined>(resolve => {
      const entry: Pending = {
        resolve,
        timer: setTimeout(() => {
          if (!this.queue.includes(entry)) { return; }
          // Bloqué APRÈS chargement → redémarrage. Encore en warmup (pas ready) →
          // on abandonne juste cette requête, sans tuer le helper qui charge.
          if (this.ready) { this.restart(); }
          else { this.settle(entry, undefined); }
        }, REQUEST_TIMEOUT_MS),
      };
      this.queue.push(entry);
      try { p.stdin!.write(`DETECT\t${caret}\t${safe}\n`); }
      catch { this.settle(entry, undefined); }
    });
  }

  dispose(): void {
    this.dead = true;
    this.flush();
    if (this.proc) {
      try { this.proc.stdin!.write('QUIT\n'); } catch { /* ignore */ }
      this.proc.kill();
      this.proc = undefined;
    }
  }

  // ── interne ──────────────────────────────────────────────────────────────

  private settle(entry: Pending, value: { start: number; end: number } | undefined): void {
    const i = this.queue.indexOf(entry);
    if (i >= 0) { this.queue.splice(i, 1); }
    clearTimeout(entry.timer);
    entry.resolve(value);
  }

  private flush(): void {
    const q = this.queue;
    this.queue = [];
    for (const e of q) { clearTimeout(e.timer); e.resolve(undefined); }
  }

  /** Helper bloqué : on vide la file (→ undefined) et on tue le process ; il
   *  sera re-spawné (frais) au prochain detect. Pas de mort définitive. */
  private restart(): void {
    console.warn('[mathcursor] ner: helper bloqué/timeout → redémarrage');
    this.restarting = true;
    this.flush();
    this.ready = false;
    if (this.proc) { try { this.proc.kill(); } catch { /* ignore */ } this.proc = undefined; }
  }

  private modelExists(): boolean {
    return fs.existsSync(this.modelDir() + path.sep + 'model_quantized.onnx');
  }

  private modelDir(): string {
    return path.join(this.ctx.extensionPath, 'out', 'models', 'latest');
  }

  private ensureProc(): ChildProcess | undefined {
    if (process.platform !== 'win32' || this.dead) { return undefined; }
    if (this.proc && !this.proc.killed) { return this.proc; }
    if (!this.modelExists()) { this.dead = true; return undefined; }

    const exe = path.join(this.ctx.extensionPath, 'out', 'ner', 'MathCursor.Ner.exe');
    const p = spawn(exe, [this.modelDir()], { windowsHide: true });
    p.stdout!.setEncoding('utf8');
    p.stdout!.on('data', d => this.onData(d));
    p.on('error', e => { console.error('[mathcursor] ner spawn:', e.message); this.dead = true; this.flush(); });
    p.on('exit', () => {
      if (this.proc !== p) { return; }
      const wasReady = this.ready;
      this.proc = undefined;
      this.flush();
      if (this.restarting) { this.restarting = false; } // notre propre kill → pas mort
      else if (!wasReady) { this.dead = true; }          // crash avant READY → indispo
    });
    this.proc = p;
    return p;
  }

  private onData(d: string): void {
    this.buf += d;
    let nl: number;
    while ((nl = this.buf.indexOf('\n')) >= 0) {
      const line = this.buf.slice(0, nl).replace(/\r$/, '');
      this.buf = this.buf.slice(nl + 1);
      if (!line || line === 'READY') { this.ready = true; continue; }
      if (line === 'FATAL') { this.dead = true; this.restart(); continue; }
      const entry = this.queue[0];   // FIFO
      if (!entry) { continue; }
      const parts = line.split('\t');
      this.settle(entry, parts[0] === 'ZONE'
        ? { start: parseInt(parts[1], 10), end: parseInt(parts[2], 10) }
        : undefined);
    }
  }
}
