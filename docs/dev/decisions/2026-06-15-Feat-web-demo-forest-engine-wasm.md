# Feat — Démo web sur le vrai moteur (ForestEngine) compilé en WASM

**Date :** 2026-06-15
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** — (remplace de fait l'ancienne démo Blazor/lattice de DocMath,
hors de ce repo)
**Lié à :** moteur `engine/MathCursor.Engine`, déploiement `tools/cloudflare/deploy.sh`,
skill `/deploy-prod` (étape WASM rebuild + mirror, jusque-là sautée)

## Citation acté

> « j'aimerai refaire marcher correctement la demo: utiliser le vrai moteur
> compilé en WASM et avoir la "popup" qui montre les choix. l'interface: une
> ligne de input texte, et en dessous les cas reconnus. » — utilisateur,
> 2026-06-15. Puis « yes ok commit ça » après itérations UX (exemples
> au-dessus, localisés FR/EN, note espaces, rangs, combo matrice).

## Contexte

La démo en ligne (`docs/demo/`, liée depuis le site) était **morte** : son bundle
WASM référençait `MathCursor.Core.*.wasm` — l'ancien moteur lattice de DocMath,
supprimé de ce repo au pivot forest. Le projet source vivait hors repo
(`D:\Software\DocMath\web-demo\`) et parlait à une API défunte
(`ConvertRich → {Top, Alternatives, Rule}`).

## Décision

Recréer le projet démo **dans ce repo**, branché sur le `ForestEngine` actuel.

- **Projet** : `web-demo/MathCursor.Demo.WebAssembly/` — Blazor WASM (net9.0,
  `PublishTrimmed=false`), `ProjectReference` vers le moteur (netstandard2.0, pur,
  zéro dépendance). Chemin EXACT attendu par `/deploy-prod`.
- **Bridge** : `[JSInvokable] Analyze(input, culture)` → DTO neuf aligné sur
  `AnalyzeResult` (`Decision` auto/popup/erreur + `Candidates[{Latex, Cost}]` +
  `HasNote`). Try/catch → `Decision="erreur"`, jamais d'exception au worker JS.
- **UI** (HTML/JS statique, pas de composant Razor) : exemples cliquables EN HAUT
  (localisés FR/EN via `data-ex-fr`/`data-ex-en`, label optionnel `data-label`),
  note « les espaces comptent », puis ligne d'input + toggle **FR/US math**
  (`EngineCulture.Fr`/`.Us`), puis la **popup produit** dessous : candidat 0 marqué
  « ★ sélectionné d'office », suivants « alternative N », rang à droite, filet à
  gauche (pas de fond), badge décision, note si dense. Rendu LaTeX via **KaTeX**
  (CDN). Toggle de langue UI FR/EN séparé de la culture math.
- **Déploiement** : `dotnet publish` → mirror `web-demo/publish/wwwroot/` vers
  `docs/demo/` (robocopy /MIR : purge les bundles morts Core/FuzzySharp/YamlDotNet).
  Site poussé via `deploy.sh site`.

Le moteur tourne 100 % dans le navigateur — rien n'est envoyé nulle part. C'est
LE même `Analyze` que la version Word : les décisions/candidats de la démo == le
corpus de fixtures.

## Tradeoff & alternatives écartées

- **Réutiliser le bundle DocMath** : impossible, il embarque le moteur mort.
- **AOT natif wasm (lighter)** : tooling lourd (`wasm-tools`), gain marginal — le
  poids du bundle c'est le runtime .NET, pas le moteur (minuscule). Blazor WASM
  non-AOT suffit et marche sans workload.
- **Trim ON** : couperait le DTO et les `[JSInvokable]` (atteints par réflexion
  JS-interop) — laissé OFF.

## Conséquences

- **Fichiers** : `web-demo/MathCursor.Demo.WebAssembly/` (csproj, Program.cs,
  Bridge.cs, wwwroot/{index.html,demo.js,demo.css}) + `docs/demo/` régénéré
  (ancien moteur purgé). `.gitignore` couvrait déjà `web-demo/publish/`, `bin/`,
  `obj/`.
- **Pas de test xUnit nouveau** : la démo n'est qu'une vue du moteur, déjà couvert
  par les 388 fixtures. Vérification = visuelle/clavier en local + en prod.
- **`/deploy-prod`** : son étape WASM rebuild + mirror (jusqu'ici inopérante faute
  de projet) fonctionne désormais — la démo se régénère à chaque release.

## Validation post-fix

Build `dotnet publish -c Release` OK, `docs/demo/_framework/` contient
`MathCursor.Engine.wasm` et plus aucun `MathCursor.Core`. Servi en local
(`http://…/demo/`) : `1/x+1` → popup, `f(x)=2x+1` → auto, combo matrice
∑/lim/1∕∑ → auto, `abs z-conjz` → popup, toggles FR/US et FR/EN OK, rendu KaTeX
propre. À confirmer en prod après `deploy.sh site`.
