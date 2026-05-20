# Brief — Édition multi-ligne via cascade cross-merge

**Date :** 2026-05-04
**Statut :** rédigé, en attente d'ADR

## Contexte

Aujourd'hui :
- Cross-merge à l'écriture marche : 1 OMath au-dessus + ligne courante avec
  marker → bloc multi-ligne (ADR/brief 30-04).
- Édition d'un OMath multi-ligne existant : clic dessus → « Revenir à la
  saisie » → revert produit `source.Replace("\n", "\r")` = N paragraphes
  Word texte. Le caret atterrit sur la dernière ligne.

Problème : si l'utilisateur édite et fait Enter pour re-merger, **seule la
dernière ligne se convertit**. Les paragraphes au-dessus restent du texte
brut. L'utilisateur doit alors manuellement re-déclencher la conversion sur
chaque ligne, ce qui est punitif.

## Solution

Étendre le cross-merge pour qu'il **cascade** sur les paragraphes au-dessus,
avec deux modes de fonctionnement :

### Mode 1 — Default (frappe neuve, pas d'édition active)

Cascade montante tant qu'on rencontre des paragraphes avec marker align en
tête (`=`, `<=>`, `=>`, `<=` et variantes Unicode/multi-char). Le sommet de
la cascade s'arrête sur :
- Un OMath qu'on possède (= bookmark `mcEq_*`) → absorbé comme actuellement.
- Un paragraphe sans marker → **non absorbé** (la cascade s'arrête juste
  au-dessus).
- Un paragraphe vide → cascade stop net (ligne vide = barrier, cf. brief
  30-04 §3.2).

Ce mode est conservateur : il évite les faux positifs sur du texte normal
qui aurait des paragraphes consécutifs avec markers.

### Mode 2 — Revert mode (édition d'un multi-ligne existant)

Quand l'utilisateur déclenche « Revenir à la saisie » sur un OMath multi-
ligne, on stocke en sus du `_editHandle` actuel un **`_revertedMultiLineZone`**
qui mémorise `[firstParaStart, lastParaEnd]` du revert. Tant que ce champ
est non-null, la cascade :
- Absorbe **tous les paragraphes** dans cette zone (y compris la première
  ligne sans marker, puisqu'on sait qu'elle appartient au bloc original).
- Si l'utilisateur a ajouté des paragraphes hors zone (clic ailleurs, Enter
  qui pousse hors), on bascule en Mode 1 (zone invalidée).

Ce mode garantit qu'un revert + commit produit le même bloc multi-ligne
qu'avant le revert (ou un bloc enrichi/réduit si l'utilisateur a édité).

## Algorithme cascade détaillé

```
Pseudocode :
function cascadeCrossMerge(currentZoneStart, currentZoneEnd, currentSource):
    // Mode 2 prioritaire si actif
    if _revertedMultiLineZone is set AND currentZoneStart in zone:
        return absorbAllParagraphsInZone(_revertedMultiLineZone, currentSource)

    // Mode 1 : cascade conservatrice
    chain = [currentSource]
    cursor = currentZoneStart
    while true:
        prevPara = paragraph immediately above cursor
        if prevPara is empty: break  // ligne vide = barrier
        if prevPara has our OMath at end:
            chain.prepend(omathSource)
            cursor = prevPara.start
            break  // OMath = sommet, absorbé
        if prevPara.text starts with align marker:
            chain.prepend(prevPara.text)
            cursor = prevPara.start
            continue
        break  // paragraphe sans marker = stop, on n'absorbe pas

    if chain.length >= 2:
        return MergeResult(start=cursor, end=currentZoneEnd, source="\n".join(chain))
    return null  // pas de cascade, fallback ParseRelation
```

## Edge cases

1. **Revert puis clic ailleurs** : `_revertedMultiLineZone` est invalidé
   (caret hors zone) → cascade en Mode 1 sur la commit suivante. Donc si
   l'user clic ailleurs sans commit, l'edit-mode est annulé proprement.

2. **Ligne vide ajoutée par l'user dans la zone reverted** : Mode 2
   absorbe tout, incluant le paragraphe vide → la source mergée a une
   ligne vide que la lattice doit gérer (probable : ligne ignorée ou
   ligne vide dans align*). À tester.

