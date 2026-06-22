# Fix — Bornes d'intégrale en indice/exposant à droite (subSup), pas empilées

**Date :** 2026-06-22
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** —

## Citation acté

> « les integrales sont rendus avec les bornes en dessous et au dessus alors que
> souvent c'est legerement plus bas en theorie.. tu vois le truc ? on peut faire
> quelque chose ? » — utilisateur, 2026-06-22.

> « je veux bien voir le code ! » — utilisateur, 2026-06-22 (validation du plan).

## Contexte

`LatexToOmml.Nary` produisait `<m:naryPr><m:limLoc m:val="undOvr">` pour **tous**
les opérateurs n-aires (∑, ∏, ∫, ∬, ∭, ∮). Conséquence : les bornes d'une
intégrale étaient **empilées au-dessus/au-dessous** du signe ∫, au lieu d'être en
**indice/exposant à droite** (`∫_a^b`).

Convention typographique mathématique standard (et comportement natif de Word
quand on insère une intégrale à la main) : bornes **à droite** pour les
intégrales, **empilées** pour sommes et produits. Public lycée → on suit la
convention attendue dans un cours.

## Décision

`limLoc` devient **dépendant de l'opérateur** :

- **Intégrales** (`∫ ∬ ∭ ∮`) → `limLoc = "subSup"` (bornes à droite).
- **Sommes / produits** (`∑ ∏`) → `limLoc = "undOvr"` (empilées, inchangé).

`Nary(...)` prend un paramètre `limLoc` explicite, passé par chaque `case` du
dispatch LaTeX.

## Tradeoff & alternatives écartées

- **Garder `undOvr` partout** : statu quo, mais rendu non conforme à l'usage math
  pour les intégrales. Écarté.
- **`subSup` partout** : casserait le rendu attendu des sommes/produits (∑ avec
  `k=1` / `n` empilés est la norme). Écarté — la distinction est intrinsèque à
  l'opérateur.
- **Mode display vs inline** : certains rendraient l'intégrale en gros centré avec
  bornes empilées en mode display. Hors périmètre (prise de notes inline lycée) ;
  `subSup` est le bon défaut ici.

## Conséquences

- **Code touché** : `serialization/src/MathCursor.Serialization/LatexToOmml.cs`
  (signature `Nary` + 6 call sites). Aucune autre couche : `OmmlToOMathBuilder`
  (adapter) lit déjà `limLoc` → `nary.SubSupLim` et propage sans modification.
- **Tests** : `LatexToOmmlTests` — assertion `limLoc=undOvr` sur `\sum`, nouveau
  cas `limLoc=subSup` sur `\int_{-T/2}^{T/2}` (3/3 vert).
- **API publique** : inchangée. Pas de nouvelle dépendance.
- **Règles MC** : aucune.

## Addendum 2026-06-22 — espaces avant `dx` + parité de l'aperçu WpfMath

> « entre la formule et le dx y'a bcp d'espaces j'ai l'impression » +
> « le prérendu wpfmath est encore dans l'ancienne facon de faire » —
> utilisateur, 2026-06-22.

Deux corrections complémentaires, **sans toucher la donnée** (`symbols.json`) ni
les 450 fixtures partagées — le template LaTeX `\int_{a}^{b} f \, dx` reste
conventionnel ; ce sont les **deux renderers** qui l'interprétaient mal.

### 1. Trop d'espace avant `dx` (couche OMML)

`LatexToOmml.ParseSeq` émettait **chaque** espace : le run du corps d'intégrale
valait `espace littéral` + `\,`→**U+2009** (espace fine) + `espace littéral` =
**3 espaces** avant `dx`. En math, des espaces consécutifs ne valent qu'un seul
(WpfMath les ignore d'ailleurs — d'où un aperçu déjà correct côté espacement).

Fix : `CollapseSpaces` appliqué dans `Flush()` — collapse tout run d'espaces
consécutifs (`U+0020`, fine `U+2009`, moyenne `U+2005`) en **un** seul, en
gardant la **fine** si le run en contient une (rendu intégrale propre). Les
espaces simples (ex. `1\,cm`) sont inchangés. Couche corrigée = le sérialiseur
OMML (le vrai bug), pas le template ni les fixtures.

### 2. Aperçu WpfMath encore en bornes empilées

