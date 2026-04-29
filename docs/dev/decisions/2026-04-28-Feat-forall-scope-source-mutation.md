# Feat — Quantificateur ∀/∃ via scope clavier + désambiguïsation par mutation source

**Date :** 2026-04-28
**Kind :** Feat
**Température :** forte
**Statut :** retracté
**Superseded by :** [2026-04-29-Feat-forall-modular-decomposition.md](2026-04-29-Feat-forall-modular-decomposition.md)

> ⚠️ **ADR retracté le 2026-04-29.** Le scope `forall var (in?) set` introduit
> ici a été remplacé par une décomposition modulaire (∀ + var + ∈ + set
> juxtaposés). ~150 lignes de code en moins. Voir l'ADR successeur pour les
> motifs et le nouveau périmètre. Le mécanisme de **désambig V → 3 alts (V/∀/√)**
> et de **mutation source** est conservé ; seul le scope monolithique disparaît.

## Décision

1. Le mot-clé `forall` (et `exists`) devient un **scope au clavier** comme `somme` /
   `lim` / `int` : la séquence `forall x R` (espaces = séparateurs d'arguments) rend
   `\forall x \in R`. Le `\in` est généré par le scope, pas tapé. Args manquants
   matérialisés par des `Hole` (rendus `\square`), symétrique avec `Sum`/`Lim`/`Int`.

2. La résolution d'ambiguïté ne mute plus seulement le **rendu LaTeX** mais aussi
   le **source** (la chaîne d'entrée). Chaque `AmbiguityAlternative` transporte
   sa propre `SourceMutation { offset, length, replacement }` (ou null pour
   l'alt identité). L'adapter VSTO applique la mutation au ContentControl puis
   relance le moteur sur la nouvelle source.

3. **Multi-alternatives par lettre ambiguë** : V suivi d'un espace (ou EOF),
   précédé d'un word-boundary, déclenche un Spot avec **3 alternatives** :
   - Alt 0 : V (identity, pas de mutation, choix par défaut si l'utilisateur
     continue à taper sans sélectionner)
   - Alt 1 : ∀ (mutation `V` → `forall`, scope quantificateur)
   - Alt 2 : √ (mutation `V` → `racine`, scope racine)
   Idem E : 2 alts (E identity / ∃ mutation `E` → `exists`). Pas de racine
   pour E (∃ uniquement).

4. **Aperçu fidèle post-mutation** : chaque alt expose un `Latex` qui est le
   rendu RÉEL de la source post-mutation (via `RenderAfterMutation`), pas un
   template figé. Pour `V x R` avec mutation V→forall, l'aperçu est
   `\forall x \in R` (var et set remplis), pas `\forall \square \in \square`.

5. **Flow utilisateur** :
   - User tape `V` (ou `V espace`)
   - Ctrl+Espace → popup avec les 3 alts en rangée du haut
   - Si user navigue + Enter sur alt → mutation appliquée, popup re-render
   - Si user continue à taper sans sélectionner → popup se ferme, V identity
     (auto-select choix 1)
   - Une fois alt ∀ choisie : utilisateur tape `x` puis `R`, les Holes se
     remplissent au fur et à mesure (`\forall x \in \square` → `\forall x \in R`)
   - Flèche bas → focus formule finale, Enter pour commit (flow habituel)

## Pourquoi

### Ergonomie clavier (point 1)

L'utilisateur lycée tape `V x R` au clavier (clavier physique français,
PAP-friendly). Sans scope, `V x R` se résout en `V * x * R` ou en `Vx R` selon
l'espace. Le mécanisme `somme k 0 n cos x` qui marche déjà (espaces filtrés au
parser, scope qui consomme par appels séquentiels à `ParseAtomOnly` /
`ParseArgument`) est exactement la bonne primitive pour `forall`. Étendre
`ParseScope case "forall"` est cohérent avec ce qui existe et symétrique avec
les autres scopes du brief (algorithm.md §4).

### Mutation source (point 2)

Le modèle actuel d'`AlternativeGenerator` substitue dans le **LaTeX rendu** :
`AB` → `\vec{AB}`, `x^2` → `x_2`, etc. Trois limites :

- **Pas de cohérence avec la frappe ultérieure** : si l'utilisateur résout `V`
  en `\forall` puis tape `x R`, le source du ContentControl est toujours `V x R`
  sous le LaTeX `\forall x R` — incohérent et fragile (le re-Convert produira
  à nouveau `V*x*R`).
- **Sub LaTeX fragile** : avec les `\left(`, les espaces \LaTeX, les `_{}` /
  `^{}`, retrouver le segment à substituer n'est pas trivial (régressions
  récurrentes : popup x_2 sur x^2 explicite, AB*CD avec AB seul résolu).
- **Pas de réutilisation du parser** : chaque alt est une string LaTeX figée.
  Si on veut `\forall x \in R` avec des Holes pour les args manquants
  (`\forall \square \in \square` puis `\forall x \in \square`), il faut un
  re-parsing donc une mutation au niveau source.

Avec `SourceMutation` :

- Le source reste l'unique vérité, le rendu LaTeX est une projection.
- Re-Convert applique le pipeline complet (Lex → TopK → Parse → Render) qui
  gère les Holes natively (`Hole` → `\square`).
- Les autres règles (AB/ABC/x2) peuvent migrer progressivement vers
  `SourceMutation` (PR ultérieures hors scope de cet ADR).

