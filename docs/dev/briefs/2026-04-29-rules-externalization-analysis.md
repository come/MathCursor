# Analyse — Externalisation des règles du moteur lattice

**Date :** 2026-04-29
**Auteur :** come + agent
**Statut :** **note d'analyse** (pas un brief d'implémentation, pas une ADR
actée). Sert à éclairer une décision future sur l'extensibilité du moteur.

---

## 1. La question

> *« j'aimerai etudier la possibilité d'etendre les regles de maniere simple
> par l'utilisateur, peut etre qu'avant ca il y'a la notion d'encoder nos
> regles en json ou yml mais il faudrait voir si la grammaire le permet »*

Deux questions imbriquées :

1. **Peut-on encoder les règles existantes en JSON/YAML ?** (data-fication
   d'un code aujourd'hui en C#)
2. **Peut-on permettre à un utilisateur d'ajouter ses propres règles ?**
   (extensibilité externe)

La réponse à (2) dépend de (1) — pas de point d'entrée externe sans format
déclaratif. Cette analyse traite (1) en priorité, (2) en conséquence.

## 2. Qui est l'« utilisateur » qui étend les règles ?

Avant de plonger dans la technique, **identifier la cible change tout** :

| Profil | Probabilité (MathCursor phase 1) | Conséquences sur le format |
|--------|----------------------------------|----------------------------|
| come (dev principal) | très élevée — tu modifies le vocab souvent | Format léger, validation au build, pas besoin d'UI |
| Prof beta-testeur | moyenne — quelques demandes ad-hoc ("mediane manque") | Pull request texte ou config sidecar |
| Élève / lycéen | très faible — cible produit, pas dev | Si jamais : bouton "ajouter mot-clé" en UI, pas un fichier |

**Lecture honnête** : la phase 1 a un seul vrai « extender » (toi). Les profs
beta émettront des demandes mais tu seras l'opérateur. **Donc pas besoin de
plugin runtime, d'UI d'édition de règles, ni de DSL puissant.** L'objectif
réel est : *éviter de recompiler / reéditer du C# pour ajouter du vocab*.

Cette contrainte simplifie énormément le design. **Le reste du document
suppose ce profil.** Si la cible change (ex: ouvrir l'extension aux profs
sans toi dans la boucle), il faudra ré-arbitrer.

## 3. Inventaire des règles (résumé exécutif)

Après cartographie exhaustive (cf. agent Explore), 58 endroits de "règle"
identifiés, classés en trois catégories :

### 3.1. DATA pure (6 endroits, ~10% du moteur)

Listes/dictionnaires sans logique, immédiatement externalisables :

- `Vocabulary.Keywords` (51 entrées : `somme→sum`, `lim`, `frac`, …)
- `Vocabulary.Functions` (15 entrées : `sin`, `cos`, `ln`, …)
- `Vocabulary.Greek` (24 entrées : `alpha`, `beta`, …)
- `Vocabulary.MultiCharOps` (7 entrées : `<=`, `->`, `//`, …)
- `Vocabulary.SingleOps` (string de 18 chars)
- `Vocabulary.TightOpChars` (string de 5 chars)

**Effort externalisation** : 1 fichier YAML + 1 loader + EmbeddedResource
déjà configuré dans le `.csproj` (Data/yaml_domains/ infrastructure
préparée). **~2h de travail.**

### 3.2. HYBRIDE (8 endroits, ~15%)

Data + logique de matching plus ou moins entrelacée :

- Pondérations Lexer (formule `18 + 3·n` pour idents, weights par EdgeType)
- Liste caractères alphabétiques (a-z + accents FR)
- Modificateurs d'ensemble (`R*`, `R+`, `R-`)
- Brackets d'intervalle (`[a,b[`)
- Détection contextuelle `U` entre intervalles
- Trigger ambig V/E/U (= "suivi d'espace ou EOF")
- Délimiteurs canonical-set
- Mutations source (`V→forall`, `R→bbR`)

**Effort externalisation** : ~1 jour par règle, demande à isoler le pattern
de matching de la logique de décision. **Faisable mais coûteux.**

### 3.3. RÈGLES écrites en code (≈ 36 endroits, ~62%)

⚠️ **Re-catégorisation honnête (correctif après revue)** : ce que j'avais
initialement classé "code pur" mélangeait deux choses très différentes —
des **règles métier écrites en switch/if** (qui sont de la data déguisée)
et le **moteur d'exécution réel** (algorithmes incompressibles). Quand on
sépare proprement :

- **Précédence opérateurs** (Expr/Term/Postfix) — table `op → niveau`,
  externalisable trivialement (Pratt parser data-driven).
- **8 grammaires de scope** (lim/sum/int/sqrt/frac/vec/forall/exists) —
  table `keyword → [args, ast_type]`, déjà identifié comme externalisable
  en §4.2.
- **11 substitutions LaTeX** (`*→\cdot`, `/→\frac`, `<=→\leq`,
  `exp→e^`, `union→\cup`, …) — templates `op → format string`.
- **RenderFunc parens auto** — switch `type(arg) → wrap_strategy`.
- **5 patterns d'ambiguïté** (AB, ABC, x²/x_2, V, E) — patterns + tableaux
  d'alternatives, exprimables en DSL léger.
- **Number-tight = exposant** — pattern conditionnel + action.
- **Notation intervalle française** — flag boolean dans render template.
- **Pondérations Lexer** (formule `18+3n` pour idents) — config dict.

**Effort externalisation** : l'effort *par règle* est trivial une fois
l'interpréteur en place. L'effort *de construire l'interpréteur* (un
dispatcher de patterns + évaluateur de templates) est ~2-3 jours pour
~300-500 lignes de C#. **Bien moins que "1 mois" écrit dans la version
initiale.**

