# Brief — Reconnaissance vecteur + coordonnées (`u (1 2)` / `u(1, 2)`)

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-29
**Branche :** `lattice-engine`
**Public cible :** agent C#/lattice autonome qui ne connaît pas le projet.

**Note :** ce brief est un **sous-ensemble strict** du brief matrices
[`2026-04-29-matrices-and-column-vectors.md`](2026-04-29-matrices-and-column-vectors.md).
Périmètre limité à un pattern bien identifié, faisable en 1-2 jours, qui
couvre 80% des cas d'usage Terminale (notation point + coordonnées,
vecteur + coordonnées 2D/3D) sans la complexité de la grammaire matricielle
complète.

---

## 1. Le besoin

Au lycée, les vecteurs et les points sont systématiquement écrits avec
leurs coordonnées :

- $\vec{u} \begin{pmatrix} 1 \\ 2 \end{pmatrix}$ (vecteur + colonne, 2D)
- $A(1, 2)$ (point + coordonnées en ligne)
- $\vec{AB} \begin{pmatrix} 3 \\ -1 \\ 5 \end{pmatrix}$ (vecteur 3D)
- $M(x, y, z)$ (point en 3D)

Aujourd'hui MathCursor sait :
- Décorer un `vec u` → $\vec{u}$
- Reconnaître `AB` comme 2 majuscules → désambig vec/paren/crochet
- Mais ne sait PAS combiner avec des coordonnées explicites

L'élève qui tape `u (1 2)` ou `AB(3, -1)` veut voir le vecteur **avec
ses coordonnées sous forme de bloc**, pas un appel de fonction `u(1)` ou
juste l'identifiant nu.

## 2. Spec syntaxe

### 2.1. Deux cas, distingués par le **séparateur INTERNE** des coordonnées

L'espace avant la paren n'a **pas d'importance** — `u(3 4)` et `u (3 4)`
produisent le même résultat. Ce qui compte = le séparateur entre les
valeurs **dans** les parens :

- **Espace** entre les valeurs → **colonne** (vertical)
- **Virgule** entre les valeurs → **ligne** (horizontal)

L'identifiant à gauche peut être **1 ou 2 lettres** (`u`, `v`, `w`, `OM`,
`AB`, `MN`…). Décoration `\vec{...}` automatique sauf pour les majuscules
seules qui dénotent un point.

| Saisie | Sortie LaTeX | Layout |
|--------|--------------|--------|
| `u (3 4)` ou `u(3 4)` | `\vec{u} \begin{pmatrix} 3 \\\\ 4 \end{pmatrix}` | colonne 2D |
| `u (1 2 3)` ou `u(1 2 3)` | `\vec{u} \begin{pmatrix} 1 \\\\ 2 \\\\ 3 \end{pmatrix}` | colonne 3D |
| `u(3,4)` ou `u (3, 4)` | `\vec{u}(3, 4)` | ligne 2D |
| `u(1, 2, 3)` | `\vec{u}(1, 2, 3)` | ligne 3D |
| `A(1, 2)` ou `A(1,2)` | `A(1, 2)` | point — A majuscule seule, pas de `\vec` |
| `A (1 2)` ou `A(1 2)` | `A \begin{pmatrix} 1 \\\\ 2 \end{pmatrix}` | point + colonne |
| `AB (3 -1)` ou `AB(3 -1)` | `\vec{AB} \begin{pmatrix} 3 \\\\ -1 \end{pmatrix}` | vecteur AB colonne |
| `AB(3, -1)` | `\vec{AB}(3, -1)` | vecteur AB ligne |
| `OM (x y z)` | `\vec{OM} \begin{pmatrix} x \\\\ y \\\\ z \end{pmatrix}` | composantes symboliques |
| `u (a+1 b-2)` | `\vec{u} \begin{pmatrix} a+1 \\\\ b-2 \end{pmatrix}` | composantes en expressions |
| `u(2x+1, 3y-2)` | `\vec{u}(2x+1, 3y-2)` | composantes en expressions, ligne |

### 2.2. Règle de désambig — séparateur uniquement

| Séparateur entre coords | Layout |
|--------------------------|--------|
| espace(s) | **colonne** |
| virgule(s) | **ligne** |

- L'espace AVANT la paren n'a **aucun effet** (`u(...)` et `u (...)` sont
  identiques).
