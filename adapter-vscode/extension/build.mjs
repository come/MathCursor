import * as esbuild from 'esbuild';
import { cpSync, rmSync, mkdirSync } from 'fs';
import { spawnSync } from 'child_process';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const watch = process.argv.includes('--watch');

// LICENSE dans le dossier extension (exigé par vsce pour le packaging VSIX) —
// copié depuis la racine pour ne pas dupliquer en git (cf. .gitignore).
cpSync(path.join(__dirname, '..', '..', 'LICENSE'), path.join(__dirname, 'LICENSE'));

const rustDir = path.join(__dirname, '..', '..', 'rust');
const outEngine = path.join(__dirname, 'out', 'engine');

// 1. Moteur forest porté en RUST (mc-engine → analyze.exe, service stdio
//    persistant). Remplace le sidecar WASM .NET (−23 Mo, plus de runtime
//    dotnet.js). Gate de parité fixtures.json 456/456. Cf. ADR Phase 2.
const analyzeExe = path.join(rustDir, 'target', 'release', 'analyze.exe');
if (process.platform === 'win32') {
  spawnSync('taskkill', ['/F', '/IM', 'analyze.exe'], { stdio: 'ignore', shell: true });
}
if (!process.argv.includes('--skip-engine')) {
  console.log('[build] cargo build mc-engine (analyze, release)…');
  const r = spawnSync('cargo', ['build', '--release', '-p', 'mc-engine', '--bin', 'analyze'],
    { cwd: rustDir, stdio: 'inherit', shell: true });
  if (r.status !== 0) process.exit(r.status ?? 1);
}
rmSync(outEngine, { recursive: true, force: true });
mkdirSync(outEngine, { recursive: true });
cpSync(analyzeExe, path.join(outEngine, 'analyze.exe'));
console.log('[build] moteur Rust copié → out/engine/analyze.exe');

// 2b. Coquille popup Rust (mc-popup, mode actif Windows : MSAA caret + hook
//     clavier) + assets KaTeX → out/popup. Remplace l'ancien helper WPF.
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

// 2c. Service NER persistant en RUST (mc-ner, onnxruntime STATIQUE → exe
//     autonome, aucune DLL). Remplace le helper .NET ner-helper : VSCode = 0
//     .NET. Tokenizer NATIF du modèle (tokenizer.json). Cf. ADR Phase 3.
const mcNerExe = path.join(rustDir, 'target', 'release', 'mc-ner.exe');
const outNer = path.join(__dirname, 'out', 'ner');
if (process.platform === 'win32') {
  spawnSync('taskkill', ['/F', '/IM', 'mc-ner.exe'], { stdio: 'ignore', shell: true });
}
if (!process.argv.includes('--skip-ner')) {
  console.log('[build] cargo build mc-ner (release)…');
  const n = spawnSync('cargo', ['build', '--release', '-p', 'mc-ner', '--bin', 'mc-ner'],
    { cwd: rustDir, stdio: 'inherit', shell: true });
  if (n.status !== 0) process.exit(n.status ?? 1);
}
rmSync(outNer, { recursive: true, force: true });
mkdirSync(outNer, { recursive: true });
cpSync(mcNerExe, path.join(outNer, 'mc-ner.exe'));
console.log('[build] service NER Rust copié → out/ner/mc-ner.exe');

// Modèle (model_quantized.onnx + tokenizer.json) → bundle.
const modelSrc = path.join(__dirname, '..', '..', 'models', 'latest');
const modelDst = path.join(__dirname, 'out', 'models', 'latest');
rmSync(path.join(__dirname, 'out', 'models'), { recursive: true, force: true });
mkdirSync(modelDst, { recursive: true });
for (const f of ['model_quantized.onnx', 'tokenizer.json']) {
  cpSync(path.join(modelSrc, f), path.join(modelDst, f));
}
console.log('[build] modèle NER copié → out/models/latest');

// 3. Bundle l'extension (esbuild ; vscode externe).
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
