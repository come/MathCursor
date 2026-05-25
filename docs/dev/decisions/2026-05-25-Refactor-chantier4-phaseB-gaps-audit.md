# Refactor — Chantier 4 Phase B : audit des gaps RewriteEngine vs concepts YAML

**Date :** 2026-05-25
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- ADR [2026-05-25-Refactor-chantier4-phaseA-rewriting-poc](2026-05-25-Refactor-chantier4-phaseA-rewriting-poc.md) (= POC Phase A).
- Brief [2026-05-23-Meta-yaml-collision-dsl-future](2026-05-23-Meta-yaml-collision-dsl-future.md) (= mockups initiaux).

## Citation acté

> « oui ! attention si les tests tournent pas tu me dis et on ajuste » — utilisateur, 2026-05-25

## Contexte

Phase A a posé un POC `RewriteEngine` avec 14 cas de test (`fract`, `dot-vec`,
intervalles, sommes, lim, funcdef, matrices, slurp N termes). Avant
de migrer les ~50 règles YAML existantes (`data-v2/concepts/*.yml`),
on identifie quelles extensions de matcher sont **strictement nécessaires**.

## Méthode

15 probes (= `PhaseBProbeTests.cs`) qui répliquent les patterns YAML
existants dans la limite de ce que le matcher actuel supporte. Résultat :

**10 probes ✅ passent** sans extension :
- `frac 1 2`, `sqrt 2`, `sqrt 3 8`, `vec u`, `derive x y`, `iint x y z`,
- `forall x R`, `exists y N`, `norm u`, `congru a b n`,
- `somme k 1 n k`, `int x 0 n y`, `lim x 0 y` (= cas simples 1-Item).

**5 probes ❌ échouent**, regroupent **4 gaps structurels** :

### Gap G1 — Slot greedy

Symptôme : `somme k 1 n k+1` ne matche pas — le slot `{body:Expr}` ne capture qu'un seul Item, donc `k+1` reste flottant.

Extension nécessaire :
- Marqueur `Slot.Greedy: bool` : si vrai, le slot consomme tous les Items consécutifs jusqu'au prochain terminator (= fin de pattern ou prochain literal du pattern parent).
- Coût : ~30 LOC dans `RewriteMatcher`.

### Gap G2 — Slot avec précédence (`{bound}`)

Symptôme : `int x 0 n+1 y` — `n+1` doit être capturé en une seule borne, pas Item par Item.

Extension nécessaire :
- Catégorie `Bound` qui consomme une expression précédence-stop à addsub-1 (= `+`/`-` sont **inclus** dans la borne, mais `=`/`<`/`>` non).
- OU mécanisme déclaratif `slot.UntilCategory: Comparison` qui dit « capture jusqu'à un Item de cette catégorie ».
- Coût : ~40 LOC.

### Gap G3 — Element optionnel

Symptôme : `somme k=1 n k` — le `=` est facultatif (`somme k 1 n k` aussi valide).

Extension nécessaire :
- `Literal.Optional: bool`. Si vrai, ne fait pas échouer le match si absent.
- OU marqueur YAML `=?` qui produit un `Literal` optionnel.
- Coût : ~10 LOC.

### Gap G4 — Filler optionnel (catégorie wildcard skippable)

Symptôme : `lim quand x tend vers 0 f(x)` — mots de transition à ignorer.

Extension nécessaire :
- `PatternElement.OptionalFiller(Category)` qui consomme zéro ou plus d'Items d'une catégorie donnée (= `Sep` ou `StopWord`).
- Marqueur YAML `<filler>?`.
- Coût : ~15 LOC.

### Gap bonus — Paren-group → Expr

Symptôme : `frac (x+1) (x-1)` — les parenthèses doivent être traitées comme un groupe atomique qui devient un `Expr` consommable par `{num:Expr}`.

Solution : **règle générique YAML** `( {inner:Expr} ) → produces: Expr, emit: ($inner)`. Aucune extension de matcher nécessaire — c'est juste une règle data.

Coût : 0 LOC moteur + 1 règle YAML.

## Décision

**Pas de code en Phase B**. On documente les 4 gaps, on les classe par ordre de priorité, on attaque Phase C/C+ avec ces extensions.

Ordre suggéré :
1. **G3 (optionnel)** — 10 LOC, débloque les variantes `=?`/`<to>?` immédiatement.
2. **G1 (greedy)** — 30 LOC, débloque tous les body multi-Item.
3. **G4 (filler)** — 15 LOC, débloque `lim quand x tend vers 0 f(x)`.
4. **G2 (bound précédence)** — 40 LOC, mais peut être contourné en pratique avec G1 + paren-group dans la majorité des cas.

Total estimé : ~95 LOC d'extensions moteur. Reste compatible avec
l'estimation initiale Phase A (= « +100 LOC max » pour patterns exotiques).

## Tradeoff & alternatives écartées

- **Tout coder en Phase B** : rejetée. La Phase B est un audit, pas un livrable de migration. Mieux : séparer en ADR distinct chaque extension pour traçabilité.

- **Renoncer aux gaps et migrer ce qui passe** : rejetée. 10/15 cas ne représentent pas la majorité de l'usage : `somme avec body multi-Item` (G1) est ultra-fréquent.

- **Adopter un parser context-free pour gérer G1+G2** : rejetée. Ajoute une couche d'abstraction non nécessaire. Le greedy slot + bound-stop sont suffisants et restent expressibles en YAML déclaratif.

## Conséquences

- **Code nouveau** :
  - `Rewriting/PhaseBProbeTests.cs` (+170 lignes, dont 13 règles probe + 15 tests).

- **Tests** :
  - 15 probes Phase B (= 10 ✅ + 5 marqueurs de gap).
  - 331/331 engine v2 verts (= 302 préservés + 29 POC dont 15 Phase B) + 3 skipped.

- **API publique** : aucune modification.

## Validation post-fix

1. `dotnet test core-csharp/tests/MathCursor.Engine.Tests/` → 331/331 + 3 skipped.
2. La liste des gaps ci-dessus est exhaustive sur les 9 concepts YAML existants (`fractions`, `sommes`, `analyse`, `limites`, `vecteurs`, `logique`, `congruences`, `norme`, `funcdef`).

## Suite

- **Phase C-1** (G3 + G4 + paren-group) : ~25 LOC matcher + règle paren YAML. Débloque ~30 % des patterns YAML restants.
- **Phase C-2** (G1 greedy) : ~30 LOC. Débloque ~50 % des patterns supplémentaires.
- **Phase C-3** (G2 bound précédence) : ~40 LOC. Débloque les bornes composites.
- **Phase D** : bascule complète une fois Phase C complète.

## Plan en cours — état d'avancement

| # | Chantier | Statut |
|---|---|---|
| 1 | hardcoded FR → YAML | ✅ |
| 2 | Normalizer dédié | ✅ |
| 3 | Pre-passes → IPreResolver | ✅ |
| 4-A | POC RewriteEngine isolé | ✅ |
| 4-A+ | RepeatGroup (matrices) + inner composite (slurp N) | ✅ |
| **4-B** | **Audit gaps moteur** | ✅ acté ici |
| 4-C-1 | Optionnel + filler + paren-group | à faire |
| 4-C-2 | Slot greedy | à faire |
| 4-C-3 | Bound précédence | à faire |
| 4-D | Bascule MathEngine → RewriteEngine | à faire |
| 5 | (absorbé par Ch4) | absorbé |
| 6 | Découper SuggestionService god class | à faire |
