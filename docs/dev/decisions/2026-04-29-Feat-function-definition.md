# Feat — Définition de fonction au clavier : `f:x->expr` → `f : x ↦ expr`

**Date :** 2026-04-29
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Reconnaître la **notation lycée FR de définition de fonction** comme un
pattern lattice dédié :

| Source | Sémantique | Rendu LaTeX |
|--------|------------|-------------|
| `f:x->2x+1` | définition d'une fonction d'une variable | `f : x \mapsto 2x+1` |
| `f:x,y->x+y` | définition d'une fonction de deux variables | `f : (x,y) \mapsto x+y` |
| `g:t->cos(t)+1` | idem (avec fonction nommée à droite) | `g : t \mapsto \cos(t)+1` |

Trois règles :

1. **Trigger strict** : la règle s'active **seulement** quand on voit un
   `Ident` suivi de `:` (Op) suivi d'au moins un `Ident` (var), suivi (après
   d'éventuelles `, Ident`) d'un `->`. Sans `->`, pas de transformation.
2. **Multi-vars implique parens automatiques** dans le rendu (`(x,y)`),
   parce qu'une fonction de plusieurs variables prend un n-uplet.
3. **Body absorbe la chaîne tight à droite** (TightChain), borné par le
   premier espace top-level — donc `f:x->2x+1 et g:y->y` sur une ligne
   marche par juxtaposition.

Nouveau nœud AST : `FuncDef(Name, Vars, Body)` où `Vars` est une liste de
nœuds (typiquement `Atom`).

## Pourquoi

### Ergo

Notation directe de la convention lycée : `f : x ↦ 2x+1`. C'est la première
chose qu'un élève écrit quand on lui demande "définissez la fonction `f` qui
à `x` associe...". Forcer une saisie alternative (`f(x) = 2x+1`) marche mais
manque la sémantique "définition" (vs simple égalité). Avec `\mapsto`, le
rendu rappelle visuellement qu'il s'agit d'une **construction** de fonction.

### Choix `\mapsto` plutôt que `f(x) = ...`

L'utilisateur a explicitement choisi `\mapsto` (citation : "avec le sto").
Conforme à la convention française programmes lycée. `f(x) = expr` est
correct mais présente une **égalité de valeurs**, pas une **définition de
fonction**. La distinction est subtile mais voulue par les profs.

### Choix `:` comme déclencheur

`:` n'a aujourd'hui **aucun usage** en math lycée hors typage (`f : R → R`),
qu'on n'adresse pas en V1. Pas de risque de collision avec une autre
notation. Et sur clavier AZERTY, `:` est sur la touche `.`, donc accessible
sans Shift (à droite directement).

### Pourquoi pas reconnaître aussi `f : R -> R`

Le typage de fonction (`f : R -> R, x -> 2x`) est la forme complète. Hors
scope V1 :

- ambigu avec la définition simple si on n'a qu'une partie ;
- demande de gérer R, N, Z comme atomes-ensembles, ce qui est traité dans
  l'ADR canonical-sets sœur ;
- les lycéens écrivent rarement le typage en pratique, ils donnent juste
  l'expression.

Si l'usage révèle le besoin, on étend dans une V2.

### Pourquoi nouveau nœud AST plutôt que `Bin(":", lhs, rhs)`

`FuncDef` porte trois infos distinctes (nom, vars, body), pas deux. Stocker
ça dans un `Bin` exigerait d'encoder vars+body dans un seul rhs — fragile.
Un nœud dédié rend le renderer trivial et l'inspection AST claire.

### TightChain pour le body

Cohérent avec l'ADR `tight-as-grouping` du même jour. Le body est l'opérande
droit de `->` au sens lecture sténo : tout ce qui est collé après lui
appartient à la définition. L'utilisateur borne par un espace.

## Conséquences

### Code (couche 1 — core)

- **Vocabulary.cs** : ajouter `:` dans `SingleOps` (devient
  `"+-*/^_=<>()[]{},|;:"`).
- **AstNodes.cs** : ajouter `FuncDef(string Name, IReadOnlyList<AstNode>
  Vars, AstNode Body)`.
- **Parser.cs `Parse()`** : avant d'appeler `ParseRelation()`, détecter le
  pattern `Ident ':' Ident (',' Ident)* '->' …`. Si match → consume et
  produit `FuncDef`. Sinon fallback `ParseRelation` comme aujourd'hui.
- **Parser.cs body** : `ParseTightChain()` (cohérent avec ADR
  tight-as-grouping).
- **LatexRenderer.cs** : ajouter une branche `FuncDef`. Une seule var → `{Name}
  : {var} \mapsto {body}`. Plusieurs vars → `{Name} : ({v1},{v2},…) \mapsto
  {body}`.

### Tests

ParserTests :
- `f:x->2x+1` → `FuncDef("f", [Atom("x")], Bin("+", Bin("*",2,x,implicit,tight), 1, tight))`.
- `f:x,y->x+y` → `FuncDef("f", [Atom("x"), Atom("y")], Bin("+", x, y, tight))`.
- `f:x` (pas de `->`) → fallback : `Bin("?")` ou parsing standard. Vérifier
  qu'aucune `FuncDef` n'est produite. Recommandation : dans le pattern
  matcher, si on n'a pas trouvé `->`, on **rewind** sur `_i` et on laisse
  `ParseRelation` traiter normalement.
- `f:x->2x+1 g:y->y` (deux def séparées par espace) → liste de FuncDef
  ? Hors scope du parser actuel (qui produit un seul AST root). Comportement
  acceptable : la première FuncDef est produite, le `g:y->y` qui reste tombe
  hors du parsing top-level (rendu en string brut ou en seconde passe par le
  caller). À documenter, pas à hardcoder.

LatexRendererTests :
- `f:x->2x+1` → `f : x \mapsto 2x+1`.
- `f:x,y->x+y` → `f : (x,y) \mapsto x+y`.

### Hors scope

- Typage de fonction (`f : R -> R, x -> expr`).
- Définition par cas (`f(x) = { x si x>0 ; -x sinon }`).
- Composition de fonctions (`g ∘ f`).
- Notation `:=` à la place de `:->` (variante recherche).
- Plusieurs définitions par ligne (limité par le contrat "un AST root par
  parse" du moteur lattice actuel).

## Validé par l'utilisateur

Demande initiale :

> "petit brief en passant pour reconnaitre automatiquement la forme :
> f:x->expr ou f:x,y->expr tu vois le truc ?"

Choix du rendu :

> "avec le sto"

(Réponse à la question rendu : `\mapsto` confirmé, multi-args avec parens
auto présupposé OK, cas `f:x` seul présupposé "pas de transformation". À
amender si l'usage diverge.)

## Statut

acté
