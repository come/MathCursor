# Feat — Mots-clés partiels par préfixe (alias auto-générés)

**Date :** 2026-06-18 (pivot d'implémentation 2026-06-19)
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** `docs/dev/engine-backlog.md` (item #2), [2026-06-16-Feat-portable-engine-universal-vocab.md](2026-06-16-Feat-portable-engine-universal-vocab.md)

## Citation acté

> « 2, le plus costaud en effet » puis, après une première implémentation jugée
> trop lourde : « est ce qu'on n'aurait pas pu utiliser le système d'alias plutôt
> mais en autogénérant les alias avec les 3 4 premières lettres ? j'ai
> l'impression qu'on est en train de monter une usine à gaz » → choix
> « Simplifier : alias auto-générés » — utilisateur, 2026-06-18/19

## Contexte

Taper un préfixe d'un mot-clé devrait l'étendre (`appro`→approx, `fora`→forall,
`unio`→union, `arcs`→arcsin…). Beaucoup d'abréviations sont déjà des alias
énumérés (`som`, `rac`, `app`…) ; on veut le **généraliser** sans les énumérer.

## Décision

**Auto-générer les alias de préfixe non ambigus** au chargement, dans
`Vocabulary` (`AddPrefixAliases`), et les fusionner dans les maps d'alias par
culture (`AliasesFr`/`AliasesUs`). **Réutilise intégralement le mécanisme
d'alias existant** (`EngineCulture.Canon` dans le lexer) — **zéro machinerie
dédiée**.

Règle : pour chaque forme « mot » (clé Vocab alphabétique → elle-même, grec
inclus ; + alias alphabétique → cible), chaque préfixe de longueur **≥ 4** qui
(a) n'est **pas** déjà une forme exacte et (b) ne préfixe qu'**UNE seule** cible
canonique → devient un alias vers cette cible.

- **≥ 4 lettres** : à 3, trop de mots/variables courants seraient capturés
  (`for`, `per`, `uni`…) ; à 4 l'intention est nette (`fora`, `unio`, `appro`).
- **Ambigu** (`arc`→arcsin/arccos/arctan, `sub`→subset/subseteq) : **non
  généré** → l'utilisateur tape une lettre de plus (`arcs`, `arcc`, `arct`).
- **Exact-match prioritaire** : un préfixe déjà clé Vocab/alias n'est pas écrasé.

Combiné au fix « relation-mot » (ADR approx) : `appro`→approx se comporte alors
comme `=` (lie, ou début de ligne via `RelationMarkers`).

## Tradeoff & alternatives écartées

- **(ÉCARTÉE, d'abord implémentée puis revertée)** Machinerie « popup
  multi-candidats étiqueté » : substitution de variantes d'entrée dans
  `ForestEngine.Run` + `EngineCandidate.Hint` + plumbing popup, pour offrir
  `arc`→[arcsin|arccos|arctan] avec libellés. **Jugée « usine à gaz » par
  l'utilisateur** pour le gain : la quasi-totalité de la valeur est couverte par
  les alias auto à 4 lettres (l'ambiguïté disparaît presque toujours dès la 4ᵉ
  lettre). Revertée intégralement (ForestEngine/EngineCulture/ConversionController/
  SuggestionPopupWindow remis à l'état pré-#2).
- **Préfixes ≥ 3** : écarté (faux positifs sur mots courants).

## Conséquences

- **Moteur (L1)** : `Vocabulary.AddPrefixAliases` (+ `IsAlphaWord`,
  `MinPrefixLen=4`) ; aucune autre couche touchée (lexer/parser/render/popup
  inchangés). Données (`symbols.json`/`cultures.json`) inchangées — les alias se
  dérivent du Vocab/alias existants. Bénéficie au futur port Python (même logique).
- **Perdu vs la machinerie** : pas de popup pour les préfixes ambigus à 3 lettres
  (`arc` seul → littéral ; taper `arcs`/`arcc`/`arct`).
- **Tests** : fixtures `a appro b`→`\approx`, `a unio b`→`\cup` (corpus 449,
  l'ancienne `a sub b`→popup retirée). Engine 21 / adapter 317 verts, zéro
  régression sur les ~hundreds d'alias générés.

## Validation post-fix

`Analyze("a appro b")`→`a\approx b` ; `("arcs x")`→`\arcsin(x)` ; `("fora x")`→
`\forall x` ; `("for x")`/`("per")`/`("uni")` → littéral (≥4 protège). Corpus
449/449 vert.
