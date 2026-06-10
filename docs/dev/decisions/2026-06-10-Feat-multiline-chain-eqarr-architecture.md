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

### 5. Systèmes d'équations « { » (extension actée le même jour)

> « si au commit tu check au dessus et y'avait une ligne qui commence
> par { , on essaie de les merger ? » — utilisateur, validé.

Le `{` ouvrant vit AUSSI hors moteur (un `{` non fermé est déjà « erreur »
côté forest). Même machinerie que les chaînes :
- ligne `{ 2x+y = 5` au commit → bloc SYSTÈME d'une ligne : `<m:d>`
  accolade ouvrante / fermante invisible (le `{…┤` que DocMath simulait en
  linéaire) enveloppant un eqArr ; Tag `type:"system"`.
- ligne SANS marqueur committée dans le ¶ adjacent sous un système →
  absorbée (+1 ligne, re-génération).

### 6. Décisions d'ergonomie (validées utilisateur, 2026-06-10)

1. **Règle d'arrêt** : absorption uniquement si ¶ ADJACENT ; une ligne
   vide casse la chaîne/le système — « double entrée nickel c'est
   naturel ». (M4 : répétition auto du marqueur à l'Entrée.)
2. **Repli sans équation au-dessus** : équation autonome avec le marqueur
   RENDU (« ⟺ x=3 ») — « si l'utilisateur l'a écrit il veut le voir ».
3. **Alignement dans les systèmes** : chaque ligne scindée au signe
   top-level, les `=` alignés dans l'accolade — « oui alignement des = ».

### 7. Schéma de métadonnée bloc (Tag CC)

`MCMeta.Type` = null (équation simple, rétro-compat) | "chain" | "system".
`Steno` = les lignes SOURCES jointes par saut de ligne (exactement ce que
l'utilisateur a tapé, marqueurs compris) ; `Latex` = les LaTeX par ligne
(SANS marqueur, tels que choisis dans la popup) joints pareil. La
re-génération ne RE-ANALYSE jamais : elle réutilise les LaTeX choisis
(le choix popup de l'utilisateur est préservé) ; les marqueurs sont
re-dérivés des lignes steno par le détecteur (pur, déterministe).

## Phasage

- **M0 — POCs** ✅ (tableau rejeté, eqArr validé simple + double alignement).
- **M1 — Détection pure** : RelationMarkers + RelationLineDetector + tests.
- **M2a — Chaînes v1** : création du bloc + append, repli autonome, Ctrl+Z
  par étape. **M2b — Systèmes** : ouvreur `{` + absorption adjacente ;
  retrait des boutons POC après validation Word.
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

## Amendements (2026-06-10, soir — validés en session)

### A1 — Alignement : UN SEUL `&` par ligne

> « c'est <=> qui aligne pas j'ai l'imp » — utilisateur, 2026-06-10

Les marques `&` d'un eqArr ALTERNENT (1ʳᵉ = point d'alignement, 2ᵉ =
séparateur) : le layout 3 colonnes du M2 alignait le début du lhs, pas
le `=`. Retour au design du brief DocMath 2026-04-30 : un seul `&` devant
le signe, connecteur ⟺ en tête de colonne gauche
(`ChainComposer.RowSingleAlign`). Display forcé sur les blocs (les `&`
n'agissent qu'en display), Justification au défaut. Détail dans
`word-api-helpers.md` §3.

### A2 — Saut de ligne au merge : remplacer depuis le DÉBUT DU ¶

La plage de remplacement démarrait à `cc.Range.Start` ; la frontière sdt
vit une position avant → Word gardait un ¶ squelette vide (paras-diag :
« paras 2→2 » malgré la marque dans la plage) → le bloc descendait d'une
ligne par merge. Fix : `ReplaceStart` = début du ¶ du bloc (garde-fou
prose inline). + démasquage `Font.Hidden=0` avant suppression (Word
refuse de supprimer du texte caché). Détail dans `word-api-helpers.md` §3.

### A3 — Règle système : `{` requis sur la ligne courante

> « si du coup on change legerement le systeme, il faut un { sur la ligne
> courante ET un { sur la ligne du dessus pour merger » — utilisateur,
> 2026-06-10

Supersede l'absorption M2b des lignes nues : une ligne SANS `{` sous un
système n'est plus absorbée (commit normal). `{ …` avec système au-dessus
= EXTENSION ; sans = CRÉATION. `TryAbsorbIntoSystemAbove` supprimé,
`CommitSystemOpener` → `CommitSystemLine` (create-or-extend).

### A4 — M4 livré : flow multiligne au commit

> « est ce qu'on pourrait se brancher à l'insert, si multiligne
> (separateur identifié : => <=> { etc) alors on insere un nouveau
> paragraphe et on pré place le séparateur » … « yes parfait » —
> utilisateur, 2026-06-10

Au commit d'une ligne à séparateur : nouveau ¶ + séparateur pré-placé
(marqueur tapé pour les chaînes, `{ ` pour les systèmes), DANS le même
UndoRecordScope. Sortie : Entrée sur la ligne séparateur-seul l'efface et
consomme la frappe (`ConversionController.TryExitFlowOnEnter`). Équations
simples : comportement inchangé.

### A5 — Popup : aperçu de merge intégré

Chaque candidat est rendu DANS l'aperçu du bloc cible : « ⋯ » (s'il y a
plus d'une ligne au-dessus), puis la VRAIE ligne du dessus grisée, puis le
candidat ; accolade `{` étirée à gauche pour les systèmes. Assemblage WPF
pur (StackPanel de rendus WpfMath), lignes lues du Tag
(`ChainController.ProbeMergeAbove`).

### A6 — Affinage A1 par preuve empirique (docx de variantes)

> « V5 aligné et à gauche : good / V4 aligné et centré » — utilisateur,
> 2026-06-10

Le single-`&` universel d'A1 désalignait les lignes à connecteur. Docx de
variantes XML (V1-V5, mêmes lignes ⇔ en 5 structures) : single-`&`
désaligné (V1-V3), double-`&` aligné (V4 centré, V5 jc=left à gauche —
retenu). Layout final ADAPTATIF dans `ComposeChain` : sans connecteur =
2 colonnes / 1 `&` (prouvé B-series) ; avec connecteur = 3 colonnes /
2 `&` (prouvé V4/V5). Le `jc=left` posé naturellement par Word est
conservé. Détail : `word-api-helpers.md` §3.
