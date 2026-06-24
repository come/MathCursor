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

// 2b. Coquille popup Rust (mc-popup, mode actif Windows : MSAA caret + hook
//     clavier) + assets KaTeX → out/popup. Remplace l'ancien helper WPF.
const rustDir = path.join(__dirname, '..', '..', 'rust');
const popupExe = path.join(rustDir, 'target', 'release', 'mc-popup.exe');
const katexSrc = path.join(__dirname, 'popup', 'katex');       // KaTeX vendorisé (extension)
const htmlSrc = path.join(__dirname, 'popup', 'index.html');   // HTML thémé VSCode
const outPopup = path.join(__dirname, 'out', 'popup');
const outPopupAssets = path.join(outPopup, 'assets', 'popup');
// La coquille peut tourner (fenêtre de dev) et verrouiller out/popup → on la tue.
if (process.platform === 'win32') {
  spawnSync('taskkill', ['/F', '/IM', 'mc-popup.exe'], { stdio: 'ignore', shell: true });
}
if (!process.argv.includes('--skip-popup')) {
  console.log('[build] cargo build mc-popup (release)…');
  const c = spawnSync('cargo', ['build', '--release', '-p', 'mc-popup'],
    { cwd: rustDir, stdio: 'inherit', shell: true });
  if (c.status !== 0) process.exit(c.status ?? 1);
}
rmSync(outPopup, { recursive: true, force: true });
mkdirSync(outPopupAssets, { recursive: true });
cpSync(popupExe, path.join(outPopup, 'mc-popup.exe'));
cpSync(htmlSrc, path.join(outPopupAssets, 'index.html'));            // HTML VSCode thémé
cpSync(katexSrc, path.join(outPopupAssets, 'katex'), { recursive: true }); // fonts/css/js KaTeX
console.log('[build] coquille Rust + HTML thémé + KaTeX copiés → out/popup');

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