- Mélange interdit : `u(1, 2 3)` est un cas borderline → erreur ou
  fallback au comportement existant (groupe paren / function call).
- Si l'identifiant est UNE majuscule unique (`A`, `B`, `M`, `P`…) → c'est
  un **point**, pas de `\vec`. Sinon (1 minuscule typique vec, ou 2 lettres
  comme `AB`) → décoration `\vec{...}`.

### 2.3. Décoration `\vec` quand ?

| Identifiant | Avec coords | Décoration |
|-------------|-------------|------------|
| `u`, `v`, `w` (1 minuscule typique vecteur) | oui | `\vec{u}` |
| `AB`, `MN` (2 majuscules) | oui | `\vec{AB}` (already AB rule) |
| `A`, `B`, `M` (1 majuscule = point) | oui | pas de `\vec` |
| `f`, `g` (1 minuscule typique fonction) | seulement avec `(...)` collé | **AMBIGUITÉ** : `f(x)` → fonction. À résoudre via cascade ambig |

### 2.4. Cardinalité et nature des valeurs

- **2 ou 3 valeurs** (V1). 1 valeur ambigu avec single-arg fonction. 4+
  valeurs hors scope (rare au lycée).
- Valeurs acceptées dans une cellule :
  - **Nombres** : `1`, `-3`, `1.5`, `0`
  - **Identifiants** : `x`, `y`, `z`, `t`, `a`, `b`, `n`, etc.
  - **Expressions sans espace top-level** : `a+1`, `2x-3`, `1/n`,
    `(a+b)/2`, `cos(t)`, `sin(2x)`
  - **Expressions avec espaces si parenthésées** : `(cos t)`, `(sin x + 1)`,
    `(a + b)` — les parens font une cellule unique

**Restriction importante (layout colonne)** : si la cellule contient un
**keyword scope** qui consomme des arguments avec espaces (`sin x`,
`cos t`, `lim x 0`, `frac a b`, `sum k 1 n`…), il faut **parenthéser**
la cellule, sinon l'espace serait pris comme séparateur de cellules.

| Saisie ambigüe | Comportement V1 |
|----------------|-----------------|
| `u (cos t sin t)` | rejet (4 chunks détectés au lieu de 2) → fallback group / parser actuel |
| `u (cos(t) sin(t))` | OK, 2 cellules `cos(t)` et `sin(t)` |
| `u ((cos t) (sin t))` | OK, équivalent |
| `u (frac a b 1)` | rejet ambigu — `frac a b` consomme 2 args, layout perd |
| `u ((frac a b) 1)` | OK, 2 cellules |

Le parser doit savoir où couper en cellules :
- **Layout colonne** (séparateur espace) : top-level chunks séparés par
  espaces. Stratégie de comptage : chaque espace **hors paren** est un
  séparateur de cellule. Si le nombre de cellules ≠ 2 ou 3 → rejet et
  fallback au comportement existant (group).
- **Layout ligne** (séparateur virgule) : split à la virgule top-level,
  parser chaque cellule comme expression complète. Comportement standard.

## 3. Désambiguïsation

### 3.1. vs appel de fonction `f(x)`

`f(x)` reste un appel de fonction. Critères orthogonaux :

