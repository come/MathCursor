# Refactor — Lecture du paragraphe courant via `Range.WordOpenXML` (pas `Range.Text`)

**Date :** 2026-05-11
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** ADR `2026-05-11-Fix-omath-splice-content-based-navigation`

## Citation acté

> "var rawText = AutocorrectNormalizer.Normalize(doc.Range(paraStart,
> paraEnd).Text ?? ''); est ce qu'on ne doit pas prendre autre chose que
> doc.Range mais plutot voir ce qu'il y'a autour du cursor en xml et
> balancer le texte ? question ouverte" — utilisateur, 2026-05-11
>
> "refactor XML"

## Décision

`WordContextReader.ReadCurrentParagraph` cesse de lire le texte du
paragraphe courant via `doc.Range(paraStart, paraEnd).Text`. À la
place :

1. **Lit `paraRange.WordOpenXML`** — fragment OOXML local du `<w:p>`
   courant, avec son contexte enveloppant complet (package, body ou
   `<w:tc>`, …).
2. **Parse via XDocument** et localise le `<w:p>` cible (le seul du
   fragment local, par construction de l'appel `paraRange.WordOpenXML`).
3. **Reconstruit le texte** en walkant les enfants direct du `<w:p>` :
   - Pour chaque `<w:r>` : concat des `<w:t>` enfants (texte brut tel
     que tapé par l'utilisateur, **jamais de char de contrôle Word**
     comme `\a` / `\v` / `\b` que `Range.Text` injectait).
   - Pour chaque `<m:oMath>` : injecte N espaces, où N = longueur Word
     de l'OMath correspondante (`om.Range.End - om.Range.Start` via
     `doc.OMaths` filtré par chevauchement avec `paraRange`). Les OMaths
     du XML sont appariées aux Word.OMath par **ordre d'apparition**
     dans le `<w:p>`.
   - Pour les autres enfants (`<w:bookmarkStart>`, `<w:bookmarkEnd>`,
     `<w:proofErr>`, `<w:commentRangeStart>`, …) : 0 char (ils
     n'occupent pas de char dans Range.Text non plus).
4. **Identifie les régions OMath** par les positions accumulées pendant
   le walk (= position relative dans `text` de chaque `<m:oMath>`,
   couvrant les N espaces injectés).
5. **Préserve l'invariant 1:1** entre `text` et positions absolues
   Word : `text[i]` correspond toujours au char absolu `paraStart + i`
   dans le document Word. Critique pour la suite du pipeline qui fait
   `absStart = paragraphAbsStart + target.Start` (insertion d'OMath
   depuis position relative). Sanity check : si
   `text.Length != Range.Text.Length`, log un diag et fallback sur
   l'ancien chemin (`Range.Text` + `AutocorrectNormalizer.Normalize`).
6. **`caretOffset`** reste calculé via `sel.Start - paraRange.Start`
   (positions absolues Word). Cohérent avec l'invariant 1:1.

L'extraction est faite par un helper pur `ParagraphTextExtractor` (logique
sans dépendance Word) — testable en xUnit avec des fragments XML
synthétiques.

`AutocorrectNormalizer` reste utilisé sur le texte reconstruit pour
normaliser les chars Unicode "smart" (en-dash, smart quotes) qui
peuvent apparaître dans les `<w:t>` (Word AutoCorrect les insère au
moment de la frappe, ils sont dans le XML).

## Pourquoi

- **Bug 2026-05-11 NER en cellule** : `Range.Text` dans une cellule
  inclut des caractères de contrôle Word (`\a` cell-end, `\v` line
  break, `\b`, …) que `AutocorrectNormalizer` ne traitait pas. Le NER,
  entraîné sur du texte propre tapé par l'utilisateur, ne reconnaît
  plus les zones math en présence de ces chars. Symptôme : l'utilisateur
  doit forcer la conversion avec Ctrl+Espace dans un tableau parce que
  la détection automatique ne propose rien.

