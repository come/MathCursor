# Feat — Composition d'intervalles : union (`U`/`union`) et intersection (`inter`)

**Date :** 2026-04-29
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Reconnaître l'union et l'intersection d'intervalles au clavier :

| Source | Sémantique | Rendu |
|--------|------------|-------|
| `[0,1] U [3,5]` | union | `[0,1] \cup [3,5]` |
| `[0,1]U[3,5]` | union (sans espace) | `[0,1] \cup [3,5]` |
| `[0,1] union [3,5]` | union (mot-clé) | `[0,1] \cup [3,5]` |
| `[0,1] inter [0.5,2]` | intersection | `[0,1] \cap [0.5,2]` |
| `[0,1] intersection [0.5,2]` | intersection (mot-clé) | `[0,1] \cap [0.5,2]` |

Les opérations restent au niveau `Bin` (pas de nouveau nœud AST) avec `op =
"union"` / `"inter"` rendus en `\cup` / `\cap`. Le parser détecte ces patterns
au niveau `ParseExpr` (priorité similaire à `+`/`-`).

**`U` entre intervalles = union 100%, sans désambig.** Pas de popup pour ce
cas (contrairement à V→∀ qui peut être V variable). Critère contextuel
strict : la lettre `U` (Ident) suivie immédiatement (avec ou sans espace)
d'un caractère `[` ou `]` (début d'intervalle) ET précédée d'une expression
qui se termine par un Interval → traitée comme `\cup`. Sinon `U` reste
variable.

**Pas de désambig pour `I` → ∩.** Trop souvent variable (matrice identité,
indice, etc.). Pour intersection, l'utilisateur tape `inter` en toutes lettres.

## Pourquoi

### Convention clavier lycée

`U` est l'écriture clavier naturelle pour `∪` (les lycéens écrivent
`A U B` au tableau). Le contexte (entre intervalles) lève toute ambiguïté :
quand on voit `[0,1] U [3,5]`, il n'y a aucun doute possible sur la
sémantique. Pas besoin de popup ambig.

### Pourquoi pas non plus mot-clé `union` à émettre comme `U` automatique

Si on déclarait `U` comme keyword global (canonical "union"), tout `U` isolé
serait interprété comme union, même quand l'utilisateur le veut comme
variable (`U` = univers, ensemble). La détection contextuelle (Interval avant
ET après) garde la sémantique variable dans tous les cas non-intervalles.

### Pourquoi `inter`/`intersection` keyword direct (pas `I`)

`inter` n'a presque aucun usage comme variable. Le faire keyword direct est
sans risque. À l'inverse, `I` est très utilisé en math (matrice identité,
indice de sommation, intégrale `I`, intervalle générique). Pas de désambig
sur `I`.

## Conséquences

### Code (couche 1 — core)

- **Vocabulary.cs** : ajouter `union` (canon "union"), `inter` et
  `intersection` (canon "inter") dans `Keywords`.
- **Parser.cs ParseExpr** : étendre la boucle pour matcher
  `IsKwCanon("union")` / `IsKwCanon("inter")` comme opérateurs binaires
  infix (priorité = `+`/`-`), produisant `Bin("union" ou "inter", lhs, rhs)`.
- **Parser.cs ParseExpr** : détection contextuelle `lhs (Interval) U
  Interval` — si `lhs` se termine par un `Interval` ET le prochain token
  est `Ident "U"` ET le suivant commence par `[` ou `]` → consommer le `U`
  comme union et produire `Bin("union", lhs, rhs)`. Avec ou sans espace.
- **LatexRenderer.cs RenderBin** : ajouter `if (b.Op == "union") return "{lhs} \\cup {rhs}"`
  et `if (b.Op == "inter") return "{lhs} \\cap {rhs}"`.
- **Parser.cs ParseScope** : pour `union`/`inter` au début (sans lhs), retour
  `Const("\\cup")` / `Const("\\cap")` placeholder (cas dégénéré, peu
  probable que l'utilisateur tape ça seul).

### Tests

- Parser : `[0,1] union [3,5]` → Bin("union", Interval, Interval)
- Parser : `[0,1] U [3,5]` → idem (détection contextuelle)
- Parser : `[0,1]U[3,5]` (sans espace) → idem
- Parser : `f(U)` → atom U (pas transformé, pas dans contexte intervalle)
- Parser : `U = R` → atom U (pas dans contexte intervalle)
- Parser : `[0,1] inter [0.5,2]` → Bin("inter", Interval, Interval)
- Renderer : `[0,1] \cup [3,5]`, `[0,1] \cap [0.5,2]`
- Pipeline : `forall x [0,1]U[2,3] x ≥ 0` → `\forall x \in [0,1] \cup [2,3] x \geq 0`

### Hors scope

- `union`/`inter` sur autre chose que des intervalles (ensembles génériques,
  unions multiples chaînées) : marche par effet de bord mais pas testé
  exhaustivement. Le rendu Bin("union", a, b) → `a \cup b` fonctionne pour
  tout couple a/b.
- Différence ∪ vs ⋃ (grand union) : on rend toujours `\cup`. Le grand union
  pour somme infinie d'ensembles est hors scope.

## Validé par l'utilisateur

Direction sur la composition :
> "ok et l'intervalle comme composition d'intervalle ?"

Précision sur la détection sans désambig pour U :
> "non pas I les gens vont pas y penser, par contre U entre deux intervalles
> avec ou sans espace d'ailleurs c'est une union 100%"

## Statut

acté
