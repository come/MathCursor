# Feat — Formes courtes des n-aires : variantes d'arité par entrée vocab

**Date :** 2026-06-11
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-tutorial-fixtures-driven.md](2026-06-10-Feat-tutorial-fixtures-driven.md) (les fixtures squelette `sum`/`int` sont des consignes du tuto, à préserver à l'identique)

## Citation acté

> Périmètre « + lim 1-aire aussi » choisi au cadrage plan, plan approuvé — utilisateur, 2026-06-11.
> (Demande d'origine : « en math sum par exemple peut avoir un peu moins de param, on peut vouloir ecrire sum k f(k) simplement »)

## Contexte

Les opérateurs n-aires ont une arité **fixe** dans `Vocabulary.cs` (`sum`=4, `prod`=4, `int`=4, `iint`=5, `iiint`=6, `lim`=3). Or l'usage scolaire emploie couramment des formes à moins de paramètres : `\sum_{k} f(k)`, `\int f(x)\,dx` (intégrale indéfinie), `\lim u_n` (suites, variable implicite). Aujourd'hui `sum k f(k)` rend `\sum_{k=f(k)}^{\square} \square` — contresens mathématique.

Découverte structurelle qui rend la feature sûre : seule la règle de jonction `num→unit` est `CrossSpace` (Lexer.cs:37) — **deux unités séparées par un espace ne reçoivent jamais d'infixe implicite**, donc un span comme `1 n f(k)` est imparsable. Pour U unités après le n-aire, une arité K ne parse que si U == K (complet) ou U < K (trous en fin) : **la forme pleine sans trou et la forme courte sans trou ne coexistent jamais**. `sum k 1 n f(k)` reste un auto net sans aucun arbitrage nouveau.

**Angle mort découvert à l'implémentation** : un opérateur EXPLICITE soude les unités — dans `lim x +inf`, le `+` rend `x+\infty` parsable d'un bloc, et le parse `(\lim x)+\infty` (lim court en opérande, coût 0) battait le squelette `\lim_{x\to+\infty} \square` (1 trou, coût 3). Deux règles complémentaires en découlent (cf. Décision) : frontière de frappe + guard `TightBody`.

## Décision

### Variantes d'arité par entrée vocab

`VocabEntry` gagne `Variants` (liste de `NaryVariant { Arity, Render, Accept }`), additif : `Arity`/`Render` restent l'arité canonique (max). Périmètre v1 :

| Op | Variante courte | Render | Guard `Accept` |
|---|---|---|---|
| `sum` / `prod` | 2 | `\sum_{a0} a1` | corps non numérique |
| `int` | 2 | `\int a0 \, da1` | différentiel = atome-nom |
| `iint` / `iiint` | 3 / 4 | idem avec 2/3 différentiels | différentiels = atomes-noms |
| `lim` | 1 | `\lim a0` | corps non numérique |

`root`/`dot`/`binom` inchangés (arité 2 déjà minimale).

### Règle no-hole pour les variantes courtes

Seule l'arité canonique a droit au comblement par trous dans `Splits` (`allowHoles=false` pour les variantes). Sans cette règle, `sum` seul rendrait `\sum_{\square} \square` (6) au lieu du squelette complet (12) — les fixtures squelette et le tuto seraient cassés.

### Règle frontière de frappe

Les variantes courtes ne sont générées que si le span du n-aire atteint la frontière de frappe (`j >= _end` dans Parser.cs, même frontière que la règle « trou droite » des infixes). Sinon `lim x +inf` lirait `(\lim x)+\infty` au lieu du squelette : le `\lim x` court à coût 0 servirait d'opérande bon marché à n'importe quel infixe englobant. Dès qu'on tape derrière les args, le squelette plein reprend la main ; `1 + sum k f(k)` reste couvert (le n-aire y finit l'entrée), et les segments coupés aux Cut (`lim u_n = l`) aussi (chaque segment a sa propre frontière).

### Rejet du biais de score SHORT_NARY

Aucun changement dans `Score.cs`. Le cas d'usage d'un biais (plein 0-trou vs court 0-trou) est structurellement impossible (cf. Contexte) ; les seules coexistences réelles sont « court 0 trou vs plein ≥ 2 trous », écart 6 ≫ PopupGap=2. Un biais violerait en outre la doctrine de Score.cs (ne lit que des features, jamais un opérateur).

### Guards déclaratifs dans Vocabulary

Pour les transitoires de frappe : sans guard, `int 0 1` rendrait `\int 0\,d1` (coût 0) au lieu du squelette borné (6), et `sum k 1` rendrait `\sum_{k} 1` en route vers `sum k 1 n f(k)`. Les guards (différentiels = atomes-noms, corps ≠ atome numérique) vivent dans `Vocabulary.cs`, seul fichier autorisé à connaître les opérateurs. Flicker résiduel assumé et documenté par fixture : `sum k n` → `\sum_{k} n` (c'est la forme même de la feature).

Guard supplémentaire `TightBody` sur `lim` 1-aire : le corps doit lier plus serré que le quantificateur (pas d'infixe de looseness ≥ QUANT au sommet) — `\lim x+1` s'afficherait comme `(\lim x)+1` alors que la structure serait `\lim(x+1)`, contresens d'affichage. `\lim u_n`, `\lim \frac{1}{n}`, `\lim f(x)` passent.

## Tradeoff & alternatives écartées

- **Render tolérant aux trous (arité fixe, dégradation au rendu)** : `Splits` comble en FIN de span, donc `sum k f(k)` donne `[k, f(k), □, □]` — le corps serait un trou, pas les bornes. Placer les trous au milieu = explosion combinatoire. Rejeté.
- **Biais de score SHORT_NARY (~1/arg omis)** : inutile structurellement (cf. Décision), et casserait la généricité de Score.cs. Rejeté par l'analyse chiffrée.
- **Séparateurs explicites (`sum k: f(k)`)** : friction de frappe contraire à l'objectif produit (lycéens PAP, fluidité). Rejeté.
- **Variante `lim` initialement écartée** (fixtures lim sensibles, dont un popup) puis **retenue par l'utilisateur** : la règle no-hole la rend sans risque — le 1-aire ne parse jamais 3 unités espacées, `lim n 0 1/n` reste popup à l'identique.

## Conséquences

- **Code touché** (L1 moteur uniquement) :
  - `engine/src/MathCursor.Engine/Vocabulary.cs` — type `NaryVariant`, `VocabEntry.Variants` + `RenderFor(argc)`, overload factory `Nary`, guards (`Numeric`/`NameAtom`/`TightBody`), déclarations des 6 variantes.
  - `engine/src/MathCursor.Engine/Parser.cs` — branche nary : boucle variantes gated `j >= _end`, `Splits(..., allowHoles)`.
  - `engine/src/MathCursor.Engine/LatexRenderer.cs` — dispatch nary par `Parts.Count`.
  - `Score.cs` : **zéro changement**.
- **Tests** : les 368 fixtures existantes et `TutorialSpecTests` passent **sans modification** ; +10 fixtures (formes courtes, verrous de guards `sum k 1`/`int 0 1`, transitoire `sum k n`) → corpus 378, tout vert.
- **API publique** : aucune (tout interne au moteur).
- **Adapter** : `OmmlToOMathBuilder` gère déjà `HideSub`/`HideSup` pour `m:nary` — 233 tests adapter verts (pipelines OMML/popup).

## Validation post-fix

1. Suite moteur verte AVANT ajout de fixtures (368 inchangées — fait), puis corpus étendu 378 vert (fait).
2. Chaînes exactes des nouvelles fixtures figées en exécutant le moteur (espaces traillants `\square `, double espace avant `\, d`) — fait.
3. Tests adapter verts (233/233 — fait) ; reste : vérification visuelle dans Word de `sum k f(k)`, `int f(x) x`, `lim u_n`.

## Hors scope (follow-ups identifiés)

- `sum k in A f(k)` → `\sum_{k\in A} f(k)` : le parse est généré mais tué par `Score.CrossesCut` (le `in` Cut non groupé sous un STRONG). Exigerait une exemption ciblée — à traiter séparément si demande.
- `sum k f(k) + 1` sans parenthèses : la forme courte n'y est pas disponible (pas en frontière) ; comportement identique à avant la feature.
