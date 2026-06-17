# Feat — Moteur portable : vocabulaire universel (data) + logique portée par langage

**Date :** 2026-06-16
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Lié à :** `engine/src/MathCursor.Engine/` (Vocabulary/Units/EngineCulture/LatexRenderer),
`engine/tests/.../fixtures.json` (contrat), futur `engine-python/`, futur `data/engine/`
**Prépare :** ADR `Feat-libreoffice-uno-python-extension` (l'extension LibreOffice consomme
le port Python)

## Citation acté

> « je m'interroge pour sortir les aspects spécifiques vocabulaires / alias etc dans un
> format universel, et de porter les moteurs » … « full linux / mac / windows + port
> python » — utilisateur, 2026-06-16. Validation du démarrage P1 : « b ».

## Contexte

MathCursor vise une extension **LibreOffice** (Win/Mac/Linux) en **Python/UNO** (pas de
.NET natif) — voir le programme dans le plan. Le moteur est en **C#** ; le réutiliser via
un CLI .NET bundlé serait lourd cross-OS. Décision : **porter** le moteur (Python
maintenant, TS plus tard possible), mais sans dupliquer la masse de données.

Mesure faite : le moteur est **~32 % données / ~68 % logique**. Un **spike** (P0,
`spikes/portable-engine/`) a prouvé qu'un rendu **piloté à 100 % par une data universelle**
(templates + parenthésage par `looseness` + hooks) produit un LaTeX **identique au
caractère près** au C# (7/7), et a révélé une contrainte de format (substitution en un
seul passage).

## Décision

Séparer le moteur en **données universelles déclaratives** (langue-neutre, source unique)
et **logique portée par langage** (C# = référence, puis Python). Le contrat de
comportement reste les **434 fixtures** (`fixtures.json`), exécutées par CHAQUE
implémentation → 434/434 partout = anti-drift.

### Format universel (schéma figé ici)
Fichier(s) sous `data/engine/` (JSON), embarqué(s) par chaque moteur :
- **`const`** : `MUL`, table `looseness` (REL=5, SUM=3, QUANT=2.5, PROD=2, POW=1, APP=0).
- **`cultures`** : `fr`/`us` → `decimalsIn`, `decimalTex`, `intervalSep`, `matrixEnv`,
  + `aliases` (génériques + par langue, mot→clé canonique).
- **`units`** : catégories SI + composés (LaTeX).
- **`symbols`** : par clé canonique →
  `shape` (atom|infix|prefix|nary|postfix), `arity`, `class` (WEAK|STRONG), `looseness`,
  flags (`bracketed`, `cut`, `implicit`, `sup`, `sub`, `tight`, `list`, `mapping`,
  `wordSpace`, `unitWord`, `unitOp`, `apply`), `lower`/`upper` (atom), `alts`/`altsUpper`,
  `coh`, `unary`, `postSign`,
  `render` (template **mini-langage `{0}`/`{1}`, substitution single-pass**),
  `renderImplicit` (variante collée), `renderHook` (nom de hook = logique conditionnelle),
  `variants` (n-aire : `{arity, render, acceptGuard:<nom>}`).

### Logique restant EN CODE (portée par langage, référencée par NOM depuis la data)
- **Renderer** (parenthésage `Child`, traversée AST, env matriciel) — algorithme.
- **Hooks de rendu conditionnels** (3) : `/` (frac|setminus), `neg/pos` (parenthésage),
  `·unit` (espacement). Référencés par `renderHook`.
- **Accept guards** des variantes n-aires (ex. `second_arg_non_numeric`) — référencés par
  `acceptGuard`.
- **Lexer, Parser/Forest, Score** : algorithmes purs, portés à l'identique (vérifiés par
  les fixtures). Les **poids** de `Score` peuvent devenir config plus tard (hors P1).

### Mise en œuvre C# (P1, comportement INCHANGÉ)
`Vocabulary`/`Units`/`EngineCulture` cessent de hardcoder : ils **chargent** `data/engine/`
(EmbeddedResource) et construisent les structures existantes ; les lambdas de rendu
deviennent (a) templates data ou (b) hooks nommés. **Garde-fou : `dotnet test` → 434/434.**

## Tradeoff & alternatives écartées (cadrées en plan mode)

- **CLI/daemon .NET bundlé** : un seul moteur, mais runtime .NET cross-OS lourd dans le
  `.oxt` / dépendance fragile. Écarté au profit du port.
- **Port Python sans vocab universel** : 2e moteur qui duplique 280 l. de données →
  drift fort. Écarté : on partage la data.
- **WASM standalone (wasmtime)** : un seul moteur, mais tooling incertain (le WASM actuel
  est Blazor/navigateur). Gardé comme piste, pas la voie P1/P2.
- **Tout passer en data (y compris Score)** : l'algo de coût dépend de la structure AST →
  reste du code. Seuls ~32 % sont data.

## Conséquences

- **Nouveau** : `data/engine/` (format), plus tard `engine-python/` + runner conformité.
- **Refacto C#** : `Vocabulary/Units/EngineCulture` data-driven ; `Lexer/Parser/Score/
  LatexRenderer` quasi inchangés (hooks nommés). `fixtures.json` inchangé.
- **CI** : à terme, C# **et** Python sur le même `fixtures.json`.
- **Roadmap** : LibreOffice + portabilité à ajouter à CLAUDE.md / ROADMAP (n'y sont pas).
- **Risque** : refactor délicat — atténué par les 434 fixtures comme garde-fou strict, et
  par une migration data-driven incrémentale.

## Validation

P1 : `dotnet test engine/...` → **434/434** inchangé après bascule data-driven (aucune
régression de comportement). Le format est ensuite figé pour le port Python (P2).
