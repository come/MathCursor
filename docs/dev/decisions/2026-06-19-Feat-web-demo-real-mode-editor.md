# Feat — Démo web « mode réel » : éditeur au fil de l'eau (caret-span fidèle produit)

**Date :** 2026-06-19
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [Feat — Démo web ForestEngine WASM](2026-06-15-Feat-web-demo-forest-engine-wasm.md) (base réutilisée) · [Fix — span étendu parenthèse non fermée](2026-06-19-Fix-spancomputer-unclosed-bracket-matrix.md) (logique portée) · `docs/dev/engine-backlog.md` (item « Démo en mode réel »)

## Citation acté

> « plan d'abord et go » puis approbation explicite du plan, avec les choix : caret-span fidèle produit / nouvelle page à côté / pas de ré-édition en v1 — utilisateur, 2026-06-19

## Contexte

La démo web actuelle (`/demo/`, `docs/demo/index.html`) est un champ **input →
output** : on tape une formule, une popup liste les candidats dessous. Elle
montre bien le moteur (`ForestEngine` compilé en WASM, cf. ADR 2026-06-15) mais
ne fait **pas sentir l'ergonomie réelle** du produit Word : on n'écrit pas au fil
de l'eau, la formule ne se détecte pas autour du curseur, rien n'est rendu inline.

Le backlog moteur (`docs/dev/engine-backlog.md`, item « Démo en mode réel »)
demande une démo qui mime le vrai flow : grande zone de texte libre, curseur,
**Entrée = passage à la ligne**, **détection live** au fil de la frappe, **popup
au caret**, **rendu KaTeX inline** de la zone reconnue. Objectif marketing/produit :
faire ressentir « j'écris mon cours, la formule se détecte seule, je choisis, ça
s'insère rendu » plutôt qu'un simple convertisseur.

Contrainte clé : le **modèle NER n'est pas embarqué en WASM** (volontairement
exclu, ADR 2026-06-15). La détection de la zone maths dans du texte libre ne peut
donc pas s'appuyer sur le NER. Mais le **chemin manuel du produit (Ctrl+Espace)**
n'utilise pas le NER non plus : il s'appuie sur `SpanComputer` (parenthèses non
fermées / délimiteurs / stopwords), une logique **pure et autonome** (~200 lignes,
zéro dépendance Word, zéro fichier data). C'est ce chemin qu'on peut porter en JS.

## Décision

Ajouter une **nouvelle page** `/demo/live.html` (à côté de `/demo/`, qui reste
intacte — bascule du CTA primaire plus tard si concluant), construite sur 5
fichiers neufs dans `web-demo/MathCursor.Demo.WebAssembly/wwwroot/` :

### Délimitation = caret-span fidèle produit
Port **strict** de `SpanComputer.cs` en JS (`spancomputer.js`) :
`computeSpanStart` / `computeSpanEnd` + helpers `enclosingOpenBracket`,
`openDepthBehind`, `isWordChar`, et les constantes copiées telles quelles
(`STOPWORDS` 28 mots, `SPAN_DELIMITERS` = `. ; ? = < > \n \r` — **sans `!`**).
On peut écrire `On a donc f(x)=1/x` et seul `f(x)=1/x` est détecté autour du
caret ; le reste demeure du texte.

### Éditeur contenteditable
`live.html` (zone `<div contenteditable>` + popup flottante) + `live.js` :
- **Mapping DOM ↔ string** : `readBlock()` reconstruit `{text, caret, omathRegions}`
  du paragraphe courant ; les équations déjà rendues sont des spans
  `contenteditable=false` représentées par un caractère sentinelle + une région
  (rôle des OMaths Word : bornes dures que la span ne traverse pas).
- **Pipeline live** (debounce ~150 ms sur `input`/`selectionchange`) →
  `computeSpanStart/End` → `Bridge.Analyze(zoneText, culture)` (DTO inchangé).
