# Feat — Juxtaposition tight = groupement implicite pour `/`, `^`, `_`

**Date :** 2026-04-29
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

En sténo clavier, **l'absence d'espace est interprétée comme un groupement
implicite** autour des opérateurs structurels `/`, `^` et `_`. Concrètement :
quand l'opérateur est tight (collé sans espace), son opérande **droit**
absorbe toute la chaîne tight qui suit, jusqu'au premier espace.

| Source | AST (intuition) | Rendu LaTeX |
|--------|-----------------|-------------|
| `AB/BC` | `(AB) / (BC)` | `\frac{AB}{BC}` |
| `1/x+1` (collé) | `1 / (x+1)` | `\frac{1}{x+1}` |
| `1/x +1` (espace avant `+`) | `(1/x) + 1` | `\frac{1}{x} + 1` |
| `A/B/C` (collé) | `A / (B/C)` | `\frac{A}{\frac{B}{C}}` |
| `cos(x)/sin(x)` | `cos(x) / sin(x)` (groupes explicites) | `\frac{\cos(x)}{\sin(x)}` |
| `x^a+b` (collé) | `x^(a+b)` | `x^{a+b}` |
| `u_n+1` (collé) | `u_(n+1)` | `u_{n+1}` |
| `u_n +1` (espace) | `(u_n) + 1` | `u_{n} + 1` |

Le côté gauche n'a pas besoin de traitement spécial : la mult implicite
gauche-associative produit déjà naturellement `(AB)` comme lhs avant de voir le
`/`.

**État actuel du code** :

- `^` et `_` : la règle est **déjà appliquée**, ils partagent le code path
  `ParsePostfix → ParseArgument → ParseTightChain` (Parser.cs:249-254). Cet
  ADR confirme l'invariant et le documente explicitement, mais aucun code
  à modifier pour ces deux-là.
- `/` : la règle est **à ajouter**. Aujourd'hui `/` consume seulement un
  `Postfix` à droite, pas un `TightChain`. C'est ce que cette ADR change.

**Périmètre limité à `/`, `^`, `_`** dans cette V1. Pour `+ - *`, pas de
sémantique de groupement par juxtaposition : la précédence math standard
reste celle qu'on utilise. Si l'usage révèle un besoin, on étendra plus tard.

## Pourquoi

### Intuition sténo

Quand un lycéen tape `AB/BC` au clavier, l'intention quasi-systématique est
"fraction de deux blocs collés", pas `((A·B)/B)·C`. Forcer des parenthèses
(`(AB)/(BC)`) casse la fluidité — c'est exactement ce que MathCursor doit
éviter (cf. brief ergo : "comportement prévisible et sans friction"). La règle
est mémorisable sans effort : **collé = groupé, espacé = math standard**. Si
l'utilisateur veut briser le groupement, il met un espace.

### Pourquoi `/`, `^`, `_` seulement

Ce sont les trois opérateurs qui matérialisent un **rendu structurel** (barre
de fraction, exposant en haut, indice en bas), donc le groupement est
visuellement évident pour l'utilisateur. Pour `+ - *`, le rendu reste
linéaire et la précédence math classique suffit ; introduire une règle "tight
= groupé" ferait diverger de la convention que l'utilisateur connaît déjà
(PEMDAS / hiérarchie standard) sans gain visible.

### Pourquoi rhs absorbe tout (pas juste un atome)

Choix utilisateur explicite (cf. validation) : *"toute la chaîne collée
passe... si jamais il voit que ça lui va pas → il rajoute l'espace"*. Règle
simple et auto-correctrice : un seul caractère d'espace suffit à briser un
groupement non voulu. Pas besoin de spec compliquée sur "où s'arrête le
groupe".

### Conséquence sur l'associativité de `/`

Avec rhs greedy, `A/B/C` collé devient droit-associatif : `A/(B/C)`. C'est
contre-intuitif vis-à-vis de la convention math (gauche-associative), **mais
cohérent avec la règle uniforme**. Si l'utilisateur veut gauche-associatif, il
écrit `A/B /C` (espace avant le second `/`) ou utilise des parens. On accepte
ce léger écart parce que `A/B/C` est de toute façon une notation rare et
ambiguë que tout style guide recommande de parenthéser explicitement.

### Cas asymétrique (mult implicite côté gauche du `/`)

