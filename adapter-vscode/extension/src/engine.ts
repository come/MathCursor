import { spawn, ChildProcess } from 'child_process';
import * as path from 'path';
import * as fs from 'fs';

// Pilote le moteur forest porté en RUST (mc-engine), service stdio PERSISTANT
// (remplace le sidecar WASM .NET : −23 Mo, plus de runtime dotnet.js). Même
// Analyze que Word et la démo web → zéro divergence (gate fixtures.json 456/456).
// Robuste : timeout par requête + re-spawn auto si le process meurt.
// Portable (Rust pur) : tourne sur tout OS où le binaire `analyze` est bundlé ;
// si absent (cible pas encore buildée) → `dead` → erreur propre, pas de crash.
// Cf. ADR 2026-06-24-Feat-rust-engine-port (Phase 2) +
//    2026-06-25-Feat-vscode-marketplace-publishing-model (cross-OS, palier 2).

export interface Candidate { latex: string; cost: number; }
export interface AnalyzeResult { decision: string; hasNote: boolean; ranked: Candidate[]; }

const REQUEST_TIMEOUT_MS = 2000;
const ERREUR: AnalyzeResult = { decision: 'erreur', hasNote: false, ranked: [] };

type Pending = { resolve: (r: AnalyzeResult) => void; timer: ReturnType<typeof setTimeout>; };

let proc: ChildProcess | undefined;
let buf = '';
let ready = false;
let dead = false;          // exe absent / crash avant READY → indispo
let restarting = false;    // kill volontaire (timeout) ≠ crash
let queue: Pending[] = []; // FIFO (le service répond dans l'ordre)

const EXE = process.platform === 'win32' ? '.exe' : '';

function exePath(): string {
  return path.join(__dirname, 'engine', `analyze${EXE}`);
}

function settle(entry: Pending, value: AnalyzeResult): void {
  const i = queue.indexOf(entry);
  if (i >= 0) { queue.splice(i, 1); }
  clearTimeout(entry.timer);
  entry.resolve(value);
}

function flush(): void {
  const q = queue;
  queue = [];
  for (const e of q) { clearTimeout(e.timer); e.resolve(ERREUR); }
}

// Service bloqué : on vide la file (→ erreur) et on tue le process ; re-spawné
// (frais) au prochain analyze. Pas de mort définitive.
function restart(): void {
  console.warn('[mathcursor] engine: service bloqué/timeout → redémarrage');
  restarting = true;
  flush();
  ready = false;
  if (proc) { try { proc.kill(); } catch { /* ignore */ } proc = undefined; }
}

function ensureProc(): ChildProcess | undefined {
  if (dead) { return undefined; }
  if (proc && !proc.killed) { return proc; }
  if (!fs.existsSync(exePath())) { dead = true; return undefined; } // binaire absent (cible non buildée)

  const p = spawn(exePath(), [], { windowsHide: true });
  p.stdout!.setEncoding('utf8');
  p.stdout!.on('data', d => onData(d));
  p.stderr!.setEncoding('utf8');
  // Le lexer panique sur un caractère inattendu (rattrapé côté Rust → "erreur") :
  // la trace part sur stderr, on la garde discrète.
  p.stderr!.on('data', () => { /* ignore (panics rattrapés) */ });
  p.on('error', e => { console.error('[mathcursor] engine spawn:', e.message); dead = true; flush(); });
  p.on('exit', () => {
    if (proc !== p) { return; }
    const wasReady = ready;
    proc = undefined; ready = false;
    flush();
    if (restarting) { restarting = false; }     // notre propre kill → pas mort
    else if (!wasReady) { dead = true; }         // crash avant READY → indispo
  });
  proc = p;
  return p;
}

function onData(d: string): void {
  buf += d;
  let nl: number;
  while ((nl = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, nl).replace(/\r$/, '');
    buf = buf.slice(nl + 1);
    if (!line) { continue; }
    if (line === 'READY') { ready = true; continue; }
    const entry = queue[0]; // FIFO
    if (!entry) { continue; }
    try { settle(entry, JSON.parse(line) as AnalyzeResult); }
    catch { settle(entry, ERREUR); }
  }
}

export async function analyze(src: string, culture: string): Promise<AnalyzeResult> {
  const p = ensureProc();
  if (!p || dead) { return ERREUR; } // exe absent (cible non buildée) → pas de moteur
  const safe = src.replace(/[\t\r\n]/g, ' '); // protocole TAB-délimité
  return new Promise<AnalyzeResult>(resolve => {
    const entry: Pending = {
      resolve,
      timer: setTimeout(() => {
        if (!queue.includes(entry)) { return; }
        if (ready) { restart(); } else { settle(entry, ERREUR); }
      }, REQUEST_TIMEOUT_MS),
    };
    queue.push(entry);
    try { p.stdin!.write(`${culture}\t${safe}\n`); }
    catch { settle(entry, ERREUR); }
  });
}

/** Libère le process moteur (appelé au dispose de l'extension). */
export function disposeEngine(): void {
  dead = true;
  flush();
  if (proc) {
    try { proc.stdin!.write('QUIT\n'); } catch { /* ignore */ }
    proc.kill();
    proc = undefined;
  }
}