3. **User supprime tous les markers, ne reste que des expressions
   isolées** : Mode 2 absorbe quand même → la source mergée a N lignes
   sans markers → le pipeline lattice détecte `MultiLineBlock` ou pas ?
   Cf. `TryParseMultiLineBlock` actuel : il EXIGE un marker en début de
   ligne 2+. Sans marker → null → fallback ParseRelation sur juste la
   ligne 1. Donc concatenation `"a\nb"` parsée comme `a` (ligne 2 perdue).
   Pas idéal. **Décision à prendre : on assume que sans marker dans la
   zone, on retombe sur la ligne courante seule (single-eq) comme si
   l'user avait simplifié son bloc.**

4. **User ajoute un nouveau paragraphe au milieu de la zone** : si nouveau
   paragraphe a un marker, Mode 2 l'absorbe. Si pas de marker, c'est le
   cas 3 → simplification.

5. **Cascade s'arrête sur un OMath SANS marker en début (ex. OMath single
   `a=b` au-dessus d'un `<=> 2x = 4`)** : Mode 1 actuel l'absorbe (existing
   cross-merge). Pas de changement.

6. **Revert dans un doc avec pas mal d'autres OMaths** : `_revertedMultiLineZone`
   ne concerne que la zone reverted. Les autres OMaths restent inchangés.

## Implémentation

Fichiers touchés :

1. **`SuggestionService.cs`** :
   - Ajouter champ privé `_revertedMultiLineZone` (Range ou tuple `(int start, int end)`).
   - Set ce champ dans `OnRevertRequested` quand on détecte que l'OMath
     reverted était multi-ligne (= source contient `\n`).
   - Reset ce champ quand : commit termine (succès ou échec), clic
     hors zone, ou frappe qui sort visiblement de la zone (caret moves
     event, similar à edit handle invalidation).
   - Étendre `TryFindCrossMergeAbove` :
     - Si `_revertedMultiLineZone` actif et range courant chevauche → Mode 2
     - Sinon → Mode 1 (cascade conservatrice, étendue par rapport au cas
       1-OMath actuel pour absorber des paragraphes texte avec markers)

2. **Tests core** : pas de test direct dans MathCursor.Core — la cascade
   est dans le côté adapter. On peut tester la logique de détection via
   un test d'intégration `SuggestionServiceTests` si la baseline existe,
   sinon on valide manuellement en Word.

## Risques

- **Tracking caret-leave-zone fragile** : Word a plein d'events de
  selection. Il faut hook proprement (réutiliser le pattern existant de
  l'edit-mode invalidation sur clic).
- **Mode 1 cascade peut être trop agressif** : un user qui écrit
  légitimement un texte avec des paragraphes consécutifs commençant par
  `=` pourrait voir ses paragraphes mergés. Mitigation : seuls les
  markers ASCII multi-char (`<=>`, `=>`, `<=`) ET le `=` SOLO en début
  déclenchent. Le `=` solo est probablement rare en début de paragraphe
  hors contexte math.
- **Source vide dans store pour les paragraphes texte mergés** : les
  paragraphes texte n'ont pas de source dans le store (pas d'OMath, pas
  de bookmark). On utilise leur texte raw. Au commit, le nouveau OMath
  multi-ligne aura une source = concat des textes raw + `\n`.

## Validation

Critère de succès :

1. User a un OMath multi-ligne existant (3 lignes, ex. `2x+1=5 / <=> 2x=4 / <=> x=2`).
2. Click sur OMath → revert → 3 paragraphes Word.
3. Modifie la 2e ligne (typo correction).
4. Enter sur la 3e ligne (ou n'importe laquelle).
5. → re-bloc multi-ligne complet, 3 lignes, alignement gauche, pas de ¶ orphelin.

Et test négatif :

6. User a 2 paragraphes Word random non-math (« Soit f une fonction » /
   « = définie sur R »).
7. Conversion sur le 2e → ne doit PAS absorber le 1er.

## Liens

- Brief multi-ligne (Phase 1) : [`2026-04-30-multiline-systems-equivalences.md`](2026-04-30-multiline-systems-equivalences.md)
- ADR refactor cross-merge : [`2026-05-04-Meta-cross-merge-pipeline-refactor.md`](../decisions/2026-05-04-Meta-cross-merge-pipeline-refactor.md)
