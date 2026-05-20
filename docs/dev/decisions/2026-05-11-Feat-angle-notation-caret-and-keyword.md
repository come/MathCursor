# Feat — Notation d'angle au clavier : `^A`/`^ABC` et `angle(...)`

**Date :** 2026-05-11
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Citation acté

> "j'aimerai qu'on passe sur les angles: ^ABC ou ^A doivent marcher
> ou angle(ABC) [...] ^a doit marcher aussi ? ^AB doit montrer la
> popup avec un carré pour la lettre manquante ?"
> — utilisateur, 2026-05-11
>
> "oui ok"

## Décision

Trois syntaxes au clavier pour noter un angle, toutes rendues avec
les commandes LaTeX `\hat` (1 lettre) ou `\widehat` (2+ lettres) :

### Syntaxe 1 : `^` en position "fresh"

`^` au début d'une zone OU précédé d'un espace ou d'un opérateur,
suivi d'identifiants alpha consécutifs sans espace :

| Source | Cas | Sortie |
|---|---|---|
| `^A` / `^a` | 1 lettre | `\hat{A}` / `\hat{a}` — direct, pas de popup |
| `^AB` | 2 lettres | **popup**, default `\widehat{AB\square}`, alt `\widehat{AB}` |
| `^ABC` | 3 lettres | `\widehat{ABC}` — direct |
| `^ABCD+` | 4+ lettres | `\widehat{ABCD\ldots}` — direct |

Position "fresh" = première position de la zone, OU précédée d'espace,
OU précédée d'un opérateur math (`+`, `-`, `=`, `*`, `/`, etc.).

Si `^` est précédé d'un atome (lettre, chiffre, `}` de fin
d'argument), c'est l'opérateur **exposant** historique (`x^a` → `x^{a}`).
Pas de changement de comportement sur ce cas.

### Syntaxe 2 : mot-clé `angle(...)`

Mot-clé `angle` (FR & EN) suivi de `(<lettres>)` — même mapping :

| Source | Cas | Sortie |
|---|---|---|
| `angle(A)` | 1 lettre | `\hat{A}` |
| `angle(AB)` | 2 lettres | popup, default `\widehat{AB\square}`, alt `\widehat{AB}` |
| `angle(ABC)` | 3 lettres | `\widehat{ABC}` |
| `angle(ABCD+)` | 4+ lettres | `\widehat{ABCD\ldots}` |

### Désambig 2-lettres (`\square` placeholder)

Pour `^AB` ou `angle(AB)`, l'ambig est :

- **Default** : `\widehat{AB\square}` — invite visuelle à compléter le
  3ème point (cas typique du student en cours de frappe).
- **Alt** : `\widehat{AB}` — angle littéral à 2 lettres (rare mais
  valide en notation FR).

L'utilisateur voit le `\square` rendu dans la popup ; si c'était
voulu en 2 lettres il switch sur l'alt, sinon il continue à taper
(`^ABC`) et l'ambig disparaît au prochain refresh.

## Pourquoi

- **Notation française angle.** Les élèves de lycée écrivent les
  angles avec un "chapeau" sur 1 ou 3 lettres ($\hat{A}$, $\widehat{ABC}$
  où B est le sommet). Aucun raccourci clavier actuel ne couvre ce
  cas → ils tapent `\widehat{ABC}` à la main, friction énorme.
- **`^` réutilisé en mode "fresh"** : moins de keystrokes que
  `angle(ABC)` (5 vs 11 chars pour 3 lettres). Cohérent avec la
  notation papier où on écrit le chapeau en premier.
- **Mot-clé `angle(...)` en alternative** : plus explicite/lisible,
  utile quand `^` serait ambigu avec l'exposant (rarement le cas en
  position fresh mais coût zéro de le supporter).
- **Square placeholder pour `^AB`** : guide visuel actif, transforme
  l'ambiguïté en aide à la saisie au lieu d'une devinette.

## Alternatives écartées

- **Une seule syntaxe `angle(...)`** : trop verbeux pour un cas
  fréquent (3 lettres = 11 chars `angle(ABC)` vs 4 chars `^ABC`).
- **`^` toujours angle** : casse complètement les exposants
  existants (`x^2` ne marcherait plus). Le contexte "fresh" est la
  garantie d'intercompatibilité.
- **Détection auto sur 3 lettres majuscules `ABC` sans le `^`** :
  trop intrusif, faux positifs sur `ABC = points d'un triangle`,
  `ABC = matrice 1×3`, etc. Le marqueur `^` ou `angle()` est
  explicite.
- **Restriction majuscules** : initialement proposé, retiré sur
  retour user. La notation $\hat{a}$ est valide en math (variable
  unitaire, vecteur normalisé, etc.).

## Scope V1

**Inclus :**
- Détection source `^[A-Za-z]+` en position fresh.
- Détection source `angle([A-Za-z]+)`.
- Popup ambig à 2-lettres avec default `\widehat{AB\square}`.
- Rule `angle-notation` avec priorité haute (entre les rules
  structurantes V→∀ et les locales AB→vec).

**Exclus V1 (à voir si demande) :**
- `\angle` LaTeX commande (= notation US différente, $\angle ABC$ avec
  petite équerre devant les points). Non utilisée en France lycée.
- Angles orientés (avec arc/flèche). Notation hors programme lycée.
- Variantes `\widehat` à 4+ lettres avec ambig (default `\widehat{ABCD}`
  vs alt `\widehat{ABCD\square}` pour 5). Marginal, pas de signal user.

## Plan d'exécution

1. ADR posée + index.
2. `AlternativeGenerator.cs` : nouvelle rule `ScanAngleNotation`.
   - Détecte `^[A-Za-z]+` source au début ou après espace/op.
   - Détecte `angle([A-Za-z]+)` source.
   - Émet l'AmbiguityMatch avec :
     - 1 lettre / 3+ lettres → 1 seule alt = `\hat{X}` ou `\widehat{XYZ}` (= top direct).
     - 2 lettres → 2 alts : `\widehat{AB\square}` (default), `\widehat{AB}` (alt).
   - RuleId = `angle-notation`, priorité 2 (entre V→∀ et locales).
3. Tests xUnit `AlternativeGeneratorTests.cs` couvrant les 8 cas du
   tableau + cas non-déclenchés (`x^A` = exposant inchangé, `angle`
   sans parens = mot normal, etc.).
4. Tests `LatexRendererTests.cs` que les `\hat`/`\widehat` rendent
   bien le LaTeX attendu (incluant `\square` dans le placeholder).
5. Vérif visuel VSTO : tape `^A`, `^ABC`, `angle(ABC)`, popup `^AB`.

## Risques

- **Faux positif `^` orphelin** : user tape juste `^` sans rien
  derrière. Pas d'ambig émise (pattern exige 1+ lettre). Le caractère
  reste tel quel jusqu'au refresh suivant.
- **Collision avec autres rules** : `^A` au début déclenche l'angle ;
  mais si la zone NER capte plus large (`x ^A` avec `x` à gauche),
  le contexte change. Décision : si `x` est juste à gauche sans
  espace (`x^A`), c'est l'exposant. Avec espace (`x ^A`), c'est
  l'angle. Le test "position fresh" doit être strict sur
  l'espace/op précédent.
- **`angle` en mot normal** : si user écrit du texte `un angle...`
  hors zone math, NER ne déclenche pas, donc pas de risque. Dans
  une zone math, `angle` sans `(` reste literal (pas de match).
