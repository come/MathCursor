# Feat — Juxtaposition tight = groupement, ops explicites = PEMDAS (avec alt désambig)

**Date :** 2026-04-30
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** [2026-04-30-Feat-revert-tight-as-grouping.md](2026-04-30-Feat-revert-tight-as-grouping.md)

## Décision

Pour les opérateurs `/`, `^`, `_` tight, le rhs absorbe la **chaîne de mult
implicite tight** (juxtaposition collée) UNIQUEMENT. Les opérateurs explicites
`+`, `-`, `*` cassent la chaîne — précédence math standard préservée.

L'élargissement aux ops tight (comportement V1 historique de
`tight-as-grouping`) reste **accessible via cascade de désambiguïsation** :
quand l'élargissement diffère du défaut, l'alternative est exposée dans la
popup pour switcher rapidement sans avoir à reparenthéser.

| Source | Default | Alt désambig (cascade) |
|--------|---------|------------------------|
| `AB/BC` | `\frac{AB}{BC}` (chaîne implicite groupée) | — (pas d'op explicite à élargir) |
| `1/2x` | `\frac{1}{2x}` (`2x` = chaîne implicite) | — |
| `1/x+1` collé | `\frac{1}{x}+1` (PEMDAS) | `\frac{1}{x+1}` |
| `x^a+b` collé | `x^{a}+b` (PEMDAS) | `x^{a+b}` |
| `u_n+1` collé | `u_{n}+1` (PEMDAS) | `u_{n+1}` |
| `cos(x)/sin(x)` | `\frac{\cos(x)}{\sin(x)}` (groupes explicites) | — |
| `A/B/C` collé | `\frac{\frac{A}{B}}{C}` (gauche-assoc) | — |
| `1/2 x` (espace) | `\frac{1}{2}x` (espace casse la chaîne) | — |

Cas non traités par cette règle (préservés par les briefs antérieurs) :
- Scope keywords `frac a b`, `cos x`, `lim x 0 …`, `sum k 1 n …`, `int 0 1 …`,
  `sqrt …`, `racine …` continuent à utiliser `ParseTightChain` (consomme tight
  ops + adjacent factors). C'est la convention "scope = prend tout ce qui colle"
  spécifique à ces keywords.

## Pourquoi

### Ce qu'on résout

Cette ADR clôt la trilogie de décisions sur le tight-grouping :
1. **29-04 V1 (retracté)** : tight = groupement total (incluant ops `+`/`-`/`*` tight).
   `1/x+1` → `\frac{1}{x+1}`. Reproché : "pas mathématiques", divergence avec
   PEMDAS standard, perturbant pour l'élève qui apprend la précédence math.
2. **30-04 revert (retracté ici)** : précédence math standard pure. `AB/BC` → `\frac{AB}{B} \cdot C`.
   Trop strict : `AB/BC` est typographiquement compris comme deux blocs (juxtaposition
   = "un mot"), forcer l'élève à parenthéser casse la fluidité sténo.
3. **30-04 final (cette ADR)** : nuance sur le **type de mult**.
   - Mult implicite (juxtaposition) → groupée (deux lettres collées =
     un identifiant logique en première intention).
   - Mult/op explicite → PEMDAS standard (l'élève a tapé `*` ou `+`,
     il distingue les opérandes, on respecte la précédence math).

### Pourquoi exposer l'élargissement en alt

L'élève qui veut `\frac{1}{x+1}` peut le saisir via parens `1/(x+1)` ou
sélectionner l'alternative dans la popup quand il tape `1/x+1`. La popup
reste un filet de sécurité : si la PEMDAS par défaut ne convient pas, le
switch est à un clic, pas à une re-saisie. Cf. validation utilisateur :
*"je veux bien garder la regle tight en desambiguisation comme ca on
change vite si soucis"*.

### Pourquoi aligner `^` et `_` sur `/`

V1 du 29-04 distinguait `^`/`_` (qui groupaient déjà tout via
`ParseTightChain`) de `/` (qui ne groupait pas). L'asymétrie créait des
incohérences mémorisables : *pourquoi `x^a+b` groupe et `1/x+1` non ?*

Aligner les trois opérateurs sur la même règle (chaîne implicite par défaut,
ops via cascade) rend le modèle prédictible. La typographie qui aurait
justifié un traitement spécial pour `^`/`_` (rendu structurel élevé/abaissé)
n'est pas suffisante pour mériter une règle distincte côté parser — la
cascade compense côté UX.

## Conséquences

### Code (couche 1 — core)

- **`Parser.cs`** :
  - Nouvelle propriété publique `TightExtendsToOps` (default `false`) qui
    bascule entre les deux modes. Mode `true` réservé à
    `AlternativeGenerator.ScanTightChainExtension` pour générer l'alt.
  - Nouvelle helper privée `ParseTightImplicitMultChain` : version
    restreinte de `ParseTightChain` qui consomme uniquement la mult
    implicite tight, pas les ops tight.
  - Nouvelle helper privée `ParseSupSubArg` : entrée d'argument pour `^`
    et `_`, identique à `ParseArgument` mais utilise
    `ParseTightImplicitMultChain` (default) ou `ParseTightChain` (alt).
  - `ParseTerm` : pour `/` tight, branche sur `ParseTightImplicitMultChain`
    (default) ou `ParseTightChain` (alt). Pour `*` ou `/` non-tight,
    comportement inchangé (`ParsePostfix`).
  - `ParsePostfix` `^`/`_` : appelle `ParseSupSubArg` au lieu de
    `ParseArgument`. `ParseArgument` reste utilisé tel quel par les
    scope keywords (frac, cos, lim, sum, int, sqrt, FuncDef body).

- **`AlternativeGenerator.cs`** :
  - Nouvelle constante `RuleTightChainExtension`.
  - Nouvelle méthode `ScanTightChainExtension` : re-parse `source` avec
    `TightExtendsToOps=true` et propose `altLatex` en cascade si différent
    du `topLatex` default.
  - Priorité 4 (basse) — les ambig structurelles (V/E, AB, vec coords)
    gardent la main si elles coexistent.
  - Spot exposé en TOP-LEVEL (toute la formule). V2 ciblera la sous-
    expression précise si plusieurs élargissements coexistent.

### Tests

- **`LatexRendererTests`** :
  - Tests inversés du revert : `AB/DC` → `\frac{AB}{DC}`, `AB/BC` →
    `\frac{AB}{BC}`, `1/2x` → `\frac{1}{2x}`.
  - Tests PEMDAS conservés : `1/x+1` → `\frac{1}{x}+1`, `1/x +1` →
    `\frac{1}{x}+1`, `1/2 x` → `\frac{1}{2}x`.
  - Nouveaux tests `^`/`_` : `x^a+b` → `x^{a}+b`, `u_n+1` → `u_{n}+1`,
    `x^2n` → `x^{2n}` (chaîne implicite groupée), `x^(a+b)` → `x^{a+b}`
    (parens explicites).
  - Test `A/B/C` left-assoc et `cos(x)/sin(x)` non-régressés.

- **`AlternativeGeneratorTests`** :
  - 3 tests positifs : `1/x+1`, `x^a+b`, `u_n+1` proposent l'alt élargi.
  - 3 tests négatifs : `AB/BC`, `1/x +1` (espace), `1/x` simple ne
    proposent PAS `RuleTightChainExtension`.

### Hors scope V1

- ❌ Stratification `Term` en `LooseTerm` / `TightTerm` (cas asymétrique
  `a 2x/y` où le lhs aspire `a·2·x` au lieu de `2·x`). Cf. brief 29-04
  §2.3 — refactor invasif, accepté tel quel.
- ❌ Cibler la sous-expression précise dans la cascade. V1 expose
  l'alt comme "toute la formule re-parsée" ; si plusieurs élargissements
  sont possibles, l'utilisateur en a un seul (le tout).
- ❌ Étendre la cascade aux scope keywords (`frac`, `cos`, etc.). Pour
  ces derniers, le tight chain reste le comportement "scope" attendu.

## Validé par l'utilisateur

Demande de retour à la règle tight :

> "AB/BC collé │ \frac{AB}{BC} (règle tight) => je veux ca !"

Précision sur la nuance implicite/explicite :

> "non c) c'est un probleme de groupement AB => groupé a*b pas groupé"

Confirmation logique deux lettres = unité :

> "deux lettres collées surtout Majuscule on peut considerer en premiere
> intention que c'est qu'une variable donc tu confirmes qu'on AB/AC c'est
> juste une fraction"

Décision d'aligner `^`/`_` sur `/` + alt cascade :

> "on va aligner pour le choix par defaut, par contre je veux bien garder
> la regle tight en desambiguisation comme ca on change vite si soucis"

Autorisation de coder :

> "yes go code !"

## Statut

acté
