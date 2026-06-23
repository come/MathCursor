import * as path from 'path';
import * as url from 'url';

// Charge LE moteur forest (MathCursor.Engine) compilé en WASM, dans l'extension
// host (Node). Même Analyze que Word et que la démo web → zéro divergence.
// Chargement LAZY : le runtime .NET ne démarre qu'au 1er trigger (boot ~230 ms,
// puis ~2 ms/analyse). Cf. ADR 2026-06-23-Feat-vscode-host + spike node-smoketest.

export interface Candidate { latex: string; cost: number; }
export interface AnalyzeResult { decision: string; hasNote: boolean; ranked: Candidate[]; }

let exportsPromise: Promise<any> | undefined;

async function loadEngine(): Promise<any> {
  // Les assets WASM sont copiés dans out/engine/_framework au build (build.mjs).
  const dotnetPath = path.join(__dirname, 'engine', '_framework', 'dotnet.js');
  // import() dynamique d'un module ESM depuis ce bundle CJS — chemin calculé
  // (esbuild le laisse en import runtime, ne tente pas de le résoudre).
  const mod: any = await import(url.pathToFileURL(dotnetPath).href);
  const { dotnet } = mod;
  const { getAssemblyExports, getConfig } = await dotnet.create();
  const config = getConfig();
  return getAssemblyExports(config.mainAssemblyName);
}

export async function analyze(src: string, culture: string): Promise<AnalyzeResult> {
  if (!exportsPromise) exportsPromise = loadEngine();
  const asm = await exportsPromise;
  const json: string = asm.Bridge.Analyze(src, culture);
  return JSON.parse(json) as AnalyzeResult;
}
