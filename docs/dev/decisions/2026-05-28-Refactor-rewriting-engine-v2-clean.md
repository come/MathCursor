# Refactor — Moteur rewriting V2 from scratch (architecture cible)

**Date :** 2026-05-28
**Kind :** Refactor
**Température :** forte
**Statut :** proposé
**Supersedes :** —
**Lié à :**
- ADR [2026-05-25-Refactor-chantier4-phaseA-rewriting-poc](2026-05-25-Refactor-chantier4-phaseA-rewriting-poc.md) (= POC actuel).
- ADR [2026-05-26-Refactor-anchor-callable-unified](2026-05-26-Refactor-anchor-callable-unified.md) (= KEYWORD args ≡ KEYWORD(args)).
- ADR [2026-05-26-Refactor-trig-rules-deferred](2026-05-26-Refactor-trig-rules-deferred.md) (= sin x+1 format).

## Citation acté

> « ok ecrit le bon ADR avec tout ce qu'on a bien discuté la on fera un nouveau moteur de zero je pense » — utilisateur, 2026-05-28

Cet ADR consolide les discussions des 25-28 mai 2026 sur l'architecture du moteur de résolution math. Il **remplace** le POC RewriteEngine actuel par une refonte from-scratch propre.

## Contexte

Le POC RewriteEngine (Phase A → Phase D) a démontré la viabilité du rewriting bottom-up via YAML déclaratif. Mais :

- Le scheduling actuel (= 3 phases : token-fusion → primitives → anchors) est LOCAL et glouton. Il casse sur `1/Somme k 0 n f(k)` car `prim-frac-implicit` capture `1/Somme` avant que Somme rule ait sa chance.
- Le partial match en typing flow n'est pas géré (= popup guide `\square` manquante).
- La composition récursive (`lim x sum k 0 n f(k)`) ne fonctionne pas naturellement.
- Le `RewriteRuleLoader` reste un adapter (= ShapeParser convertit shape strings vers Pattern structuré, c'est OK mais YAML legacy `shape:`/`anchor:` archaïque).

Multi-discussions session 26-28 mai ont fait émerger une **architecture cible cohérente** que cet ADR fige.

## Décision

Refonte from-scratch d'un nouveau moteur (`MathCursor.Engine.Rewriting.V2/` ou rename de l'existant après vidage) qui implémente **6 principes** intriqués.

### Principe 1 — Tout en YAML déclaratif, zéro code C# par règle

**Format YAML natif** (= format final, plus de "conversion") :

```yaml
concept: fractions
rules:
  - id:       frac-explicit
    pattern:  "frac {num} {den}"
    produces: expr
    emit:     "\\frac{$num}{$den}"
    tests:
      - "frac 1 2 => \\frac{1}{2}"
```

Chaque règle déclare son `pattern:`, son `produces:`, son `emit:`. Lue directement en `RewriteRule` sans intermédiaire.

### Principe 2 — Catégories sémantiques typées + subsumption

Hiérarchie de catégories (`Category` enum) :

```
any ⊃ expr
   expr ⊃ { letter, number, var, function, vector, interval, set, expr }
   set  ⊃ interval

(catégories techniques orthogonales : symbol, delim, sep)
```

Chaque règle YAML déclare son `produces:` (= type sémantique du RewriteItem émis).

**Conséquence** : une règle `{a:set} union {b:set}` matche aussi bien `R union [0;1]` (= Set ∪ Interval) que `[0;1] union [2;3]` (= Interval ∪ Interval). Les types composent.

### Principe 3 — Scan-keywords + scoping (passe top-down)

Au lieu du bottom-up glouton actuel, le moteur fait une **passe préliminaire** :

```
Passe A (scan keywords) :
  Pour chaque Item, vérifier s'il match le 1er literal d'une règle anchor.
  → liste de (position, rule).

Passe B (scoping) :
  Pour chaque anchor détecté (de droite à gauche pour imbriquer correctement) :
    - les N slots suivants = ses params
    - le dernier slot greedy s'étend jusqu'à fin / prochain anchor / délimiteur

Passe C (résolution récursive inside-out) :
  Résoudre les params dans le scope (= primitives).
  Apply règle anchor.
  Result : Item typé via produces.

Passe D (primitives sur résiduel) :
  +, -, /, ^, _, ( ) sur ce qui reste hors scopes.
```