L'aperçu popup (`FormulaControl` WpfMath) empilait les bornes : WpfMath, comme
TeX, met les grands opérateurs en `\limits` (display style). `\nolimits` **n'est
pas supporté** (rend un visuel vide — sondé empiriquement via
`WpfMathRenderProbeTests`, probes 52→56). Astuce retenue : `WpfMathAdapter`
**groupe** l'opérateur — `{\int}_a^b` attache les scripts à un atome *ordinaire*
qui les place à droite (probe 55 confirmée). Regex `IntBoundsRegex` :
`(\\(?:o?int)(?:\\int)*)(?=[_^])` → `{$1}`, donc couvre `\int`, `\oint`, et le
run `\int\int` issu de la dégradation `\iint`/`\iiint`. Intégrale **sans** bornes
non touchée (pas de `_`/`^`).

### Conséquences (addendum)

- **Code** : `serialization/.../LatexToOmml.cs` (`CollapseSpaces`/`IsSpace` +
  `Flush`), `adapter-vsto/.../UI/WpfMathAdapter.cs` (`IntBoundsRegex` + étape 6).
- **Tests** : `Integral_body_collapses_redundant_spaces` (OMML, zéro doublon
  d'espace) ; `Integral_bounds_grouped_for_subSup` +
  `Integral_without_bounds_not_grouped` (adapter) ; MAJ
  `Integrals_with_bounds_degrade_to_chained_ints` (`\iint` → `{\int\int}_…`).
  Suites : serialization 63/63, adapter 363/363.
- **Écarté** : modifier `symbols.json` + régénérer les 450 fixtures (le LaTeX
  conventionnel n'est pas le bug) ; `\nolimits` côté WpfMath (non supporté).

## Addendum 2026-06-22 (bis) — espace de tête + borne basse de l'aperçu

> « il reste un espace entre integrale et son corps. + wpfmath la borne du bas
> est un peu trop à droite, comme si il y'avait un espace aussi » — utilisateur,
> 2026-06-22.

### 3. Espace résiduel ∫ ↔ corps (OMML)

Le template `\int_{a}^{b} {2}` a un espace littéral avant l'opérande →
`CollapseSpaces` le réduisait à UN espace, mais il restait en **tête** du corps
(Word affichait un blanc entre ∫ et l'opérande). Fix : `TrimRunEnds` rogne
l'espace de tête du 1er run texte et de queue du dernier, **uniquement pour le
corps `<m:e>` du n-aire** (les espaces internes « f \, dx » restent). Verrouillé
par `Integral_body_collapses_redundant_spaces` (corps ≠ démarre par un blanc).

### 4. Borne basse trop à droite (aperçu WpfMath)

Avec `{\int}` (atome ordinaire), WpfMath aligne indice ET exposant à droite de
façon **symétrique** → la borne basse ne « rentre » pas sous la pente de
l'intégrale (paraît trop à droite). `\textstyle`/`\nolimits` non supportés
(rendu vide). Fix : injecter un **kern négatif `\!\!`** en tête de l'indice
braced (`IntBoundsBracedSubRegex` : `\int_{` → `{\int}_{\!\!`). Validé sur borne
longue (`-T/2`) et courte (`0`) — probes 65/66. Cas restants (exposant avant
indice, indice non-braced) → groupage seul sans kern (`IntBoundsRegex`).

### Conséquences (addendum bis)

- **Code** : `LatexToOmml.cs` (`TrimRunEnds`, appelé dans `Nary`),
  `WpfMathAdapter.cs` (`IntBoundsBracedSubRegex` avant `IntBoundsRegex`).
- **Tests** : `Integral_body_collapses_redundant_spaces` (renforcé : pas
  d'espace de tête) ; adapter `Integral_braced_sub_grouped_and_kerned`,
  `Integral_supfirst_grouped_without_kern` + MAJ des expectations `\iint`/`\iiint`
  (`{\int\int}_{\!\!…}`). Suites : serialization 64/64, adapter 363/363.

## Validation post-fix

- **Unit** : `Sum_with_bounds` (undOvr), `Integral_bounds_are_placed_to_the_right_subSup`
  (subSup), `Integral_without_bounds_hides_them`, `Integral_body_collapses_redundant_spaces`
  (collapse + pas d'espace de tête) ; adapter : `Integral_braced_sub_grouped_and_kerned`,
  `Integral_supfirst_grouped_without_kern`, `Integral_without_bounds_not_grouped`.
- **Manuel produit** : dans Word, `\int_{-T/2}^{T/2} f` → bornes à droite du ∫,
  opérande collée à l'opérateur (pas de blanc), un seul espace fin avant `dx` ;
  aperçu popup : bornes à droite, borne basse rentrée sous la pente ;
  `\sum_{k=1}^{n}` → bornes empilées (inchangé).
