import * as esbuild from 'esbuild';
import { cpSync, rmSync } from 'fs';
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