**Exemple `1/Somme k 0 n f(k)`** :
- Passe A : `Somme` détecté à index 2 (= alias de `sum`, règle somme-k-from-to).
- Passe B : scope = tokens à droite (`k 0 n f(k)`). `1, /` hors scope.
- Passe C : résout `f(k)` → Expr ; apply Somme → Expr(\sum_{k=0}^{n} f(k)).
- Passe D : `1, /, Expr(\sum...)` → `prim-frac-implicit` matche → `\frac{1}{\sum_{...}}` ✅

### Principe 4 — Partial match obligatoire en typing flow

**Règle d'or** : tout anchor reconnu produit un Item de catégorie `produces:` — **même si ses slots sont vides**. Les slots non remplis deviennent `\square`. L'Item résultant a `IsPartial = true` mais sa **Category est celle déclarée**.

**Conséquence** : la composition fonctionne dès le 1er caractère pertinent.

**Exemple `1/sum` (en cours de frappe)** :
- `sum` détecté → règle attend 4 slots, 0 rempli → Expr partial = `\sum_{\square=\square}^{\square}\square`.
- `prim-frac-implicit` matche `1 / Expr(partial)` → `\frac{1}{\sum_{\square=\square}^{\square}\square}`.
- Popup montre la structure complète avec carrés.

**Exemple `lim x sum`** :
- `sum` partial → Expr partial.
- `lim x Expr(sum partial)` matche → `\lim_{x \to \sum_{\square=\square}^{\square}\square} \square`.

Récursion naturelle.

### Principe 5 — Multi-chains (beam search) avec scoring

À chaque ambiguïté (= plusieurs règles matchent au même endroit), le moteur **fork** en plusieurs chaînes de résolution. Garde top-K (K = 4 par défaut).

```csharp
var initial = new ResolutionChain(tokens);
var chains = new List<ResolutionChain> { initial };

while (chains.Any(c => c.HasAvailableRewrite())) {
    var next = new List<ResolutionChain>();
    foreach (var chain in chains) {
        foreach (var match in chain.AvailableMatches().Take(BranchingFactor)) {
            next.Add(chain.Apply(match));
        }
    }
    chains = next.OrderBy(c => c.Score).Take(K).ToList();
}

return new RewriteResult(
    best:         chains.First(),
    alternatives: chains.Skip(1).Take(3)
);
```

**Critère de scoring** :
1. **Moins d'items résiduels** (= une expression bien résolue = 1 seul Item).
2. **Priorité moyenne des règles** appliquées (= règles avec anchor literal P=100 favorisées sur primitives P=50).
3. **Pas de slots partiels** (= full match préféré à partial).

**Conséquence** : les lectures slurp/strict pour `1/2 + 3/4` apparaissent toutes comme alternatives dans la popup. L'utilisateur tranche.

### Principe transversal — tolérance aux espaces

**Règle implicite unique** : entre chaque élément d'un pattern, le matcher
skip les `Sep " "` automatiquement. **Aucune règle ne doit déclarer ses
espaces.**

Exemple : `[0;1] U [0;4]` ≡ `[0;1]U[0;4]` — les 2 inputs matchent le même
pattern `{a:set} U {b:set}` sans aucune adaptation. Le matcher se contente
de skipper les Sep en début d'élément.

**Exceptions (= `glued`)** : 2 cas explicites où la cohérence sémantique
exige absence de Sep :
- `prim-implicit-product` : `{a:number}{b:letter glued}` (= `2x` collé,
  pas `2 x`).
- `prim-function-call` : `{f}({a:expr})` (= `f(x)` collé, pas `f (x)`).

Le flag `glued` est minoritaire et toujours explicite dans le YAML. C'est
**l'unique source de complexité d'espacement** — pas d'usine à gaz.

**Cas d'usage révélateur** — `cos2(x)` vs `cos 2x` vs `cos(x)2` :

| Input | Tokens | Result |
|---|---|---|
| `cos2(x)` | `\cos·2·(·x·)` (collé) | `\cos^{2}(x)` (= via `prim-function-superscript`) |
| `cos 2x` | `\cos· ·2·x` (espacé+collé) | `\cos 2x` (= via `function-implicit` + `prim-implicit-product`) |
| `cos(x)2` | `\cos·(·x·)·2` (collé partout) | `\cos(x)^{2}` (= via `prim-function-call` + `prim-expr-num-superscript`) |

