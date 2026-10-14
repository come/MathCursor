# Feat — Indice / exposant tapés avec la mise en forme Word

**Date :** 2026-10-14
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-23-Refactor-zonespan-popup-commit-coords](2026-05-23-Refactor-zonespan-popup-commit-coords.md) (séparation `Text` / `TextForEngine`)

## Citation acté

> « un testeur me dit qu'il utilise bcp le bouton indice / exposant de word.. il faudrait voir comment on s'adapte pour prendre en compte quand il tape : f puis indice puis 2+1 puis re indice.. et idem exposant.. la ca fait tout foirer dans la popup » puis « ok on peut tenter ca pour voir.. ca rajoute un truc c'est pas trop cra cra ? » — utilisateur, 2026-10-14

## Contexte

Les boutons Indice / Exposant de Word (Ctrl+= / Ctrl+Maj+=) ne changent pas
le texte, seulement la **mise en forme** (`Font.Subscript/Superscript`).
MathCursor lisait le texte brut : `f` + indice `2+1` arrivait au moteur comme
`f2+1` → `f^{2}+1` en **auto**. L'information « indice » était perdue.

Traduire la mise en forme en sténo (`_(…)` / `^(…)`) a été mesuré et
**écarté** : la sténo est ambiguë et le moteur re-devine mal ce que
l'utilisateur a rendu exact (`x_(1)^(2)` → `x_{(1)^{(2)}}` en tête,
`f_(n+1)(x)` → `f_{(n+1)(x)}`, `u_(n)=2u_(n-1)+1` → indice qui déborde).

POC ruban (2026-10-14, Word réel) : `Range.Font.Subscript` renvoie 9999999
(mixte) en ~1 ms → fast path suffisant pour le cas courant sans mise en
forme ; lecture caractère par caractère ~0,25 ms/caractère, alignée sur
`Range.Text`.

## Décision

**Contrat moteur `ScriptMarkers`** (C# `engine/` + Rust `mc-engine`, défini
une seule fois de chaque côté) : 4 caractères Unicode d'usage privé délimitent
un indice exact (U+E000 … U+E001) et un exposant exact (U+E002 … U+E003).

- **Lexer** : un groupe marqué est analysé à part (contenu → meilleure lecture
  du moteur), puis émis comme **un seul atome** précédé d'un infixe `_` / `^`
  **sticky** (même mécanique que `x2` → x², ne prend qu'un token à droite).
  Le groupe ne peut donc s'attacher qu'à son contenu exact : x + ₁ + ² →
  `x_{1}^{2}`, f + ₙ₊₁ + (x) → `f_{n+1}(x)`, sans lecture concurrente.
- **Robustesse** : marqueur orphelin ou mal apparié ignoré, groupe vide ignoré,
  groupe non fermé = jusqu'à la fin. Les marqueurs ne sortent jamais dans le
  LaTeX.
- **Fixtures** : cas avec marqueurs dans `fixtures.json` → parité C#/Rust
  verrouillée par les gates existants.

**Adapter VSTO** : au moment d'analyser une zone (`AnalyzeAndShow`), lecture
de la mise en forme sur la **plage de la zone** uniquement (une zone ne
contient jamais d'OMath → alignement caractère = string-pos). Fast path
`Font.Subscript/Superscript == 0` → rien à faire. Sinon lecture par
caractère et insertion des marqueurs par une fonction pure testée
(`ScriptMarkup`). Les marqueurs n'existent que dans le texte envoyé au
moteur : positions, suppression et insertion Word inchangées.

## Tradeoff & alternatives écartées

- **Traduction en sténo `_(…)` / `^(…)`** : mesurée, ambiguë (cf. contexte).
- **API à plages (`Analyze(texte, plagesIndice)`)** : plus élégante sur le
  papier, mais le texte moteur est découpé/décalé à plusieurs endroits (préfixe
  de chaîne retiré, extension de zone, protocole stdio Rust) → recalcul
  d'offsets à chaque étape, famille de bugs déjà payée (ADR ZoneSpan). Les
  marqueurs suivent le texte sans recalcul.
- **Lire toute la mise en forme du ¶** : inutile et plus fragile (wrappers
  OMath) ; la zone suffit.

La branche système `{ … }` (qui lit le ¶ entier) passe par la même lecture
(`EngineInputWithScripts(sysZone)`), retour testeur « { f exposant 2 + 1 ».

Limites assumées : la sténo stockée pour le revert reste le texte brut (un revert
rend `f2+1` sans la mise en forme) ; contenu d'un groupe ambigu → seule sa
meilleure lecture est gardée.

## Conséquences

- **Code touché** : `engine/…/ScriptMarkers.cs` (nouveau) + `Lexer.cs`,
  `rust/mc-engine/src/lexer.rs` (+ constantes), `fixtures.json`,
  adapter `Host/Detection/ScriptMarkup.cs` (pur, nouveau),
  `Host/Detection/ScriptFormatReader.cs` (interop, nouveau),
  `ConversionController.AnalyzeAndShow`, les deux csproj. POC ruban supprimé.
- **API publique moteur** : ajout de `ScriptMarkers` (additif).
- Aucune touche à l'insertion OMath / CC / positions Word.

## Validation post-fix

`scripts/run-tests.ps1` vert (C# + conformance Rust) ; en Word : f + indice
`2+1` → popup `f_{2+1}` ; x + indice 1 + exposant 2 ; uₙ₊₁ = 2uₙ + 1 ; une
ligne sans mise en forme inchangée.
