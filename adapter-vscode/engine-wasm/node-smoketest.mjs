// Spike : prouve que le moteur C# (compilé en WASM) tourne dans Node (= l'extension
// host VSCode) et que Bridge.Analyze répond. Lance : node node-smoketest.mjs
import { dotnet } from './bin/Release/net9.0/publish/wwwroot/_framework/dotnet.js';

const t0 = performance.now();
const { getAssemblyExports, getConfig } = await dotnet.create();
const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
const tBoot = performance.now() - t0;
console.log(`boot=${tBoot.toFixed(0)}ms  mainAssembly=${config.mainAssemblyName}`);

const cases = ['x^2+racine(2)', 'lim x->0 sin(x)/x', 'integrale de 0 a 1 de x^2 dx', '1/2 + 3/4'];
for (const src of cases) {
  const t = performance.now();
  const json = exports.Bridge.Analyze(src, 'fr');
  const dt = performance.now() - t;
  const r = JSON.parse(json);
  const top = r.ranked[0]?.latex ?? '(aucun)';
  console.log(`\n[${dt.toFixed(1)}ms] "${src}" -> decision=${r.decision} n=${r.ranked.length}`);
  console.log(`   top: ${top}`);
}