### 3.4. MOTEUR réel (≈ 8 endroits, ~13%)

Algorithmes incompressibles, **pas des règles** :

- Lattice Dijkstra top-K
- Algorithme rightmost ambig + cascade
- `IsTightAdjacent`, `IsAlphabetic` (helpers de scan)
- `IsKwCanon`, helpers de matching token
- Anti-FP V/E/U (logique trigger contextuelle)

**Effort externalisation** : non pertinent. Ce code n'a pas vocation à
bouger ; il *exécute* les règles, il n'en est pas une.

## 4. Trois niveaux possibles d'externalisation

### Niveau 1 — Vocabulary YAML *(rentable)*

**Quoi :** déplacer les 6 listes DATA dans `data/vocabulary.yaml`, charger
au démarrage. Le code consomme la même API qu'aujourd'hui (les statiques
deviennent peuplées au lieu d'être litérales).

**Format proposé :**

```yaml
# data/vocabulary.yaml
keywords:
  - canonical: sum
    aliases: [somme, sum]
  - canonical: lim
    aliases: [lim, limite]
  - canonical: frac
    aliases: [frac]
  # …

functions:
  trig: [sin, cos, tan, cot, sec, csc]
  hyper: [sinh, cosh, tanh]
  log: [ln, log, exp]
  other: [min, max, det]

greek:
  - alpha
  - beta
  # …

operators:
  multi_char:
    "<=": leq
    ">=": geq
    "->": to
    # …
  single: "+-*/^_=<>()[]{},|;:"
  tight: "+-*/^"
```

**Gains :**

- Ajout d'un mot-clé/fonction/grecque = éditer le YAML, pas de rebuild.
- Validation possible : un script Python/C# qui charge le YAML et vérifie
  l'absence de doublons, la cohérence des canonicals, etc.
- Sépare data de code. Les profs/contributeurs peuvent proposer des PR
  sans toucher au C#.

**Coûts :**

- ~2h pour le loader.
- Risque : faute de frappe dans le YAML → erreur runtime. Mitigation : les
  tests existants chargent le vocab et plantent au démarrage si invalide.

**Verdict :** **fais-le quand tu en as l'envie.** Faible risque, gain réel
en flexibilité.

### Niveau 2 — Scope-table déclarative *(envisageable)*

**Quoi :** la grammaire des 8 scopes (lim, sum, int, sqrt, frac, vec,
forall, exists) suit un pattern ultra régulier dans `ParseScope`. On
pourrait la décrire en table :

```yaml
# data/scopes.yaml
- canonical: lim
  ast_type: Lim
  args:
    - { name: var,    parser: argument }
    - { name: target, parser: argument, prefix_op: "->", optional_op: true }
    - { name: body,   parser: body }

- canonical: sum
  ast_type: Sum
  args:
    - { name: var,   parser: atom }
    - { name: start, parser: argument, prefix_op: "=", optional_op: true }
    - { name: end,   parser: argument }
    - { name: body,  parser: body }

# …
```

Le parser devient un mini-interpréteur de cette table. Avantage : ajouter
un nouveau scope = 5 lignes de YAML + 1 nœud AST + 1 entrée renderer. Pas
de modif Parser.

**Coûts :**

- Refactor non-trivial du `switch` de `ParseScope` en table-driven.
- Les scopes "irréguliers" résistent : `forall`/`exists` injecte un `\in`
  optionnel, `vec` consomme une chaîne d'idents collés, `int` n'a pas de
  variable nommée (juste low/high/body). Soit on enrichit le vocabulaire
  de la table (`special_consume`, `inject_keyword`), soit on garde ces
  cas en code et la table couvre 70%.
- Le moteur table-driven est plus lent à débugguer qu'un switch C#
  (stacktrace moins parlante).

**Verdict :** **différer.** Aujourd'hui tu ajoutes ~1 scope par mois.
L'investissement (1-2 jours) ne se rembourse qu'au bout de 6+ scopes
ajoutés. À reprendre quand le rythme s'accélère ou si plusieurs personnes
contribuent en parallèle.

### Niveau 3 — Render templates *(utile en passant)*

**Quoi :** la fonction `RenderBin` est une suite d'`if` qui mappe
opérateur → template LaTeX. Externalisable :

```yaml
# data/render-templates.yaml
bin:
  "*_implicit": "{lhs}{rhs}"
  "*":          "{lhs}\\cdot {rhs}"
  "/":          "\\frac{{{lhs}}}{{{rhs}}}"
  "<=":         "{lhs} \\leq {rhs}"
  ">=":         "{lhs} \\geq {rhs}"
  "!=":         "{lhs} \\neq {rhs}"
  "//":         "{lhs} // {rhs}"
  "union":      "{lhs} \\cup {rhs}"
  "inter":      "{lhs} \\cap {rhs}"
  "+":          "{lhs}+{rhs}"
  "-":          "{lhs}-{rhs}"
```

Idem pour `Sup`, `Sub`, `Frac`, `Sqrt`, etc.

**Gains :** lisibilité, possibilité de personnaliser la convention
typographique (ex: rendre `\cdot` au lieu de juxtaposition pour la mult
implicite, version pour exam où l'élève doit voir le `\cdot`).

**Coûts :** ~3h. Reste les cas spéciaux (`exp→e^`, `RenderFunc` parens
contextuelles) qui restent en code.

**Verdict :** **bon ROI si tu veux multi-conventions** (lycée FR vs prépa
vs autre style). Sinon overkill.

### Niveau 4 — DSL de patterns *(à éviter sauf pivot)*

**Quoi :** un langage déclaratif pour exprimer des patterns comme
`f:x->expr → FuncDef`, `ABC → triangle/angle/vec`, `x² → x^2`. Genre
PEG ou règles façon TextMate / regex sur tokens.

**Coûts :**

- 1-2 mois de R&D + outillage.
- Cycle d'adoption long (debug, validation cross-cas).
- Risque de divergence entre règles user et règles built-in.

**Gains :** tu deviens éditeur d'écosystème (chaque prof peut publier ses
règles). Mais on est très loin de la phase 1.

**Verdict :** **non.** Si jamais l'extensibilité par tiers devient le
business model (cf. memory `project_business_model.md` qui parle de pivot
non-OSS), à reposer la question.

## 5. Tradeoffs transverses

### Performance

Charger un YAML au démarrage = ~10 ms sur fichier <100 KB. Négligeable
puisque l'add-in démarre avec Word (plusieurs secondes incompressibles).
**Pas un blocage.**

### Validation

Plus on externalise, plus on a besoin d'un validateur côté CI :

- Niveau 1 : check JSON-Schema simple sur le YAML.
- Niveau 2-3 : tests unitaires qui chargent la table et confirment qu'elle
  produit la même grammaire qu'avant (golden test sur le corpus existant).
- Niveau 4 : impossible de garantir qu'une règle user ne casse pas le
  système. Sandbox + score de qualité comme un store d'extensions.

### Maintenance

Le code C# d'aujourd'hui est lisible parce que les règles sont écrites
linéairement avec des commentaires explicatifs (cf. `Parser.cs:228-242` :
8 lignes de doc sur la règle Number-tight). Une table YAML enlève ce
contexte. **Risque : perte de la documentation au fil de l'eau.** Mitigation :
champ `comment:` dans le YAML.

### Couplage AST

Externaliser la grammaire (Niveau 2) crée un couplage entre le YAML et les
classes AST C#. Renommer `Lim` en `Limit` casse le YAML. **Acceptable** si
on traite le YAML comme du code source (commit, review).

## 6. Recommandation

**Phase 1 (à court terme) : Niveau 1 uniquement.**

Externaliser `Vocabulary.cs` en `data/vocabulary.yaml`. Loader au démarrage,
les statiques deviennent peuplées au lieu d'être litérales. ~2h de travail,
~0% de risque, gain immédiat : tu peux ajouter `mediane`, `cardinal`, etc.
sans toucher au C#.

**Phase 2 (déclencheur : >5 contributeurs OU >2 scopes/mois) : Niveau 3
puis 2.**

D'abord les render-templates (peu de risque), puis la scope-table si le
rythme d'ajout justifie le refactor.

**Phase 3 (déclencheur : pivot business sur l'écosystème) : Niveau 4.**

Pas avant.

## 7. Questions ouvertes pour décider

1. **As-tu déjà ressenti la friction de modifier `Vocabulary.cs` plus que
   3-4 fois ce mois-ci ?** Si oui → Niveau 1 maintenant. Sinon → différer
   d'autant.

2. **Veux-tu une convention typographique alternative** (ex: render
   "rigoureux" pour exam où la mult implicite devient `\cdot`) ? Si oui →
   Niveau 3 utile. Sinon → laisse le renderer en C#.

3. **Les profs beta-testeurs sont-ils OK pour ouvrir un PR / soumettre un
   YAML par mail** ? Ou veulent-ils un mode "fichier de config dans
   `%APPDATA%`" qui se charge sans rebuild ? Le second oblige à du
   hot-reload (~1 jour de plus), le premier est gratuit.

4. **Combien de scopes prévois-tu d'ajouter d'ici 6 mois ?** Si <5,
   Niveau 2 n'est pas rentable.

## 8. Plan concret si on tranche pour Niveau 1

```
1. Créer data/vocabulary.yaml (1 fichier, structure §4 niveau 1).
2. Créer Lattice/VocabularyLoader.cs : charge le YAML embedded → peuple
   les statiques de Vocabulary.cs au premier accès (lazy + thread-safe).
3. Mettre à jour MathCursor.Core.csproj : EmbeddedResource sur le YAML
   (déjà configuré pour data/yaml_domains/, étendre à data/vocabulary.yaml).
4. Tests : ajouter VocabularyLoaderTests qui vérifie qu'après chargement
   les valeurs matchent l'ancien hardcodé (test de non-régression).
5. ADR : `2026-XX-XX-Meta-vocabulary-yaml-extraction.md` (Kind=Meta,
   Température=molle).
6. Commit unique. Pas de feature ergo, juste data move.
```

Dépendance externe : choisir un parseur YAML léger pour .NET Standard
2.0. Options : `YamlDotNet` (mature, ~200 KB), ou JSON pur si tu veux
éviter la dépendance (le format §4 niveau 1 marche aussi en JSON).
**Recommandation : JSON** pour zéro dépendance, c'est le format déjà
utilisé dans `data/glue_words.json`, `operators.json`, etc.

## 9. Note finale

Une grosse partie de la valeur de l'externalisation **ne vient pas de
l'extensibilité par utilisateur**, mais de la **séparation lisible
data/code** dans ton propre repo. Niveau 1 t'apporte ça
immédiatement. Le reste (Niveau 2, 3, 4) ne s'amortit que si l'extensibilité
externe devient un objectif produit explicite — ce qui n'est pas le cas
aujourd'hui (cf. memory `project_ergo_brief.md` : la cible est l'élève PAP
+ profs beta, pas un écosystème).

**Verdict honnête : ne sur-investis pas.** Niveau 1 maintenant si la
friction existe, Niveau 3 si tu veux multi-convention, le reste différé
sine die.