Les 3 inputs **se distinguent par le glued only**. Le reste du moteur
est neutre aux espaces. **3 règles primitives** suffisent à couvrir
ces 3 patterns + leurs variants.

### Principe 6 — Anchor unifié 3-formes + prefix-match dynamique

Tout anchor accepte 3 formes d'appel équivalentes :
- `KEYWORD a b` (= sans parens, espaces)
- `KEYWORD(a b)` (= parens, espaces)
- `KEYWORD(a, b)` (= parens, virgules)

Le matcher détecte automatiquement la forme et adapte. Cf. ADR [2026-05-26-Refactor-anchor-callable-unified](2026-05-26-Refactor-anchor-callable-unified.md).

**Prefix-match dynamique 3-chars** : `som`, `inte`, `ome` (≥ 3 caractères) résolvent automatiquement vers `somme`/`integrale`/`omega` via un mécanisme de préfixe au tokenize-level. Élimine la majorité des aliases statiques dans `fr.yml`.

## Format YAML cible complet

### Règle simple

```yaml
- id:       frac-explicit
  pattern:  "frac {num} {den}"
  produces: expr
  emit:     "\\frac{$num}{$den}"
  tests:
    - "frac 1 2 => \\frac{1}{2}"
```

### Slots typés

```yaml
- id:       interval-closed
  pattern:  "[ {a} ; {b} ]"
  produces: interval
  emit:     "[$a;$b]"

- id:       set-union
  pattern:  "{a:set} U {b:set}"   # accepte aussi 2 intervals via Set ⊃ Interval
  produces: set
  emit:     "$a \\cup $b"

- id:       forall-in-set
  pattern:  "forall {var:letter} in? {set}"
  produces: expr
  emit:     "\\forall $var \\in $set"
```

### Slots répétés (matrices, listes)

```yaml
- id:       matrix-row
  pattern:  "{cells:expr}+ sep=','"   # ou syntaxe à finaliser
  produces: matrix-row
  emit:     "$cells | join: ' & '"
```

### Optionnels

```yaml
- id:       sum-with-eq-optional
  pattern:  "sum {var:letter} =? {from} {to} {body}"
  produces: expr
  emit:     "\\sum_{$var=$from}^{$to} $body"
```

### Classes (= références à `locale/fr.yml`)

```yaml
- id:       lim-with-fillers
  pattern:  "lim <filler>? {var:letter} <to>? {a} {body}"
  produces: expr
  emit:     "\\lim_{$var \\to $a} $body"
```

## Concepts YAML attendus dans `data/concepts/`

Existants à porter au nouveau format :
- `analyse.yml`, `congruences.yml`, `fractions.yml`, `funcdef.yml`,
  `limites.yml`, `logique.yml`, `norme.yml`, `sommes.yml`, `vecteurs.yml`

