# Feat — Chaînes multilignes : architecture eqArr « A-discipliné » (hors moteur)

**Date :** 2026-06-10
**Kind :** Feat
**Température :** forte (architecture du chantier multiligne)
**Statut :** acté
**Supersedes :** — (remplace de fait l'approche mergers-cascade de DocMath v1, jamais actée en ADR unique)
**Lié à :** POCs `Host/Blocks/ChainTablePoc.cs` + `ChainEqArrPoc.cs` (M0, à retirer après M2) ; [2026-06-10-Fix-anchor-cc-deletion-hygiene.md](2026-06-10-Fix-anchor-cc-deletion-hygiene.md) (préalable) ; garde moteur relation-en-tête (commit 1a44dc5) ; [2026-05-19-Feat-anchor-cc-pattern.md](2026-05-19-Feat-anchor-cc-pattern.md)

## Citations acté

> « pour moi le multiligne doit vivre en dehors [du moteur], au même titre
> que le merge » — cadrage initial.
> « j'ai un peu peur du flow de modification avec ça.. déjà le cc merdouille
> pas mal à la suppression » — critère de décision = la MANIPULATION.
> « à l'usage édition ligne à ligne je le sens mieux mais j'ai peur du
> tableau fantôme qui traîne :D » — puis, POC tableau en main : « je me
> demande si ça va pas être encore plus foireux que le multiligne :D même
> si visuellement c'est super ok ! »
> « oui ça a l'air moins foireux sur l'eqArr » ; double alignement testé :
> **« non c'est bien ça fonctionne comme ça »** — utilisateur, 2026-06-10.

## Contexte

Les chaînes de raisonnement (lignes commençant par `=`, `⟺`…, alignées
sur la ligne précédente) sont LE pavé qui a généré la dette DocMath v1 :
cascade de mergers opérant sur les OMaths (8 fichiers Merging/, absorption
/ré-absorption, sidecars). L'utilisateur a volontairement retiré `<=>` etc.
du moteur forest : le multiligne vit HORS moteur.

Deux options physiques comparées par POC (M0) dans Word :

- **B — tableau invisible, un OMath par cellule** : alignement par bord de
  colonne, édition ligne à ligne native. Verdict POC : « visuellement
  super ok » MAIS manipulation jugée risquée (squelette de tableau fantôme
  après suppression, double terrain miné cellules+CC, mécanique tableau).
  **Rejetée.**
- **A-discipliné — UN OMath `<m:eqArr>`** (equation array natif) : verdict
  POC **validé**, y compris le **double alignement** (chaîne d'équivalences
  `x+2=5 ⟺ x=3 ⟺ 2x=6` : 2 marques `&` par ligne, les ⟺ alignés entre eux
  ET les =, colonne connecteur vide en 1ʳᵉ ligne OK). **Retenue.**

## Décision

### 1. Principes

- **P1 — Le moteur ne sait rien du multiligne.** Forest analyse UNE
  expression ; relation en tête d'entrée = « erreur » (garde commit
  1a44dc5). Les connecteurs (`<=>`, `=>`, `⟺`…) vivent dans la table du
  module Blocks.
- **P2 — La source est la vérité.** Le bloc = UN OMath eqArr + UN anchor
  CC dont le Tag porte la LISTE des sources lignes (JSON). TOUTE opération
  (ajouter, modifier, supprimer une ligne) = **re-génération complète** du
  bloc depuis les sources puis remplacement — un seul chemin de code,
  jamais de chirurgie d'OMath, jamais de cascade de mergers.
- **P3 — Append-only en v1.** Extension par le bas uniquement ; pas de
  fusion de blocs, pas d'insertion au milieu, pas de chaîne dans une liste.

### 2. Structure physique validée (POC)

```xml
<m:oMath><m:eqArr>
  <m:e> [&] [lhs] [&] [=rhs] </m:e>     ligne 1 (connecteur vide)
  <m:e> [⟺] [&] [lhs] [&] [=rhs] </m:e> lignes suivantes
</m:eqArr></m:oMath>
```
Marques `&` = `<m:r><m:t>&</m:t></m:r>` ; même nombre de segments sur
chaque ligne (colonnes vides permises). OMML construit par GREFFE des
fragments `LatexToOmml.Convert(segment)` — le moteur fournit le LaTeX de
chaque segment, le module Blocks assemble.

### 3. Modules (`adapter-vsto/Host/Blocks/`)

- `RelationMarkers.cs` (pur) — table des marqueurs de tête de ligne et de
  leur LaTeX : `=` `<` `>` `<=`/`≤` `>=`/`≥` `!=`/`≠` `<=>`/`⟺`/`⇔`
  `=>`/`⟹`/`⇒` (+ extensible).
- `RelationLineDetector.cs` (pur) — « la ligne commence par un marqueur »
  → (marqueur, latex du marqueur, reste). Testé sans Word.
- `ChainController.cs` (Word) — M2 : au commit d'une ligne marquée dont le
  ¶ précédent porte une équation à nous → crée le bloc (lit la source de
  l'équation via son Tag, scinde au signe top-level, ré-émet en eqArr) ou
  étend le bloc existant (Tag type chaîne → +1 ligne → re-génération).
  `UndoRecordScope` par opération.

### 4. Métadonnée bloc

Tag CC étendu : `{v, handle_id, type:"chain", lines:[{m:"⟺", src:"x=3"},…],
latex, omml_hash, …}` — rétro-compatible (absence de `type` = équation
simple). L'hygiène anchor (H1-H3) fonctionne sur le bloc SANS modification
(un OMath + un CC = suppression atomique déjà validée).

## Phasage

- **M0 — POCs** ✅ (tableau rejeté, eqArr validé simple + double alignement).
- **M1 — Détection pure** : RelationMarkers + RelationLineDetector + tests.
- **M2 — Chaîne v1** : création du bloc + append, Ctrl+Z par étape ;
  retrait des boutons POC.
- **M3 — Vie du bloc** : revert du bloc (sources → N lignes texte),
  modification d'une ligne (réouverture/re-génération), suppression.
- **M4 — Ergonomie** : répétition auto du marqueur à l'Entrée, double
  Entrée pour sortir (brief ergonomie).

## Tradeoff & alternatives écartées

- **B tableau** : rejetée sur l'axe manipulation (POC) — fantôme de
  tableau, frontières cellules+CC, mécanique lourde — malgré le meilleur
  flow d'édition ligne à ligne.
- **Mergers-cascade DocMath v1** : opérait sur les OMaths au lieu des
  sources → complexité explosive ; éliminé par P2.
- **Taquets de tabulation** : position d'alignement à re-mesurer au reflow,
  fragile. Écarté dès la discussion.

## Validation post-fix

M2 livré : taper `f(x) = 2x+2-2` (commit), ⏎ `= 2x` → bloc aligné créé ;
⏎ `= 2·x` → ligne ajoutée ; Ctrl+Z remonte étape par étape ; Backspace ×2
sur le bloc → tout disparaît (hygiène) ; équivalences `⟺` : double
alignement conforme au POC.
