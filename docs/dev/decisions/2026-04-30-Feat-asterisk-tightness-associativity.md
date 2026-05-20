# Feat — Associativité de `*` pilotée par sa tightness

**Date :** 2026-04-30
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

L'associativité de l'opérateur `*` explicite dépend de sa **tightness**
(adjacence aux opérandes) :

- **`*` tight** (collé des deux côtés) → **gauche-associatif** (PEMDAS standard).
  Comportement math classique, le `*` se comporte comme un opérateur de même
  précédence que `/`. Ex : `a*b/3` → `\frac{a \cdot b}{3}`.
- **`*` loose** (espace d'au moins un côté) → **droite-récursive**. Le `*`
  agit comme séparateur typographique entre unités. Ex : `a *b/3` →
  `a \cdot \frac{b}{3}`.

L'inverse est exposé en **alt désambig** (cascade `RuleTightChainExtension`,
même mécanisme que pour le tight chain extension) — l'élève peut switcher
rapidement vers l'autre groupement si le défaut ne lui convient pas.

| Source | Tightness `*` | Default | Alt désambig |
|--------|---------------|---------|--------------|
| `1/2*3/4` | tight | `\frac{(1/2)\cdot 3}{4}` | `\frac{1}{2} \cdot \frac{3}{4}` |
| `1/2 * 3/4` | loose | `\frac{1}{2} \cdot \frac{3}{4}` | `\frac{(1/2)\cdot 3}{4}` |
| `a*b/3` | tight | `\frac{a\cdot b}{3}` | `a \cdot \frac{b}{3}` |
| `a *b/3` | loose | `a \cdot \frac{b}{3}` | `\frac{a\cdot b}{3}` |
| `a* b/3` | loose | `a \cdot \frac{b}{3}` | `\frac{a\cdot b}{3}` |
| `2x/3` | mult **implicite** | `\frac{2x}{3}` (inchangé) | — |
| `cos(x)*sin(x)/3` | tight | `\frac{\cos(x)\cdot \sin(x)}{3}` | `\cos(x) \cdot \frac{\sin(x)}{3}` |

## Pourquoi

### Le problème observé

Quand l'utilisateur tape `1/2*3/4` au clavier en sténo, deux interprétations
typographiques coexistent :

1. **Math classique (PEMDAS)** : `*` et `/` sont au même niveau,
   gauche-assoc. Donc `((1/2)*3)/4` → fraction imbriquée
   `\frac{\frac{1}{2}\cdot 3}{4}`.
2. **Convention sténo / typographie** : deux fractions multipliées restent
   visuellement séparées, on n'imbrique pas. `(1/2)*(3/4)` →
   `\frac{1}{2}\cdot\frac{3}{4}`.

Mathématiquement les deux sont équivalents (`= 3/8`). Mais visuellement
différents. Le user a constaté que le rendu actuel (PEMDAS imbriqué) ne
correspond pas à ce qu'on écrit "à la main".

### La règle de la tightness comme cue

L'utilisateur peut indiquer son intention typographique par un espace :

- **Pas d'espace** (`1/2*3/4`) → "je veux une expression compacte, applique
  la précédence math standard". Default PEMDAS.
- **Avec espace** (`1/2 * 3/4`) → "je veux que les fractions restent des
  unités séparées". Default droite-récursif.

Cette règle est cohérente avec le reste du projet :
- `/` tight absorbe la chaîne implicite (`AB/BC` groupe), `/` loose pas.
- `^a+b` collé groupe (en alt désambig), `^a +b` non.
- `*` tight respecte PEMDAS, `*` loose sépare.

Le pattern général : **tight = unité compacte, loose = séparateur**.

### Pourquoi le flip en cascade

Comme pour le tight chain extension du même jour, l'utilisateur peut se
tromper de tightness ou changer d'avis. Le flip systématique en alt
désambig permet de switcher en un clic sans re-saisir. Cohérent avec la
demande utilisateur : *"je veux bien garder la regle tight en
desambiguisation comme ca on change vite si soucis"*.

### Mult implicite non concernée

La multiplication implicite (juxtaposition `2x`, `AB`) reste **gauche-assoc
tight** comme avant. C'est une convention math forte (l'identifiant
multi-lettre est une unité), distincte de l'opération binaire `*`. Pas
d'ambig à ce niveau.

## Conséquences

### Code (couche 1 — core)

- **`Parser.cs`** :
  - Nouvelle propriété `FlipAsteriskAssociativity` (default `false`).
    Mode `true` réservé à `AlternativeGenerator` pour générer l'alt.
  - `ParseTerm` : la branche unique `IsOp("*", "/")` est divisée en deux.
    - `IsOp("/")` : comportement existant (chaîne implicite tight, etc.).
    - `IsOp("*")` : nouvelle logique tightness-based.
      - `useLeftAssoc = FlipAsteriskAssociativity ? !tight : tight`
      - Si `useLeftAssoc`, rhs = `ParsePostfix` puis continuer la boucle.
      - Sinon, rhs = `ParseTerm` (récursif), retourner immédiatement.
  - Mult implicite (branche `CanStartFactor`) : aucun changement.

- **`AlternativeGenerator.cs`** :
  - `ScanTightChainExtension` étendue pour tester les 3 combinaisons
    non-default des flags `(TightExtendsToOps, FlipAsteriskAssociativity)`.
    Toute alt distincte est ajoutée à la cascade.
  - Helper privé `TryReparse(source, extend, flip)` factorise la
    re-construction parser+render.
  - Le `RuleId` reste `RuleTightChainExtension` (inchangé) — la cascade
    rassemble les variantes de groupement sous une même étiquette UX.

### Tests

- **`LatexRendererTests`** (couche a) : 8 nouveaux tests couvrant tight/
  loose/mixed pour `a*b/3`, `1/2*3/4`, `1*2*3`, `2x/3` (anti-régression
  mult implicite).
- **`AlternativeGeneratorTests`** (couche b) : 4 nouveaux tests sur
  `1/2*3/4`, `1/2 * 3/4`, `2*b/3`, `a *b/3` vérifiant que le flip alt
  est proposé.

### Cas connus

- `a*b/3` (tight, lettre*lettre) : déclenche aussi `RuleVecDotProduct`
  (priorité 3 vs 4 pour tight-chain-extension). Vec-dot-product gagne et
  expose `\vec{a}\cdot \vec{b}` ; le flip d'associativité reste accessible
  une fois cette ambig résolue. UX acceptable, pas de blocage. Test xUnit
  utilise `2*b/3` pour cibler spécifiquement le flip.

### Hors scope V1

- ❌ Cibler la sous-expression précise quand plusieurs flips coexistent.
  V1 expose le re-parse global ; V2 isolera les sous-trees concernés.
- ❌ Étendre la règle aux opérateurs `+ -`. Ces ops sont à un niveau Expr
  (plus bas que Term), leur précédence est claire pour l'élève.
- ❌ Étendre à la mult implicite. La juxtaposition est une unité forte par
  convention typographique, pas un opérateur arithmétique au sens strict.

## Validé par l'utilisateur

Constat initial sur le rendu :

> "On a 1/2*3/4 [image popup] la plupart du temps ca se rend comme
> 1/2 * 3/4 ca non ?"

Précision sur la règle de la tightness :

> "je pense que a*b/3 par defaut fais du (a*b)/3 et en desambig a*(b/3) et
> si possible a* b/3 ou a *b/3 fais du a*(b/3) par defaut et l'autre en
> desambig"

Autorisation de coder :

> "oui tente ca"

## Statut

acté