**Règle 1 — séparateur** :
- Espace top-level entre args → forcément layout colonne → forcément
  coordonnées (les fonctions n'utilisent pas l'espace comme sep d'args).
- Virgule entre args → soit fonction soit coords ligne, à arbitrer par
  l'identifiant.

**Règle 2 — identifiant (quand le séparateur est virgule)** :
- 1 lettre minuscule **typique fonction** (`f`, `g`, `h`, `F`, `G`,
  `H`) → fonction par défaut.
- 1 lettre minuscule **typique vecteur** (`u`, `v`, `w`) → coordonnées
  par défaut.
- 1 lettre majuscule (`A`, `B`, `M`, `P`…) → point par défaut (coords).
- 2 lettres (`AB`, `OM`, `MN`…) → vecteur par défaut (coords),
  cohérent avec la règle two-uppercase existante.

Exemples :
- `f(x)` → fonction (1 arg, comportement actuel)
- `f(2x+1)` → fonction (1 arg expression)
- `u (3 4)` → **toujours coords colonne** (espace = pas de fonction
  possible)
- `u(3, 4)` → coords ligne (`u` typique vecteur)
- `f(3, 4)` → **AMBIGUITÉ** : function call à 2 args OR row coords.
  Default = function call (`f` typique fonction). Alternative via
  cascade.
- `A(1, 2)` → coords point (A majuscule)
- `AB(1, 2)` → vecteur AB ligne (cascade ambig two-uppercase)

Toutes les ambig restent traitables via cascade `AlternativeGenerator`.

### 3.2. vs intervalle `(0; 1)` / `(0, 1)`

`(0; 1)` est aujourd'hui un intervalle ouvert. `u (1 2)` ne pose pas
problème (la présence de `u` à gauche désambiguïse).

Mais `(1, 2)` seul (sans préfixe) est ambigu :
- Intervalle ouvert ?
- Coordonnées d'un point anonyme ?
- Tuple ?

V1 décision : **`(1, 2)` seul reste un intervalle** (comportement actuel).
Le pattern coordonnées **EXIGE un identifiant à gauche**.

### 3.3. Cascade avec règle two-uppercase (AB / vec)

`AB` propose déjà 3 alternatives via `RuleTwoUppercase` (vec / paren /
bracket). Quand `AB(1, 2)` ou `AB (1 2)` est tapé :
- L'identifiant absorbe les coordonnées
- La désambig AB s'applique sur `\vec{AB}` (default), `(AB)`, `[AB]`
- Si l'élève choisit `\vec{AB}` (default) → `\vec{AB}(1, 2)` ou
  `\vec{AB} \begin{pmatrix}…\end{pmatrix}`
- Si l'élève choisit `(AB)` → `(AB)(1, 2)` (cas rare mais cohérent)

## 4. Architecture impactée

### 4.1. Lexer

Pas ou peu de changement. Le lexer voit déjà les patterns `u (`, `u(`,
les espaces, les virgules. Vérifier que la **présence/absence d'espace**
entre l'ident et la paren est conservée comme info au parser. Si non,
ajouter un flag dans le token paren (`HasSpaceBefore`) ou un token séparé
`SpaceParenOpen` vs `ParenOpen`.

### 4.2. Vocabulary

Aucun nouveau keyword nécessaire — la reconnaissance est purement
syntaxique.

### 4.3. Parser + AST

**Nouveau nœud** : `VectorCoordinates(name: AstNode, values: AstNode[], layout: 'column'|'row', isPoint: bool)`

- `name` : l'AST de l'identifiant (peut être Atom("u"), Atom("AB"), …)
- `values` : tableau de 2 ou 3 valeurs (Atom number ou Atom ident)
- `layout` : column (espaces) ou row (virgules)
- `isPoint` : true si l'identifiant est une seule majuscule ne déclenchant
  pas `\vec` (cas A, B, M, P…)

**Pattern de reconnaissance dans le parser** :
1. Voir un ident `name` (Atom kind=ident).
2. Lookahead : optionnel espace, puis `(`.
3. Parser le contenu paren en list de valeurs (cardinalité 2 ou 3,
   séparateur comma OR space cohérent).
4. Vérifier `)` à la fin.
5. Si match : créer `VectorCoordinates`. Sinon : fallback comportement
   actuel (Group / FunctionCall).

### 4.4. LatexRenderer

```
VectorCoordinates with layout=column, isPoint=false:
  → \vec{<name>} \begin{pmatrix} v1 \\ v2 [\\ v3] \end{pmatrix}

VectorCoordinates with layout=column, isPoint=true:
  → <name> \begin{pmatrix} v1 \\ v2 [\\ v3] \end{pmatrix}

VectorCoordinates with layout=row, isPoint=false:
  → \vec{<name>}(v1, v2[, v3])

VectorCoordinates with layout=row, isPoint=true:
  → <name>(v1, v2[, v3])
```

### 4.5. AlternativeGenerator

Cas où l'on veut proposer une alternative :

- `f(2, 3)` : default = function call (existant), alt = `\vec{f}(2, 3)`
  ou `f \begin{pmatrix} 2 \\ 3 \end{pmatrix}`. Cascade similaire au pattern
  AB.
- `u(1, 2)` : default = coordinates row, alt = function call à 2 args,
  alt = column block.
- `A(1, 2)` : default = point, alt = function call.

À voir avec come : combien d'alternatives expose-t-on ? La désambig
deux-uppercase a déjà 3 alts, on peut faire pareil ici.

## 5. Cas de test obligatoires (xUnit)

### 5.1. Vecteurs colonnes (séparateur INTERNE = espace)

| Saisie | LaTeX attendu (extrait) |
|--------|--------------------------|
| `u (1 2)` | `\vec{u} \begin{pmatrix} 1 \\\\ 2 \end{pmatrix}` |
| `u(1 2)` | `\vec{u} \begin{pmatrix} 1 \\\\ 2 \end{pmatrix}` (espace avant paren ne change rien) |
| `v(-1 3)` | `\vec{v} \begin{pmatrix} -1 \\\\ 3 \end{pmatrix}` |
| `u (1 2 3)` | `\vec{u} \begin{pmatrix} 1 \\\\ 2 \\\\ 3 \end{pmatrix}` |
| `OM (x y z)` | `\vec{OM} \begin{pmatrix} x \\\\ y \\\\ z \end{pmatrix}` |
| `AB (3 -1)` | `\vec{AB} \begin{pmatrix} 3 \\\\ -1 \end{pmatrix}` (default cascade) |
| `u (a+1 b-2)` | `\vec{u} \begin{pmatrix} a+1 \\\\ b-2 \end{pmatrix}` (expressions sans espaces internes) |
| `u (2x+1 3y-2)` | `\vec{u} \begin{pmatrix} 2x+1 \\\\ 3y-2 \end{pmatrix}` |
| `u (cos(t) sin(t))` | `\vec{u} \begin{pmatrix} \cos(t) \\\\ \sin(t) \end{pmatrix}` (parenthéser les trig en mode colonne) |

### 5.2. Coordonnées en ligne (séparateur INTERNE = virgule)

| Saisie | LaTeX attendu |
|--------|---------------|
| `u(1, 2)` | `\vec{u}(1, 2)` |
| `u (1, 2)` | `\vec{u}(1, 2)` (espace avant paren ne change rien) |
| `A(1, 2)` | `A(1, 2)` (point, pas de `\vec`) |
| `M(x, y, z)` | `M(x, y, z)` |
| `AB(3, -1)` | `\vec{AB}(3, -1)` |
| `u(2x+1, 3y-2)` | `\vec{u}(2x+1, 3y-2)` (expressions) |

### 5.3. Anti-régression — checklist exhaustive

**Cette feature touche au pattern le plus chargé du parser** (`<ident>(...)`).
Liste de TOUT ce qui doit continuer à marcher exactement comme avant.

#### Function calls
| Saisie | Comportement attendu (inchangé) |
|--------|----------------------------------|
| `f(x)` | function call — 1 arg ident |
| `f(2)` | function call — 1 arg number |
| `f(2x+1)` | function call — 1 arg expression |
| `f(x, y)` | function call à 2 args (ident `f` typique fonction) |
| `g(t)` | function call |
| `cos(x)` | trig function call |
| `sin(2x+1)` | trig function call expression |
| `ln(x)` | log function call |
| `exp(x)` | exp function call |
| `sqrt(x+1)` | racine function call |

#### Trigonométrie sans parens (scope-style)
| Saisie | Comportement attendu (inchangé) |
|--------|----------------------------------|
| `sin x` | `\sin x` (scope, pas paren) |
| `cos t` | `\cos t` |
| `tan(2x)` | `\tan(2x)` |
| `lim x 0 sin x / x` | `\lim_{x \to 0} \frac{\sin x}{x}` (lim scope intact) |

#### Intervalles
| Saisie | Comportement attendu (inchangé) |
|--------|----------------------------------|
| `(0, 1)` | intervalle ouvert (PAS de coords car pas d'ident à gauche) |
| `(0; 1)` | intervalle ouvert |
| `[0, 1]` | intervalle fermé |
| `[0; 1]` | intervalle fermé |
| `[0; 1[` | intervalle semi-ouvert |
| `[0; +inf[` | intervalle non borné |
| `[0,1] U [2,3]` | union d'intervalles |

#### vec keyword
| Saisie | Comportement attendu (inchangé) |
|--------|----------------------------------|
| `vec u` | `\vec{u}` (keyword scope, pas de coord) |
| `vec AB` | `\vec{AB}` |
| `vec u + vec v` | `\vec{u} + \vec{v}` |

#### AB cascade ambig (two-uppercase)
| Saisie | Comportement attendu (inchangé sauf désambig contextuelle) |
|--------|------------------------------------------------------------|
| `AB` seul | cascade : `\vec{AB}` (default), `(AB)`, `[AB]` |
| `AB + CD` | cascade two-uppercase appliquée |
| `AB(3, -1)` | NOUVEAU : `\vec{AB}(3, -1)` (default) avec `AB` dans la cascade |

#### Number-tight et Sup
| Saisie | Comportement attendu (inchangé) |
|--------|----------------------------------|
| `x2` | `x^2` (Sup implicite) |
| `x^2` | `x^2` |
| `u(x2, y2)` | NOUVEAU : `\vec{u}(x^2, y^2)` — number-tight appliqué dans cellules |

#### Holes et fractions
| Saisie | Comportement attendu (inchangé) |
|--------|----------------------------------|
| `frac a b` | `\frac{a}{b}` |
| `frac` seul | `\frac{\square}{\square}` |
| `f(_)` | function call avec Hole |

#### Quantificateurs et ensembles
| Saisie | Comportement attendu (inchangé) |
|--------|----------------------------------|
| `forall x R, x^2 >= 0` | `\forall x \in \mathbb{R}, x^2 \geq 0` |
| `exists y N` | `\exists y \in \mathbb{N}` |
| `V x R` | cascade V→forall |

#### Définitions de fonctions (V0.5.0)
| Saisie | Comportement attendu (inchangé) |
|--------|----------------------------------|
| `f : x -> 2x+1` | définition de fonction |

#### Mode édition (revert source)
| Action | Attendu |
|--------|---------|
| Ctrl+E sur un OMath issu de `u (1 2)` | revert au texte `u (1 2)` |
| Ctrl+E sur un OMath issu de `u(1, 2)` | revert au texte `u(1, 2)` |

### 5.4. Désambig fonction vs coords (cascade)

| Saisie | Default | Alternatives proposées |
|--------|---------|-------------------------|
| `u(1, 2)` | coords row | function call `u(1, 2)`, column block |
| `f(1, 2)` | function call (f minuscule = fonction) | coords row `\vec{f}(1, 2)` |
| `A(1, 2)` | coords point | (pas d'alt, A en majuscule = clair) |

## 6. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `core-csharp/src/MathCursor.Core/Lattice/Lexer.cs` | Vérifier flag espace-avant-paren |
| `core-csharp/src/MathCursor.Core/Lattice/Parser.cs` | Pattern de reconnaissance (à étendre) |
| `core-csharp/src/MathCursor.Core/Lattice/Ast/` | Nouveau nœud `VectorCoordinates` |
| `core-csharp/src/MathCursor.Core/Lattice/LatexRenderer.cs` | Rendu LaTeX (4 cas §4.4) |
| `core-csharp/src/MathCursor.Core/Lattice/AlternativeGenerator.cs` | Cascade ambig vs FunctionCall |
| `docs/dev/briefs/2026-04-29-matrices-and-column-vectors.md` | Brief parent (matrices complètes — ce brief en est un sous-ensemble) |

## 6.b. Stratégie de non-régression (à respecter au niveau du code)

Cette feature ajoute un nouveau pattern qui chevauche `f(x)` (function
call) et `(0, 1)` (intervalle). Quelques règles strictes pour minimiser
le risque de casse :

### Reconnaissance par opt-in, pas par défaut

Le pattern coordinates ne doit être reconnu que **si TOUS** ces critères
sont vrais — sinon retomber sur le comportement existant :

1. Présence d'un **identifiant** (1 ou 2 lettres) immédiatement à gauche
   de la paren (ou avec un espace simple).
2. Le contenu des parens parse en **exactement 2 ou 3 cellules**.
3. Le séparateur interne est **homogène** (que des espaces top-level OU
   que des virgules top-level — pas de mélange).
4. Pour le layout colonne (espace) : les cellules ne contiennent **pas**
   de keyword scope avec espace non parenthésé (`sin x`, `lim …`,
   `frac a b`, etc.).

Si **un seul** critère manque → fallback pur : on ne touche pas au
parsing existant. Aucune cellule n'est créée, on retombe sur
FunctionCall ou Group ou Interval comme avant.

### Position dans le pipeline

- **Lexer** : ne change PAS de comportement (les espaces et virgules
  sont déjà des tokens). Vérifier juste que l'info "espace avant paren"
  est préservée si un jour on en a besoin (V2). En V1 elle ne sert pas.
- **Parser** : ajout du nouveau pattern dans la passe d'expression, en
  position **AVANT** la règle FunctionCall pour les idents qui matchent
  les critères 1-3, **APRÈS** sinon. Ordre de priorité explicite à
  documenter.
- **AlternativeGenerator** : ajouter une nouvelle ambig
  `RuleVectorCoordsVsCall` qui propose les alternatives quand les deux
  interprétations sont valides (cas `f(1, 2)`).

### Tests de non-régression obligatoires

- Faire tourner toute la suite `LatticeEngineTests`, `LexerTests`,
  `LatexRendererTests`, `AlternativeGeneratorTests` AVANT le commit
  final. Aucun test existant ne doit casser.
- Ajouter explicitement les ~30 cas du §5.3 comme tests xUnit séparés.
- Si **un** test existant régresse, c'est un signe qu'on a trop
  élargi le pattern de reconnaissance — restreindre les critères ci-dessus.

## 7. Ce qu'il NE faut PAS faire

- ❌ Implémenter le support matrice complet (`(1 2 ; 3 4)` etc.) dans
  cette PR. C'est l'objet du brief parent. Ici on reste sur le **pattern
  vecteur + coordonnées** : ident + parens + valeurs simples.
- ❌ Casser `f(x)` qui doit rester un function call. Tester l'anti-régression.
- ❌ Casser les intervalles `(0; 1)`, `[0; 1[`. Le pattern coordonnées
  EXIGE un ident à gauche, donc pas de conflit.
- ❌ Accepter 4+ valeurs en V1. Limiter à 2 ou 3 (cible Terminale).
  Les expressions internes sont OK (`a+1`, `2x-3`, `cos t`).
- ❌ Décorer un identifiant majuscule unique (`A`, `B`, `M`) avec `\vec`.
  Ce sont des points en notation française.
- ❌ Forcer la désambig — laisser la cascade existante (popup deux-section
  comme pour AB/x2) gérer les cas borderline `f(1, 2)`.

## 8. Validation

1. `dotnet build MathCursor.sln` → 0 erreur, 0 warning nouveau.
2. `dotnet test core-csharp/tests/` → tous les tests passent, dont les
   nouveaux du §5.
3. Test manuel sur démo web après `dotnet publish` :
   - `u (1 2)` → vecteur colonne 2D
   - `AB(3, -1)` → vec AB row coords
   - `M(1, 2, 3)` → point 3D
   - `f(x)` → toujours function call (anti-régression)
4. Test manuel dans Word avec MSI rebuilt :
   - Cas du §5 directement dans un document, Ctrl+Espace.
5. ADR créé : `docs/dev/decisions/2026-04-XX-Feat-vector-coordinates-shorthand.md`
   - Kind = Feat, Température = molle, Statut = acté
   - Citation utilisateur = ce brief

## 9. Estimation

| Tâche | Durée |
|-------|-------|
| Lecture Lexer/Parser/AST existants pour vérifier où s'accrocher | 1 h |
| Vérif lexer (flag espace-avant-paren) + ajout si manquant | 1 h |
| Nouveau nœud AST `VectorCoordinates` | 30 min |
| Pattern de reconnaissance dans Parser | 2-3 h |
| Renderer (4 cas §4.4) | 1 h |
| Tests xUnit (~12 cas du §5) | 2-3 h |
| Cascade AlternativeGenerator (au moins le cas `f(1, 2)`) | 1-2 h |
| ADR + commit propre | 30 min |
| **Total V1** | **~9-12 h ≈ 1.5 jours** |

## 10. Phasing avec le brief parent

Ce brief peut être :

- **Soit indépendant** : on ship cette PR, pas de matrices complètes
  encore. Couvre 80% du besoin Terminale (vecteurs/points avec coords).
- **Soit Phase 0 du brief matrices** : on commence par ça, puis on
  continue avec phase 1 (vecteurs colonnes via keyword `colvec`), etc.
  Cohérent : le rendu LaTeX `\begin{pmatrix}` est partagé.

**Recommandation** : faire ce brief **indépendamment et en premier**.
Plus simple, plus rapide, gain produit immédiat. Si l'utilisation
quotidienne révèle un besoin de matrices `(1 2 ; 3 4)` plus tard, le
brief parent reste applicable et réutilise tout le rendu pmatrix.

---

**Question ouverte pour come** : confirmer la règle "espace avant paren
= colonne, pas d'espace = ligne". Sinon proposer une autre règle (ex :
toujours colonne par défaut, alternative ligne via popup).