Pour `a 2x/y` (espace avant `2`, collé après `x`) : le lhs du `/` est
`a·2·x` à cause de l'associativité gauche de la mult implicite, pas
seulement `2x`. Sémantiquement on aimerait `a · (2x/y)`, mais le parser
descendant a déjà aspiré `a·2·x` quand il voit le `/`. Pour corriger
proprement il faudrait stratifier `Term` en `LooseTerm` / `TightTerm`,
changement plus invasif. **Hors scope V1** : on accepte que les cas
asymétriques exigent un espace autour du `/` ou des parens. À ré-évaluer si
ça gêne à l'usage.

## Conséquences

### Code (couche 1 — core)

- **Parser.cs ParseTerm** : quand l'opérateur consommé est `/` ET
  `op.Tight == true`, le rhs passe par `ParseTightChain` au lieu de
  `ParsePostfix`. Sinon comportement actuel inchangé.
- **Parser.cs ParsePostfix** : aucun changement pour `^` / `_`. Ils
  délèguent déjà à `ParseArgument → ParseTightChain`. L'ADR sert de
  documentation de l'invariant : si quelqu'un refactore le code-path de
  `^`/`_`, il doit préserver cette propriété.
- **LatexRenderer.cs** : aucun changement. `RenderBin` pour `/` rend déjà
  `\frac{lhs}{rhs}` qui groupe naturellement. Idem `Sup` (`^{...}`) et
  `Sub` (`_{...}`).

### Tests

À ajouter dans `ParserTests` :

- `AB/BC` → `Bin("/", Bin("*", A, B), Bin("*", B, C))` — vérifier que rhs est
  bien la mult implicite `B*C`, pas juste `B`.
- `1/x+1` collé → `Bin("/", 1, Bin("+", x, 1, tight=true))`.
- `1/x +1` (espace) → `Bin("+", Bin("/", 1, x), 1)` — non-régression.
- `A/B/C` collé → droit-assoc : `Bin("/", A, Bin("/", B, C))`.
- `cos(x)/sin(x)` → groupes intacts, `Bin("/", Func(cos, x), Func(sin, x))`.
- `1/x` simple → `Bin("/", 1, x)` — non-régression.
- `u_n+1` collé → `Sub(u, Bin("+", n, 1, tight=true))` (déjà passant, ajouté
  pour pinner l'invariant).
- `u_n +1` espace → `Bin("+", Sub(u, n), 1)` — non-régression.
- `x^a+b` collé → `Sup(x, Bin("+", a, b, tight=true))` (déjà passant, idem).

À ajouter dans `LatexRendererTests` :

- `AB/BC` → `\frac{AB}{BC}`.
- `1/x+1` collé → `\frac{1}{x+1}`.
- `1/x +1` (espace) → `\frac{1}{x}+1` (ou `\frac{1}{x} + 1` selon convention).
- `u_n+1` collé → `u_{n+1}`.
- `x^a+b` collé → `x^{a+b}`.

### Hors scope V1

- Stratification `Term` en `LooseTerm`/`TightTerm` (cas asymétrique
  `a 2x/y`).
- Extension de la règle à `+ - *` : non, on garde la précédence math
  standard pour ces opérateurs.

## Validé par l'utilisateur

Direction initiale :

> "y'a un truc que j'aimerai reflechir sur le tight en steno, il a de
> l'importance par exemple AB/BC est aujourd'hui reconnu comme AB/B \* C
> je pense qu'on devrait pouvoir planifier une regle assez meta c'est que
> la liaison sans espace pourrait quasi etre un groupement .. 1/x+1 =>
> 1/(x+1) <> 1/x +1 mathématiques alors qu'en steno mode ca a du sens de
> faire sauter la lourdeur de la parenthese.. tu vois le truc ?"

Précisions sur scope et associativité :

> "je pense que toute la chaine collée passe.. si jamais il voit que ca
> lui va pas => il rajoute l'espace .. pour 1. oui juste / et ^ pour
> l'instant"

Validation du plan :

> "vas y redige + un brief complet dans docs"

Confirmation iso `_` :

> "et d'ailleurs on est iso avec _ les indices ?"

(Réponse : oui, déjà iso aujourd'hui — `^` et `_` partagent le même code
path qui passe par `TightChain`. ADR enrichi pour rendre l'invariant
explicite.)

## Statut

acté
