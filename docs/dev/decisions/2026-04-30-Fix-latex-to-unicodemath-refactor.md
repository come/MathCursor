# Fix — Refactor `LatexToUnicodeMath` en parser → AST → émetteur (anti-absorption Word OMath)

**Date :** 2026-04-30
**Kind :** Fix
**Température :** molle
**Statut :** acté

## Décision

Réécrire `LatexToUnicodeMath.cs` (couche 1, core) en remplaçant l'approche
actuelle (regex + StringBuilder à un seul passage) par un pipeline en trois
étapes :

1. **Parse** : tokenize le LaTeX en AST minimal couvrant les structures que
   notre `LatexRenderer` émet (`Frac`, `Sup`, `Sub`, `Sqrt`, `Cmd`,
   `Group`, `Lit`, `Seq`).
2. **Emit** : visitor qui produit du UnicodeMath en connaissant le contexte
   (nœud parent, voisin droit immédiat).
3. **Préserve** la liste des remplacements littéraux et l'ordre fragile
   `\int` avant `\in` (régression v0.5.2 connue).

L'émetteur applique trois règles dépendantes du contexte que l'approche
regex ne pouvait pas porter :

- **Single-char shortcut** : `x^{2}`, `x_{n}`, `\int_{0}^{1}` — l'argument
  d'un `^` / `_` qui est un seul caractère ASCII alphanum est émis nu, sans
  parens. `x^2` et non `x^(2)`. Word BuildUp gère cela nativement, et c'est
  ce qui supprime les parens visibles autour des exposants/indices simples
  (cf. bug v0.5.3 reporté le 30-04).
- **Anti-absorption après structure** : si une fraction (ou sub/sup avec
  parens) est immédiatement suivie d'un token tight (lettre, chiffre,
  parenthèse), l'émetteur insère un caractère séparateur Word-friendly
  pour éviter que Word absorbe le token suivant dans le dénominateur.
- **Intégrale propre** : `∫_<low>^<high> body` avec single-char shortcut
  appliqué aux bornes. Avec bornes multi-char, parens conservées :
  `∫_(low)^(high)`. Le bug image `∫(0)^(1)` (perte du `_`) disparaît avec
  le single-char shortcut + ordre d'émission garanti par l'AST.

## Pourquoi

### Bug racine : 3 symptômes, une cause

L'approche actuelle (regex + StringBuilder) émet systématiquement des `(...)`
autour des arguments de `^`, `_`, `\frac`, etc. Ce wrapping uniforme cause
trois bugs distincts en prod :

1. **Bug image v0.5.3** : `\frac{1}{2}x` → `(1)/(2)x` → Word lit `1 / ((2)x)`
   et place `x` au dénominateur avec parens visibles.
2. **Bug puissances** : `x^{2}` → `x^(2)` → Word affiche `x^(2)` parens
   visibles autour du `2`.