- **Popup au caret** positionnée au début de la zone détectée
  (`Range.getBoundingClientRect`), candidats rendus comme le produit (★ d'office,
  alternative N, badge décision, note si dense).
- **Clavier produit** : `Tab` valide, `↑/↓` navigue, **`Entrée` = nouvelle ligne**
  (jamais submit), clic valide, `Échap` ferme.
- **Commit** : la zone devient un span KaTeX **figé** (`data-latex`), caret après.

### Pas de ré-édition (v1)
Une zone validée est figée. On continue à taper après. Le mode édition produit
(revert/edit) reste une démo future.

### Parité garantie
`spancomputer.test.js` porte les **13 cas** de `SpanComputerTests.cs`, lançable
`node spancomputer.test.js` (assert simple, pas de framework) — verrou contre
toute dérive du port JS vs le C# validé.

## Tradeoff & alternatives écartées

- **NER en WASM (détection « vraie »)** : le plus fidèle, mais modèle ONNX +
  runtime lourds en navigateur, et explicitement hors-scope depuis l'ADR
  2026-06-15. Le caret-span (chemin Ctrl+Espace) donne déjà la sensation visée
  sans ce poids.
- **Détection ligne entière** (toute la ligne = zone) : modèle simple mais
  **basse fidélité** — une ligne mêlant prose + maths serait analysée en entier
  et le moteur réagirait mal. Le caret-span gère le mix texte/maths, qui est le
  cas d'usage réel (cours de maths).
- **Remplacer `/demo/` directement** : risque sur le CTA primaire du site si un
  bug. Nouvelle page = validation côte-à-côte sans régression possible.
- **Ré-édition des équations en v1** : ajoute un cycle rendu↔source non nécessaire
  pour « sentir le flow ». Reporté.
- **`Entrée` = valider le candidat** : casse l'invariant « Entrée = nouvelle
  ligne » du backlog et de l'éditeur ; on suit la convention produit (`Tab`
  valide, `Entrée` saute).

## Conséquences

- **Code touché** : 5 fichiers **créés** dans
  `web-demo/MathCursor.Demo.WebAssembly/wwwroot/` (`spancomputer.js`,
  `spancomputer.test.js`, `live.html`, `live.js`, `live.css`). **Aucune modif**
  de `Bridge.cs`, `index.html`, `demo.js`, du core (L1), des contrats (L2) ni du
  moteur WASM. `Bridge.Analyze(input, culture)` réutilisé tel quel.
- **Tests** : `spancomputer.test.js` (13 cas, parité avec `SpanComputerTests.cs`).
  Pas de test C# nouveau (rien touché côté C#).
- **API publique** : inchangée.
- **Déploiement** : automatique — `/deploy-prod` fait `dotnet publish` puis mirror
  de tout `web-demo/publish/wwwroot/.` → `docs/demo/` ; les `live.*` partent sans
  modif du skill. Page accessible à `/demo/live.html`.
- **Règles MC impactées** : aucune. Le port JS doit rester synchronisé avec
  `SpanComputer.cs` — `spancomputer.test.js` est le garde-fou.

## Validation post-fix

1. **Parité span** : `node web-demo/MathCursor.Demo.WebAssembly/wwwroot/spancomputer.test.js` → 13/13 verts.
2. **Build WASM** : `dotnet publish … -c Release -o web-demo/publish/` OK,
   `web-demo/publish/wwwroot/live.html` + `_framework/` présents.
3. **Manuel** (servir `web-demo/publish/wwwroot/` local, ouvrir `/live.html`) :
   `On a donc f(x)=1/x` → seul `f(x)=1/x` détecté + popup au caret (`f(x)=\frac{1}{x}`) ;
   `Tab` → rendu KaTeX figé inline ; `Entrée` → nouvelle ligne ; `(a,b;c,d`
   (matrice non fermée) → détectée entière ; toggle FR/US → re-détection live ;
   `/demo/` toujours fonctionnelle.
