import * as esbuild from 'esbuild';
import { cpSync, rmSync, mkdirSync } from 'fs';
import { spawnSync } from 'child_process';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const watch = process.argv.includes('--watch');

const engineProj = path.join(__dirname, '..', 'engine-wasm');
const publishFramework = path.join(
  engineProj, 'bin', 'Release', 'net9.0', 'publish', 'wwwroot', '_framework'
);
const outEngine = path.join(__dirname, 'out', 'engine');

// 1. Compile le moteur en WASM (sauf si --skip-engine).
if (!process.argv.includes('--skip-engine')) {
  console.log('[build] dotnet publish engine-wasm (Release)…');
  const r = spawnSync('dotnet', ['publish', engineProj, '-c', 'Release'],
    { stdio: 'inherit', shell: true });
  if (r.status !== 0) process.exit(r.status ?? 1);
}

// 2. Copie les assets runtime à côté du bundle (hors .gz/.br — duplicats compressés).
rmSync(outEngine, { recursive: true, force: true });
cpSync(publishFramework, path.join(outEngine, '_framework'), {
  recursive: true,
  filter: (src) => !src.endsWith('.gz') && !src.endsWith('.br'),
});
console.log('[build] assets WASM copiés → out/engine/_framework');

// 2b. Helper natif popup au caret (Windows). Build net48 + copie de l'exe et de
//     ses dll (WpfMath…) dans out/bin.
const helperProj = path.join(__dirname, '..', 'caret-popup');
const helperOut = path.join(helperProj, 'bin', 'Release', 'net48');
const outBin = path.join(__dirname, 'out', 'bin');
if (!process.argv.includes('--skip-helper')) {
  console.log('[build] dotnet build caret-popup (Release)…');
  const h = spawnSync('dotnet', ['build', helperProj, '-c', 'Release'],
    { stdio: 'inherit', shell: true });
  if (h.status !== 0) process.exit(h.status ?? 1);
}
// Le helper persistant peut être en cours d'exécution (lancé par une fenêtre de
// dev) et verrouiller l'exe → on le tue avant de copier (Windows).
if (process.platform === 'win32') {
  spawnSync('taskkill', ['/F', '/IM', 'MathCursor.CaretPopup.exe'], { stdio: 'ignore', shell: true });
}
rmSync(outBin, { recursive: true, force: true });
cpSync(helperOut, outBin, {
  recursive: true,
  filter: (src) => !src.endsWith('.pdb'),
});
console.log('[build] helper natif copié → out/bin');

// 2c. Helper NER persistant (ONNX) + modèle (bundle dans le VSIX).
const nerProj = path.join(__dirname, '..', 'ner-helper');
const nerOut = path.join(nerProj, 'bin', 'Release', 'net48', 'win-x64');
const outNer = path.join(__dirname, 'out', 'ner');
if (!process.argv.includes('--skip-ner')) {
  console.log('[build] dotnet build ner-helper (Release win-x64)…');
  const n = spawnSync('dotnet', ['build', nerProj, '-c', 'Release', '-r', 'win-x64'],
    { stdio: 'inherit', shell: true });
  if (n.status !== 0) process.exit(n.status ?? 1);
}
if (process.platform === 'win32') {
  spawnSync('taskkill', ['/F', '/IM', 'MathCursor.Ner.exe'], { stdio: 'ignore', shell: true });
}
rmSync(outNer, { recursive: true, force: true });
cpSync(nerOut, outNer, {
  recursive: true,
  filter: (src) => !src.endsWith('.pdb') && !src.endsWith('.lib'),
});
console.log('[build] helper NER copié → out/ner');

// Modèle (model_quantized.onnx + vocab.txt) → bundle.
const modelSrc = path.join(__dirname, '..', '..', 'models', 'latest');
const modelDst = path.join(__dirname, 'out', 'models', 'latest');
rmSync(path.join(__dirname, 'out', 'models'), { recursive: true, force: true });
mkdirSync(modelDst, { recursive: true });
for (const f of ['model_quantized.onnx', 'vocab.txt']) {
  cpSync(path.join(modelSrc, f), path.join(modelDst, f));
}
console.log('[build] modèle NER copié → out/models/latest');

// 3. Bundle l'extension (esbuild ; mathjax-full ESM inclus, vscode externe).
const options = {
  entryPoints: [path.join(__dirname, 'src', 'extension.ts')],
  bundle: true,
  outfile: path.join(__dirname, 'out', 'extension.js'),
  platform: 'node',
  format: 'cjs',
  target: 'node18',
  external: ['vscode'],
  sourcemap: true,
  logLevel: 'info',
};

if (watch) {
  const c = await esbuild.context(options);
  await c.watch();
  console.log('[build] watch actif');
} else {
  await esbuild.build(options);
  console.log('[build] bundle extension.js OK');
}