### UX des Holes (point 3)

Conséquence directe du choix mutation-source : après la sub `V` → `forall`, le
parser tombe sur `forall ` (pas d'arg → `Hole(1)` puis `Hole(2)`). Le Renderer
rend `\forall \square \in \square`, l'utilisateur voit visuellement les boîtes
à remplir. Quand il tape `x` puis ` R`, les carrés se remplissent. **Il n'a
jamais à taper `dans` ou `in`**, ce qui est l'objectif ergo.

## Conséquences

### Code

- **Nouveau nœud AST** `Quant(symbol, var, set)` dans
  `core-csharp/src/MathCursor.Core/Lattice/Ast/AstNodes.cs`. `Var` et `Set`
  jamais null — les args manquants sont matérialisés par `Hole` (rendu
  `\square`), exactement comme `Sum`/`Lim`/`Int`.
- **Parser** `ParseScope` étendu pour `case "forall"` / `case "exists"` :
  retourne TOUJOURS un `Quant` (jamais un `Const`). `var = ParseAtomOnly() ?? Hole(1)`,
  puis `in?` optionnel via `IsKwCanon("in")`, puis `set = ParseArgument() ?? Hole(2)`.
  → Symétrique avec `somme k 0 n cos x` qui montre `\sum_{k=\square}^{\square} \square`
  pour les args manquants.
- **Renderer** : branche `Quant => "{symbol} {Render(var)} \\in {Render(set)}"`.
  Le `\in` est toujours rendu (jamais omis), c'est un rail typographique du scope.
- **AmbiguityAlternative** (nouveau type) : `{ Latex, Mutation? }`. Remplace
  la liste plate `IReadOnlyList<string>` qui était dans `AmbiguitySpot`. Chaque
  alt porte sa propre mutation, ce qui permet d'avoir des choix hétérogènes
  dans le même Spot (V identity sans mutation + ∀ avec mutation forall + √
  avec mutation racine).
- **AlternativeGenerator.RenderAfterMutation** : helper qui simule la mutation
  in-memory (Lex+TopK+Parse+Render sur la source mutée) pour produire l'aperçu
  fidèle de chaque alt. Sans ça, l'aperçu serait toujours générique (carrés
  vides) même quand la source contient déjà var/set.
- **AlternativeGenerator** : règle `RuleVAsForall` qui scan le source pour
  `V` suivi d'un espace (word-boundary devant), produit 3 alts (V identity, ∀,
  √) chacune avec sa mutation source.
- **Adapter VSTO** : `SuggestionPopupWindow.ResolveCurrentAltIfFocused`
  applique la mutation au ContentControl puis re-Convert.

### Tests

- Parser : `forall x R` → `Quant("\\forall", x, R)`.
- Renderer : `Quant` → `\forall x \in R`. Avec Holes → `\forall \square \in \square`.
- AlternativeGenerator : `V ` → mutation `V` → `forall`, alt `\forall \square \in \square`.
- Régression : `Vx` (collé), `V*x` (op), `Volume` (mot) → pas d'ambig.

### Hors scope (PR ultérieures)

- Migration des règles AB/ABC/x2 vers `SourceMutation` (rétro-compat préservée).
- Détection « V suivi d'autre chose qu'espace » (ex `V,x` n'est pas couvert ici).
- Mode `exists` symétrique (la règle ParseScope est faite, mais pas la
  détection ambig E).

## Validé par l'utilisateur

Brief direction :
> "j'aimerai qu'on gere le cas (V x dans R) donc V tout seul à desambiguiser et
> limite V x R => une formule comme limite, ou les espace sont des separateurs
> d'arguments ?"

Précision sur le mécanisme :
> "et on peut pas faire que la desambiguité transforme le token V en token
> scopé ? forall ?"

Précision sur le déclencheur :
> "V espace => desambiguité => forall"

UX des Holes :
> "du coup on voit V [] (- [] les carrés se remplissent au fur et a mesure de
> la frappe et l'utilisateur voit qu'il ne doit pas taper 'dans'"

Validation finale du plan :
> "oui"

Confirmation symétrie avec `somme` (ne pas court-circuiter en `Const` quand args
manquants) :
> "peux tu faire que forall se comporte comme somme ? espace + variable + espace
> + ensemble (le appartient est mis automatiquement)"

Spec finale du flow multi-alternatives (V/∀/√, auto-select V si user continue) :
> "j'aimerai revoir tout cet enchainement avec le V
> 1. l'utilisateur tape V ou V espace
> 2. l'utilisateur tape ctrl+espace -> la popup se loade
> 3. la popup presente 3 choix de desambuisation (dans cet ordre) V / ∀ / √
> 4. choix 1 ou l'utilisateur continue de taper on auto select choix 1 / Choix 2
>    on valide la desambiguisation et on attend les arguments séparé par espace
>    (comme une somme ou une limite) / choix 3 on valide la desambuigisation et
>    on attends le parametre
> 5. l'utilisateur continue a ecrire et fleche du bas pour validation finale +
>    entree quand il est content.. on est dans le flow habituel"

Aperçu fidèle (carrés ne devraient pas apparaître quand var/set sont déjà tapés) :
> "y'a un truc bizarre la desambiguité ne droit pas montrer de carré la si ?"

## Statut

acté
