# Meta — YAML collision DSL (brief gardé pour plus tard)

**Date :** 2026-05-23
**Kind :** Meta
**Température :** provisoire
**Statut :** proposé
**Supersedes :** —
**Lié à :** [2026-05-22-Feat-engine-poc-isolation.md](2026-05-22-Feat-engine-poc-isolation.md) (P11),
[2026-05-22-Feat-popup-ide-style.md](2026-05-22-Feat-popup-ide-style.md) (P14+P15)

## Citation acté

> « 3 mais garde le brief pour plus tard » — utilisateur, 2026-05-23

(Option 3 = ne pas implémenter maintenant ; revoir quand on a 10+ détecteurs.)

## Contexte

Après P31 (= refacto `ICollisionDetector` + scores YAML), 7 détecteurs C#
co-existent dans `MathCursor.Engine.Collision.Detectors/` :

- `VecLetterDetector`, `DotVecDetector`, `TripleUpperDetector`,
  `VectorCoordsDetector`, `LetterSupSubDetector`,
  `SlurpFractionDetector`, `SlurpSupSubDetector`

Chaque détecteur = ~60 LOC C#. Brief v5 §6 prévoit que les collisions
soient **déclarées en YAML** (= "collisions déclarées, pas auto-détectées").
Le user a demandé un mockup pour évaluer la rentabilité.

Conclusion : on ne fait **pas maintenant**, mais on garde le design pour
réimplémenter quand le nombre de détecteurs justifie un DSL générique.

## Décision

Conserver le design ci-dessous. Re-évaluer son implémentation quand :
- ≥ 10 détecteurs C# co-existent OU
- Un user non-dev veut ajouter une collision sans toucher du C# OU
- On a besoin d'overrides par locale (= différentes collisions pour FR vs EN)

## Design YAML proposé

Format `data-v2/collisions/*.yml`, un fichier par règle (= 1 collision = 1 YAML).

### Mockup 1 — collision simple sur operand isolé

```yaml
id: vec
description: vecteur
score: 70                    # optionnel, sinon lookup collision-scores.yml
scope: operand-isolated      # = l'expression entière doit être ce pattern
pattern:
  - word:
      is: vec-candidate       # = prédicat builtin (1 lettre OU 2 maj)
      capture: name
emit: "\\vec{$name}"
```

### Mockup 2 — pattern infixe au sein d'un operand

```yaml
id: dot-vec
description: produit scalaire vecteurs
score: 75
scope: per-operand            # = scan chaque operand
pattern:
  - word: { is: vec-candidate, capture: a }
  - symbol: "."
  - word: { is: vec-candidate, capture: b }
emit: "\\vec{$a} \\cdot \\vec{$b}"
```

### Mockup 3 — pattern avec groupe + items répétés

```yaml
id: vec-coords
description: vecteur (coordonnées)
score: 60
scope: per-operand
pattern:
  - word:
      is: vec-candidate
      capture: name
      glue-next: true           # = collé au token suivant
  - open-delim: "("
  - items:
      min: 2                    # = minimum 2 coordonnées
      seps: [",", ";"]          # = séparateurs autorisés
      capture: coords
  - close-delim: ")"
emit: |
  \vec{$name}\begin{pmatrix}{$coords | join: " \\\\ "}\end{pmatrix}
```

### Mockup 4 — cross-operand (= absorbe l'operand suivant)

```yaml
id: fraction-slurp
description: fraction (slurp dénominateur)
score: 80
scope: cross-operand
pattern:
  current-operand:
    - any: { capture: num, until: "/" }   # = tout avant le `/`
    - symbol: "/"
    - any: { capture: den }                # = tout après
  next-op:
    in: ["+", "-"]
    capture: connector
  next-operand:
    any: { capture: rest }
absorbs: 1                                  # = consomme l'operand+1
emit: "\\frac{$num}{$den$connector$rest}"
```

### Mockup 5 — alt avec re-render via flag

```yaml
id: letter-sub-number
description: indice (au lieu d'exposant)
score: 75
scope: per-operand
pattern:
  contains:                                # = quelque part dans le bucket
    - word: { length: 1, kind: letter, glue-next: true }
    - number: { capture: digits }
emit:
  re-render-operand: true                  # = parse l'operand, re-emit avec...
  options: { prefer-subscript: true }      # ← option passée au LatexEmitter
```

## Vocabulaire du DSL

| Élément | Sémantique |
|---|---|
| `scope: operand-isolated` | match seulement si toute la source = ce pattern |
| `scope: per-operand` | scan chaque operand top-level (= défaut) |
| `scope: cross-operand` | regarde `operand[i] + op + operand[i+1]` |
| `word: { ... }` | match `Token.Kind=Word` + contraintes |
| `is: vec-candidate` | prédicat builtin (= 1 lettre OU 2 maj) |
| `length: N`, `kind: letter`, `all-upper: true` | prédicats sur `Text` |
| `glue-next: true` | doit être collé au token suivant (= no Sep) |
| `capture: name` | exposé comme `$name` dans emit |
| `items: { min, seps, capture }` | liste répétée séparée |
| `absorbs: N` | combien d'operands suivants sont consommés |
| `emit: "..."` | template string avec `$name` + filters `&#124; join: ...` |
| `emit: { re-render-operand: ..., options }` | re-render via emitter avec options |

## Avantages vs hardcoded C#

| Aspect | C# détecteur | YAML collision |
|---|---|---|
| Lecture par un non-dev | ❌ | ✅ |
| Ajout d'un cas | ~60 LOC + class | ~10 LOC YAML |
| Score | hardcoded ou lookup | inline ou lookup |
| Tests co-localisés (= `tests:` dans le YAML) | non | ✅ |
| Override par utilisateur final | non | ✅ (= édite le YAML) |

## Coût d'implémentation estimé

| Composant | LOC |
|---|---|
| Parseur DSL (POCO YamlDotNet + interpréteur pattern) | ~150 |
| Détecteur générique `YamlCollisionDetector(spec)` | ~100 |
| Suppression des 7 détecteurs C# actuels | **−400** |
| **Net** | **−150 LOC** |

Net négatif (= moins de code) + format déclaratif. Donc rentable **dès** qu'on a 7+ détecteurs (= déjà le cas).

## Raison du report

User : « on reverra activement ». Pour l'instant les 7 détecteurs C# fonctionnent
et passent 211 tests. Le DSL est un investissement design (= choix du format
exact, tests, validation par corpus) qui n'est pas urgent tant que l'ergo
popup est validée et que le moteur tient.

## Quand reprendre ce brief

- Avant d'ajouter le 8e détecteur (= seuil de rentabilité)
- Si on veut un mode "user-pluggable" (= ajout règle sans recompilation)
- Si on doit faire des overrides par locale FR/EN/DE/ES

## Plan en cours — état d'avancement

Designé en P31 + reporté :
- [x] Mockup 5 patterns (= cover les 7 détecteurs actuels)
- [x] Vocabulaire DSL spécifié
- [x] ADR provisoire (= ce document)
- [ ] Implémentation P32+ (= différée)