- **Le XML est exempt de chars de contrôle Word par design.** `<w:t>`
  contient le texte brut. Les `\a`/`\v`/etc. de `Range.Text` sont du
  **rendu** Word, pas de la donnée stockée. Lire le XML court-circuite
  cette pollution.

- **Cohérent avec ADR `2026-05-11-Fix-omath-splice-content-based-navigation`.**
  Le splicer est déjà passé en content-based XML. Le reader fait pareil.
  Une seule mentalité : raisonner sur la structure OOXML locale autour
  du curseur, pas sur le rendu Range.Text aplati avec ses caractères
  internes.

- **Robustesse cellule par construction.** Pas de spécial case
  `wdWithInTable`, pas de détection de conteneur. Le XML donne
  directement la bonne structure peu importe où on est dans le doc.

- **Pas de pansement à étendre.** Si demain Word ajoute un nouveau char
  de contrôle interne, le code n'a rien à mettre à jour — il ne lit
  plus jamais `Range.Text`.

## Phasage

1. **Helper pur `ParagraphTextExtractor`** (~50 LoC, .NET Standard 2.0
   compatible côté core ou interne adapter selon dépendances Word —
   adapter suffit ici).
2. **Tests synthétiques** dans `MathCursor.Tests/Host/` :
   - Texte simple, pas d'OMath.
   - Texte avec 1 OMath au milieu.
   - Texte avec 2 OMaths consécutifs / espacés.
   - Texte avec `<w:bookmarkStart>` au milieu (doit être skip).
   - Cellule : `<w:tc>` contenant le `<w:p>` cible (helper ne se soucie
     pas du conteneur — il prend le seul `<w:p>` du fragment).
   - Fragment vide ou mal formé → fallback null.
3. **Integration `WordContextReader.ReadCurrentParagraph`** :
   - Path nominal : lit XML, parse, extrait, retourne.
   - Sanity check `text.Length == Range.Text.Length` → si KO, log
     `paragraph_xml_mismatch` et fallback sur l'ancien chemin.
4. **Suppression de l'ancien chemin** une fois confirmé en usage réel
   (1-2 sprints). Pas dans ce commit.

## Risques

- **Perf** : `paraRange.WordOpenXML` est appelé à chaque
  `WindowSelectionChange`. Sur un doc long, le sérialiseur Word peut
  prendre quelques ms. Le cache existant côté `SuggestionService`
  (`_lastParagraph == paragraphText && _lastCaretPos == caretPos`) ne
  filtre pas l'appel XML — il filtre la suite. À mesurer empiriquement
  sur usage réel. Si problème : key le cache sur `(paraStart, paraEnd,
  caretPos)` avant l'appel XML.

- **Format Word inattendu** : si Word émet un fragment XML avec
  plusieurs `<w:p>` (ne devrait pas arriver pour un `Paragraph.Range`,
  mais défensif), le helper prend le **dernier** `<w:p>` du fragment
  (= celui où est le curseur en pratique). Sanity check
  `text.Length == Range.Text.Length` couvre ce cas en fallback.

- **OMaths sans `<m:oMath>` direct enfant** : si une OMath est imbriquée
  (très inhabituel, pas vu en pratique), le helper ne la voit pas. À
  surveiller via le sanity check.

- **Compatibilité aval** : `text.Length`, `caretOffset`,
  `omathRegions`, `paragraphAbsStart` ont les **mêmes sémantiques**
  qu'avant. Aucun code aval n'a à changer.

## Hors scope

- Refactor du caretOffset pour qu'il soit calculé via XML aussi (= 2e
  appel `Range(paraStart, sel.Start).WordOpenXML`). Coût perf x2, gain
  marginal. On garde l'invariant 1:1 Word qui rend ça inutile.
- Suppression de `doc.OMaths` globale du reader. On l'utilise toujours
  pour récupérer les longueurs Word des OMaths (la seule infos qu'on
  ne peut pas déduire du XML local sans simuler comment Word compte
  ses chars).
- Migration du même pattern dans d'autres readers (s'il y en a). À
  traiter ADR séparé si besoin.
