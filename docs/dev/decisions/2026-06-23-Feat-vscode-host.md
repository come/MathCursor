# Feat — Host VSCode : LaTeX propre inline au caret (moteur réutilisé via WASM, complétion native pré-rendue)

**Date :** 2026-06-23
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Lié à :** [2026-06-23-Refactor-delete-dead-host-contract.md](2026-06-23-Refactor-delete-dead-host-contract.md) (portabilité du moteur pur prouvée par la démo WASM), [2026-06-19-Feat-web-demo-real-mode-editor.md](2026-06-19-Feat-web-demo-real-mode-editor.md) (précédent : un hôte non-Word réutilise `Bridge.Analyze` tel quel)

## Citation acté

> « j'ai fait marcher [LaTeX Workshop] j'aimerai rajouter un plugin math cursor
> par dessus pour generer du latex propre inline dans vscode, avec une popup du
> coup comme sur word mais dans vs code » — utilisateur, 2026-06-23
>
> Popup : « 1 [complétion native] si on peut avoir le prérendu dedans, sinon 2
> [webview] ». Réutilisation moteur : « Tu décides ». — utilisateur, 2026-06-23
> (plan validé via plan mode avant tout code de prod)

## Contexte

Première brique de **phase 2** (nouvel hôte hors Word). L'utilisateur a LaTeX
Workshop fonctionnel et veut, par-dessus, taper de la notation math en texte
simple, déclencher, choisir un candidat LaTeX « comme la popup Word », et
l'insérer **inline**. LaTeX Workshop reste indépendant (il compile/preview) :
MathCursor **génère** le LaTeX, simple coexistence sur `.tex`/`.md`, zéro couplage
d'API.

L'archi le permet sans toucher le cœur : le moteur (`ForestEngine.Analyze` :
texte+culture → candidats classés `{Latex, Cost}`) est **pur, netstandard2.0,
zéro Word**, et la démo WASM prouve déjà qu'il tourne hors-Word via `Bridge.Analyze`.
Un host VSCode = un nouveau **L6** ; on réutilise L0–L2 tel quel et on **s'arrête
au LaTeX** (pas d'OMML — `LatexToOmml` est Word-only).

## Décision

1. **Nouveau dossier top-level `adapter-vscode/`** (frère de `adapter-vsto/`,
   `web-demo/`, `engine-python/`) : `engine-wasm/` (interop) + `extension/` (host TS).

2. **Réutilisation du moteur via WASM** (et **non** un port TypeScript) :
   `adapter-vscode/engine-wasm` compile `MathCursor.Engine` en `browser-wasm`
   (SDK `Microsoft.NET.Sdk.WebAssembly`, `[JSExport] Bridge.Analyze(input,culture)
   → JSON {decision,hasNote,ranked:[{latex,cost}]}`, JSON construit à la main pour
   robustesse au trimming). Le runtime `dotnet.js` est chargé **dans l'extension
   host (Node)** et appelé directement — **zéro divergence** avec Word (même
   `ForestEngine`), **aucun runtime .NET** à installer côté utilisateur (le `.wasm`
   est packagé dans l'extension), cross-plateforme.

3. **UI = complétion native VSCode** (`CompletionItemProvider`), déclenchée
   manuellement (Ctrl+Espace) sur `latex`/`tex`/`markdown`. Chaque candidat = un
   `CompletionItem` (ordre = `Cost`), avec **pré-rendu de la formule** dans le
   panneau de détails : `documentation` = `MarkdownString` contenant une image
   **SVG MathJax** en data-URI (`![](data:image/svg+xml,…)`), + le LaTeX source.
   `insertText` = LaTeX (wrap configurable). Pas de webview.
   **Forme de l'image** : markdown image `![]()` en défaut (markdown pur, ni
   `supportHtml` ni `isTrusted` requis → surface de sécurité minimale). Le spike a
   montré que `<img>` HTML (avec `supportHtml=true`) rend **aussi** ; on n'y bascule
   que si un contrôle de hauteur/`vertical-align` explicite devient nécessaire
   (différence d'une ligne, réversible).
   **MathJax** : licence **Apache-2.0** (`mathjax-full` sur npm) → libre, usage
   commercial OK, redistribuable dans le VSIX.

## Spikes (gate, faits avant cet ADR — POC minimal avant la prod)

- **#1 WASM-in-Node** : `node node-smoketest.mjs` → boot one-time **232 ms**, puis
  **~2 ms/analyse** (183 ms au 1er appel = warmup) ; `x^2+racine(2)` → `x^{2}+\sqrt{2}`,
  `1/2 + 3/4` → `\frac{1}{2}+\frac{3}{4}` (parité exacte démo web). → chargement
  **lazy** au 1er trigger, latence négligeable. Fallback sidecar **non nécessaire**.
- **#2 pré-rendu en complétion native** : mini-extension jetable, 3 variantes
  (image markdown `![]()`, HTML `<img>`, contrôle texte) testées en F5 par
  l'utilisateur → **la variante markdown image rend la formule SVG** dans le
  panneau de détails du widget de suggestion. → variante native validée ; webview
  écartée.

## Tradeoff & alternatives écartées

- **Port TypeScript du moteur** : performant/léger en VSCode, mais **2ᵉ
  implémentation** à maintenir en parité avec le C# (contre la doctrine « un seul
  moteur »). Écarté : le WASM donne la parité gratuitement.
- **Sidecar .NET self-contained** (JSON sur stdio) : zéro divergence aussi, mais
  binaires natifs par OS (VSIX plus lourd) + process à gérer. Gardé en **fallback**
  seulement ; le spike #1 a montré que le WASM-in-Node suffit.
- **Webview popup au caret** (rendu KaTeX, look Word) : plus de contrôle visuel
  mais positionnement au caret délicat et focus à gérer. **Écarté car le spike #2
  a prouvé que le pré-rendu marche dans la complétion native** (choix utilisateur :
  natif si prérendu possible).
- **Réutiliser le projet Blazor de la démo** : le `Bridge.Analyze` Blazor est lié
  au runtime Blazor (DotNet.invokeMethod) ; on préfère un projet WASM dédié
  `[JSExport]` plus léger et appelable directement depuis Node.
- **Dépendre de l'API LaTeX Workshop** : inutile — coexistence, pas d'intégration.

## Conséquences

- **Nouveau code** : `adapter-vscode/engine-wasm/*` (projet WASM + `Bridge`),
  `adapter-vscode/extension/*` (host TS : manifest, trigger, lecture src, insertion,
  pré-rendu MathJax, chargement WASM lazy). `engine-wasm.csproj` à déclarer dans
  `MathCursor.sln`.
- **Cœur** : `engine/` et `serialization/` **inchangés** (réutilisation pure).
- **Build** : ajoute les workloads `wasm-tools` + `wasm-experimental` (.NET 9) et
  une dép npm `mathjax-full` (pré-rendu SVG côté extension host).
- **Réglages** : `mathcursor.culture` (fr/us), `mathcursor.delimiters` (`$…$`
  défaut .md / `\(…\)` / brut), `mathcursor.maxCandidates` (défaut 3, comme Word/web).
- **Hors périmètre** : OMML/Word, publication marketplace/VSIX, télémétrie,
  persistance des choix de désambiguïsation (sidecar L2). Spikes jetables
  (`spike-extension/`, `node-smoketest.mjs`) à retirer après stabilisation.
