# Fix — Insertion d'OMath par splice XML du `<w:p>` existant (pas reconstruction depuis `Range.Text`)

**Date :** 2026-05-07
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** ADR `2026-05-04-Refactor-omath-via-xml-transplant`

## Citation acté

> "et le paragraphe on peut pas le parser ou l'avoir en XML et jouer avec
> ca ? plutot que d'essayer des range non consistants" — utilisateur,
> 2026-05-07
>
> "oui ca me semble robuste et ce doit etre la solution de base meme"

## Décision

Quand on commit une nouvelle OMath en mode inline single-¶
(`!isDisplayMath && targetCount == 1`), on ne reconstruit plus le `<w:p>`
depuis `textBefore = doc.Range(paraStart, absStart).Text`. À la place :

1. On lit le `<w:p>` cible directement depuis Word :
   `firstPara.Range.WordOpenXML`. Ce XML est la source de vérité — il
   contient les OMaths voisines à l'identique sous forme `<m:oMath>`
   dedans.
2. On build la nouvelle OMath en zone isolée (existant), on extrait juste
   son élément `<m:oMath>` (ou `<m:oMathPara>`).
3. On localise dans le `<w:p>` cible les runs `<w:r>` qui couvrent la
   source brute du commit (= les chars `[absStart, absEnd]`, qui sont du
   texte plain — aucune OMath dans cette plage). On utilise
   `doc.Range(absStart, absEnd).Text` ici parce que cette plage ne
   contient pas d'OMath par construction.
4. On remplace ces runs par la nouvelle OMath dans le `<w:p>` (string
   manipulation pure).
5. On réinjecte le `<w:p>` modifié via le `ReplaceParagraphsInDocXml` +
   `doc.Content.InsertXML` existant.

Les routes display-math / multi-ligne / cases / align gardent l'ancien
chemin (elles ne souffrent pas du bug parce qu'elles remplacent des ¶
qui ne contiennent que la math).

## Pourquoi

- **`Range.Text` est lossy pour les OMaths.** Pour `Soit [OMath f] et g`,
  `Range(0, 11).Text` renvoie quelque chose comme `"Soit f et "` avec
  la f comme texte plain (perdue de structure), ou `"Soit  et "` avec
  un placeholder. Selon le contenu de l'OMath voisine (lettre simple
  vs subscript vs vec), Word renvoie des chars différents qui peuvent
  faire dégénérer la BuildUp ultérieure (cas user 2026-05-07 :
  `Soit [x_2] et y2` → x_2 disparaît complètement, alors que
  `Soit [f] et g` survit dégradé).

- **Le `<w:p>` XML EST la source de vérité de Word.** Il contient déjà
  les OMaths voisines correctement structurées. Reconstruire à partir
  de `Range.Text` jette cette information qu'on a déjà — design
  cradeau (citation user). Splicer dedans préserve tout par défaut, on
  ne touche que ce qu'on doit toucher.

- **La plage de la source brute `[absStart, absEnd]` est garantie
  text-only.** L'utilisateur tape la math en texte plain (`"y2"`,
  `"AB"`, `"x2"`), et c'est ce qu'on convertit en OMath au commit.
  Il n'y a JAMAIS d'OMath dans `[absStart, absEnd]` — c'est uniquement
  les runs de texte qu'on vient de taper. Donc `Range.Text` sur cette
  plage spécifique est safe (pas d'OMath à flatten).

- **100% testable hors Word.** La fonction de splice prend en entrée :
  le `<w:p>` XML, la math source string, le `<m:oMath>` XML neuf →
  retourne le `<w:p>` modifié. Aucune dépendance Word, xUnit normal.

## Alternatives écartées

- **Range.InsertXML(omathWrapped) à la position [absStart, absEnd].**
  Tenté précédemment, Word a refusé avec "Impossible d'insérer le code
  XML à l'emplacement spécifié". À fond debugger faisable mais on n'a
  pas la garantie que ça marche tous les cas.

- **Délimiter la BuildUp avec des `\r` temporaires (split ¶ avant
  BuildUp, rejoin après).** Manipulations fragiles sur les ¶, Word
  copie les pPr inconsistamment, risque de perdre la justification.

- **Range.FormattedText copie depuis temp zone vers cible.** Word
  transporte la structure OMath mais aussi les pPr du temp ¶, peut
  écraser les attributs de ¶ cible.

- **Rester sur `Range.Text` et patcher les chars-de-OMath.** Hack
  fragile, tout autre rule sup-number (vec, indice, etc.) peut ressortir
  des chars différents. On ne contrôle pas ce que Word renvoie.

## Scope du fix

- **Inclus** : route `isDocxOoxml && !isDisplayMath && targetCount == 1`
  dans `SuggestionService.OnPopupCommitRequested` (= la route majoritaire
  pour les commits user lambda).
- **Exclus** (gardent l'ancienne route) :
  - `isDisplayMath` (cases, align multi-ligne) — la math est seule dans
    le ¶ par construction, pas de problème.
  - `targetCount > 1` (cross-paragraph merge) — déjà géré par d'autres
    mergers en amont.
  - `!isDocxOoxml` (legacy .doc) — pattern API in-place inchangé.

## Plan d'exécution

1. (Re-)créer `Host/InlineOMathSplicer.cs` — pure utility :
   - `ExtractOMathElement(capturedXml)` : extrait `<m:oMath>` ou
     `<m:oMathPara>` du XML de zone isolée.
   - `SpliceMathSourceRuns(paraXml, mathSource, newOMathXml)` : trouve
     les `<w:r>` couvrant `mathSource` à la fin du `<w:p>`, les
     remplace par `newOMathXml`. Retourne le `<w:p>` modifié.
2. Tests xUnit `InlineOMathSplicerTests.cs` couvrant :
   - 1 run unique contenant exactement la source.
   - Source répartie sur plusieurs runs (Word splitte sur changement
     de formatage).
   - ¶ avec OMath voisine en début → préservée.
   - ¶ avec deux OMaths voisines + texte → préservées toutes les deux.
   - Cas dégénérés (XML invalide, source vide, source absente).
3. Brancher dans `SuggestionService.OnPopupCommitRequested` à la place
   du `BuildOMathXmlIsolated(doc, textBefore, latex, textAfter)` pour
   la route concernée.
4. Vérifier en VSTO les scénarios :
   - `Soit f et g` → 2 OMaths (régression-non).
   - `Soit x2 [popup indice] et y2 [popup revert]` → 2 OMaths.
   - `Soit AB [popup vec] et y` → 2 OMaths.
   - `Soit f et g [puis edit f]` → mode édition inchangé.

## Risques

- Si Word splitte la source brute sur plusieurs runs avec des
  formatages exotiques (ex. `<w:rPr>` partiel), la recherche backward
  peut rater le match. Mitigation : fallback sur l'ancien chemin si la
  fonction de splice retourne null, on ne casse pas le baseline.
- Word 2019 XML strict (cf. memory `reference_office_2019_omath_limits`)
  → tester en Word, pas seulement xUnit.