3. **Bug intégrale** : `\int_{0}^{1}` → `∫_(0)^(1)` → Word ne reconnaît pas
   `∫_(...)` comme intégrale-avec-borne-inférieure (l'underscore est perdu)
   et affiche `∫(0)^(1)`.

### Pourquoi pas un patch ciblé

L'approche "tweak les regex" testée dans le ticket précédent ne tient pas :
chaque correction crée une régression sur un autre cas (cf. ordre fragile
`\int` / `\in` qui a déjà nécessité un commentaire pour ne pas re-régresser).
Le code a atteint la limite de ce qu'un seul passage regex peut gérer
proprement quand les règles deviennent dépendantes du contexte (voisin
gauche/droit, profondeur d'imbrication, single vs multi-char).

Un parser → AST → émetteur :

- Sépare la **reconnaissance** de la **génération** : on peut tester chaque
  étape isolément.
- Permet l'**inspection du contexte** lors de l'émission (le visitor sait
  ce qu'il y a après).
- Reste **léger** : ce n'est pas un parser LaTeX complet, juste les
  structures qu'on émet — couverture inférieure à celle de pandoc, plus
  simple à porter en C# .NET Standard 2.0.

### Pourquoi le single-char shortcut

UnicodeMath de Word a une règle native : après `^` ou `_`, un seul
caractère ASCII alphanum est traité comme exposant/indice atomique. Pas
besoin de délimiteur. Quand on force `(...)`, Word affiche les parens
selon la version (Word desktop 2019+ tend à les rendre visibles dans
certains contextes).

Le commentaire actuel dans `LatexToUnicodeMath.cs:243-247` ("on garde
toujours les parens, même pour les single chars : sinon `cos^2(x)` est
parsé en `cos^{2(x)}`") sera ré-évalué par tests d'intégration Word
manuels. L'hypothèse : la règle single-char du parser UnicodeMath est
correcte sur Word desktop moderne ; le bug `cos^{2(x)}` venait d'un
contexte spécifique, pas d'une absorption universelle.

### Pourquoi l'anti-absorption pour les fractions

`\frac{1}{2}x` produit aujourd'hui `(1)/(2)x` que Word interprète comme
"fraction avec dénominateur `(2)x`" (le `x` collé est avalé). C'est le bug
de l'image utilisateur. La nouvelle stratégie : après une fraction multi-
char, si un token tight suit, insérer un séparateur invisible reconnu par
Word UnicodeMath comme borne de structure.

Candidats de séparateur (à valider en test Word) :
- Espace simple ` ` — sûr mais peut affecter l'espacement visuel selon
  contexte.
- Zero-Width Joiner `‍` — invisible, peut ne pas être respecté par
  BuildUp.
- Function Application `⁡` — opérateur math invisible standard
  Unicode, semble respecté par Word.
- Espace fin ` ` — visible mais minimal.

V1 : on commence avec l'espace simple. Si l'espace casse un autre cas, on
bascule sur U+2061. À tester manuellement dans Word avant commit.

## Conséquences

### Code (couche 1 — core)

- **`LatexToUnicodeMath.cs`** réécrit avec :
  - `LatexLexer` (interne static) — produit les tokens (`\cmd`, `{`, `}`,
    `^`, `_`, char).
  - `LatexParser` (interne) — produit l'AST (Seq, Frac, Sup, Sub, Sqrt,
    Cmd, Group, Lit).
  - `UnicodeMathEmitter` (interne static) — visitor qui sort la string,
    avec contexte voisin droit pour l'anti-absorption.
  - API publique inchangée : `static string Convert(string latex)`.
- Liste `LiteralReplacements` conservée (lettres grecques, relations,
  symboles). Ordre fragile `\int` / `\in` documenté et testé.
- Map `CombiningAccents` conservée (vec, hat, bar…).
- Map `SetLetterMap` conservée (`\mathbb{R}` → ℝ).
- Régressions historiques (v0.5.2 `\int → ∫`, vec multi-char `\vec{AB}`)
  préservées par les tests dédiés.

### Tests

- **`LatexToUnicodeMathTests.cs`** étendu avec **3 couches × 5 cas** :
  - Couche (a) — sortie `LatexRenderer` validée séparément (déjà couverte
    par `LatexRendererTests`, on ne duplique pas).
  - Couche (b) — alternatives `AlternativeGenerator` (déjà couverte par
    `AlternativeGeneratorTests`).
  - Couche (c) — sortie `LatexToUnicodeMath.Convert` étendue avec :
    - `\frac{1}{2}x` n'absorbe pas le `x` (bug image).
    - `x^{2}(x+1)` ne contient pas de `(2)` visible.
    - `\int_{0}^{1} f(x) dx` produit `∫_0^1` ou `∫_(0)^(1)` propre selon
      single/multi-char, avec `_` et `^` préservés.
    - Régressions historiques : `\int`, `\subset`, `\vec{AB}` continuent.
    - Tests existants `Converts_expected` adaptés au nouveau format de
      sortie (`(1)/(2)` reste valide pour le multi-char ; `x^{2}` →
      `x^2` change si on adopte single-char shortcut).
- Tests existants à mettre à jour si la stratégie single-char modifie la
  sortie attendue. Liste à actualiser au moment de l'implémentation,
  validation utilisateur déjà acquise pour le revert UI (les parens
  visibles sont un bug à supprimer).
- **Checklist Word manuelle** obligatoire avant commit final, listant les
  cas du bug ticket (image, puissances, intégrale) tapés directement dans
  un document Word avec MSI rebuilt.

### Hors scope V1

- ❌ Parser LaTeX complet (environnements imbriqués, macros utilisateur).
  On ne couvre que les sorties de notre `LatexRenderer`.
- ❌ Optimisation perf : la conversion s'exécute une fois par insertion
  Word, pas un goulot.
- ❌ Conservation API legacy `Convert(string)` retournant exactement
  l'ancien format. Les tests de sortie peuvent évoluer (et c'est le but).

## Validé par l'utilisateur

Direction du fix :

> "et reproduire aussi les probleme ?" (TDD obligatoire)
> "dans les tests je veux tester la formule proposée / les desambiguités /
> la formule convertie" (3 couches de tests)

Choix propre vs rapide :

> "ok, tout propre stp / P2 refactor supersedes"

## Statut

acté