À créer (= ouverture de la composition) :
- `intervalles.yml` (= 4 variants `[a;b]`, `]a;b]`, etc., chacun produces `interval`)
- `ensembles.yml` (= `set-finite {1,2,3}`, `set-union`, `set-inter`, `set-minus`)
- `relations.yml` (= `=`, `<=>`, `=>`, `in`, `notin` comme règles primitives binaires)
- `primitives.yml` (= portage de `PrimitiveRules.cs` C# vers YAML, sauf si on garde le C# pour la perf)
- `trig.yml` (= sin x+1 format `\sin` expr) — débloqué après bascule

## Tradeoff & alternatives écartées

### **Garder le RewriteEngine POC actuel et patcher**
Rejetée. Le scheduling actuel a un défaut fondamental (= ordre des phases) qui demande une refonte du moteur central. Patcher = continuer à empiler.

### **Inverser primitives ↔ anchors dans le scheduling**
Rejetée. Test mental sur `frac n n+1` montre que ça casse les cas où primitives doivent matcher en premier.

### **Compute 2 passes GD/DG + best wins**
Considérée puis dépassée. Le scan-keywords + scoping (Principe 3) est plus déterministe et propre.

### **Beam search pur sans scan-keywords préliminaire**
Considérée. Mais le beam search explose combinatoirement sur de longues entrées. Le scan-keywords ancre la résolution autour des structures sémantiques connues.

### **Garder partial match optionnel**
Rejetée. Le typing flow EST l'usage principal — partial match doit être au cœur du moteur, pas une option.

### **Repartir sur ShapeMatcher legacy + collisions C#**
Rejetée. C'était l'engine v1 dont on s'est explicitement débarrassé en Phase D-6. Pas de retour en arrière.

## Conséquences

### Code

- **Nouveau moteur** : `core-csharp/src/MathCursor.Engine/Rewriting/` refondu from scratch.
  - Estimation : ~600-800 LOC moteur final (= scan + scope + matcher + multi-chains + emit).
- **Supprimer** : le `RewriteRuleLoader` actuel + `ShapeParser` simplifié.
- **Garder** : `Tokenization/`, `Vocabulary/`, `Normalization/`, `MathEngine.cs` (= façade délégation).

### YAML

- **Migration tous concepts** au format natif `pattern:` + `produces:` (= déjà fait ce 28 mai).
- **Ajout** de `intervalles.yml`, `ensembles.yml`, `relations.yml` (~80 lignes).
- **Update `fr.yml`** pour ajouter quelques aliases manquants (`appartient`, `notin`, `∉`).

### Tests

- Test corpus existant (= 167 tests engine) repris.
- Tests cibles spécifiques pour valider la composition :
  - `1/sum k 0 n f(k)` → `\frac{1}{\sum_{k=0}^{n} f(k)}`
  - `1/sum` (typing) → `\frac{1}{\sum_{\square=\square}^{\square}\square}`
  - `lim x sum k 0 n f(k)` → `\lim_{x \to \sum_{k=0}^{n} f(k)} \square` (= ambiguïté typing)
  - `forall x R U [0;1] P(x)` → `\forall x \in \mathbb{R} \cup [0;1], P(x)`
  - `forall x R - {0}, P(x)` → `\forall x \in \mathbb{R} \setminus \{0\}, P(x)`
  - `frac n n+1` → `\frac{n}{n+1}`
  - `1/2 + 3/4` → 3 lectures (strict, slurp-num, slurp-den)

### API publique

- `MathEngine.BuildDefault` inchangée (= délègue au nouveau RewriteEngine).
- `EngineResult` inchangée (= TopLatex, Collisions, IsComplete, RuleId).
- Pas de breaking change pour l'adapter VSTO.

## Plan d'implémentation suggéré

| Phase | Livrable | LOC |
|---|---|---|
| **0** | ADR (= ce document) + audit final POC actuel | 0 |
| **1** | Squelette du nouveau RewriteEngine + Items typés + Match basique | ~150 |
| **2** | Scan-keywords + scoping inside-out | ~150 |
| **3** | Partial match avec slots `\square` | ~50 |
| **4** | Multi-chains beam search + scoring | ~100 |
| **5** | Anchor unifié 3-formes | ~50 |
| **6** | Prefix-match 3-chars dynamique | ~80 |
| **7** | Migration YAML existants + nouveaux concepts ensembles/intervalles | ~80 lignes YAML |
| **8** | Bascule MathEngine.BuildDefault + suppression POC actuel | ~30 LOC |
| **9** | Tests cibles + validation usage Word réel | — |

**Total estimé** : ~600 LOC moteur + 80 lignes YAML. Sprint dédié de 3-4 jours concentrés.

## Validation post-implémentation

1. Tous les tests YAML inline (= `tests:` co-localisés) passent → 100 %.
2. Les 7 tests cibles ci-dessus passent.
3. Usage Word réel sur 1 cours de maths réel — pas de bug bloquant.
4. `MathCursor.Engine.dll` final < 1 MB embedded (= moteur compact).

## Quand reprendre ce brief

- Sprint dédié quand la tête est fraîche (= pas en fin de session marathon).
- Idéalement après pause de 1-2 jours pour digérer.
- L'utilisateur reste maître de l'ordre des phases (= peut commencer par Phase 4 multi-chains si critique).

## Plan en cours — état d'avancement global MathCursor

| # | Chantier | Statut |
|---|---|---|
| 1 | hardcoded FR → YAML | ✅ |
| 2 | Normalizer dédié | ✅ |
| 3 | Pre-passes → IPreResolver (puis supprimés) | ✅ → archivé |
| 4 (POC) | RewriteEngine POC + bascule Phase D-6 | ✅ |
| **5** | **Nouveau moteur from-scratch (= cet ADR)** | **proposé ici** |
| 6 | Découper `SuggestionService` god class | à faire |
