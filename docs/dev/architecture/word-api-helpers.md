# Word API helpers — inventaire et règles d'usage

Référence pour les helpers de bas niveau qui encapsulent les comportements
piège de l'API Word (positions internes, structural markers, sticky-zones,
auto-grow CC, etc.). Si tu touches à l'insertion/suppression/probe
d'OMath ou de ContentControl, **lis cette page d'abord**.

Voir aussi : [feedback-word-api-workflow](../../../C:/Users/wanadev/.claude/projects/D--Software-DocMath/memory/feedback_word_api_workflow.md) (workflow anti-tournage-en-rond).

---

## 1. Positions internes Word

### `ParagraphPositionTranslator.StringPosToInternal(paraRange, stringPos)`

`Host/Detection/ParagraphPositionTranslator.cs`

Traduit une position string (= offset dans `paragraph.Range.Text`, ce que
voit le NER) vers une position interne Word (= ce que `SetRange` attend).

**Pourquoi** : les OMath, CC wrappers, OMathPara, structural markers
prennent des positions internes invisibles dans `Range.Text`. Sans
traduction, `SetRange(stringPos, ...)` snap arrière sur l'OMath voisine.

**Implémentation** : itère `paragraph.Range.Characters` en accumulant
`Character.Text.Length` (= gère les surrogate pairs correctement).
Une variante `StringPosToInternalNative` utilise `Range.MoveStart` mais
foire avec les surrogates (cf. bug `gg` 2026-05-19).

**Usage** : appelé au commit dans `SuggestionService.CommitLatexAndOMathCore`
pour convertir les coords NER en coords Word avant `InsertOMathAt`.

---

### Pattern de normalisation `sel.SetRange + readback`

Quand on a une position "conceptuelle" (= calculée depuis `om.Range.Start`,
`cc.Range.End`, etc.) qui doit être passée à une autre opération Word
(ex. `sel.Delete`, `Range.InsertXML`), TOUJOURS faire :

```csharp
sel.SetRange(rawPos, rawPos);
int internalPos = sel.Start;  // ← position Word interne réelle
```

Word snap silencieusement les positions invalides (= dans une OMath, à
une frontière SDT, etc.). Sans normalisation, l'opération suivante peut
agir sur des coords légèrement différentes de ce qu'on attend.

Référence canonique : `InsertOMathAt` étape 1 (normalisation des bornes
absStart/absEnd au début de l'insertion).

---

## 2. Probe Anchor CC (lookup OMath → metadata)

### `CcMetaResolver.ResolveAt(om)` → `(cc, meta)`

`Host/CCMeta/CcMetaResolver.cs`

Retrouve l'anchor CC MathCursor à partir d'une OMath. Cascade :

1. **Backward probe** (positions -1 à -3 avant `om.Range.Start`,
   `doc.Range(p, p+1).ParentContentControl`). C'est le chemin principal
   avec le pattern anchor (ADR 2026-05-19).
2. **Fallback wrap-legacy** : `om.Range.ParentContentControl` direct,
   pour les OMaths créées avec l'ancien pattern wrap (rétro-compat).
3. **Fallback ultime** : probe inverse `om.Range.ContentControls` pour
   les cas legacy display math avec CC sub-anchor.

**Coût** : au pire 4 appels COM (3 probes + 1 fallback). O(1) en pratique.

### `CcMetaResolver.IsOurs(om)` → `bool`

Idem mais sans parser le Tag — juste pour identifier si l'OMath nous
appartient (= a un anchor MathCursor à proximité).

### `CcMetaResolver.ResolveBehindCaret(doc, sel)` → `(om, meta)`

Probe locale brief 2026-05-18 : OMath collée juste avant le caret. Filtre
`StoryType == wdMainTextStory` (= ignore headers/footers/footnotes).

---

## 3. Cleanup structurel d'une plage

### `ZoneCleaner.ClearZone(doc, absStart, absEnd, log)` → `int (newPos)`

`Host/ZoneCleaner.cs`

Vide structurellement une plage `[absStart, absEnd)`. Gère :
- Les CCs intersectant la plage → `cc.Delete(true)` (avec unlock défensif).
- **Garde anti-bloated** : skip les CCs dont `cc.Range.Start < newStart`
  (= CCs étrangères qui débordent dans la plage par auto-grow). Empêche
  la destruction d'OMath voisine.
- Les OMaths résiduelles (legacy non wrappées) → `om.Range.Delete()`.
- Plain text restant → `doc.Range(...).Delete()`.

**Robustesse positions** : mesure le shift réel via `doc.Content.End`
delta avant/après chaque Delete. Pas d'arithmétique sur `cc.End-cc.Start`
(= peut diverger des chars supprimés par Word).

**Tracking `shiftBefore`** : si Word supprime des chars structurels AVANT
`newStart`, le contenu post-CC glisse dans la zone before-CC. Probe pre/post
pour détecter via différence de longueur de `Range.Text`. Cf. bug `=F(x)= 1`
2026-05-19.

Retourne la position post-cleanup où placer le caret pour `TypeText`.

---

## 4. Insertion d'OMath (anchor pattern)

### `SuggestionService.InsertOMathAt(absStart, absEnd, latex, source, absorbedHandles)` → `(newStart, newEnd, newHandle)`

`Host/SuggestionService.cs`

Séquence atomique d'insertion. **Ordre critique** validé 2026-05-20 :

1. Clamp + trim whitespace bornes.
2. LaTeX → UnicodeMath via `Core.LatexToUnicodeMath.Convert`.
3. Normalize bornes via `sel.SetRange + readback`.
4. `ZoneCleaner.ClearZone` sur la plage.
5. `sel.TypeText("​")` (ZWSP) + `Font.Hidden=-1`.
6. `sel.TypeText(unicodeMath)` (la math en plain text après le ZWSP).
7. `OMaths.Add + BuildUp` sur la math range **seule** (ZWSP exclu).
8. `om.Type = DecideOMathTyping(...)`, `om.Justification = Left`.
9. `ContentControls.Add` sur le ZWSP (= EN DERNIER), Tag JSON.
10. `sel.SetRange(om.Range.End, ...)` (caret prêt à taper).

**Ne JAMAIS faire** (cf. feedback_word_api_workflow) :
- Wrap CC avant BuildUp (= Word absorbe le ZWSP)
- `Range.InsertBefore` sur `om.Range` pour le ZWSP (= dans la math zone)
- `LockContentControl = true` (= bloque `cc.Delete` au revert)
- `SetRange(cc.End, cc.End)` pour échapper sticky-zone (= snap dedans)

### `DecideOMathTyping(om, source, log)` → `(WdOMathType, WdOMathJc)`

Helper privé de `SuggestionService`. Décide Display vs Inline + alignement :

1. Source démarre par espace → Inline + Left (override user explicite).
2. OMath seule dans son contexte (¶ vide OU cellule vide) → Display + Left.
3. OMath mixée avec de la prose → Inline + Left.

Détection "seule" : strip de `paragraph.Range.Text` tous les chars
structurels (`\r`, `\n`, `\v`, `\a`=Chr7=cell marker, `\t`, `\f`) +
l'OMath text. Si reste vide → seule.

---

## 5. Détection de voisins + merger

### `NeighborFinder.FindAdjacent(absStart, absEnd)` → `AdjacentNeighbors`

`Host/Merging/NeighborFinder.cs`

Cherche les OMaths voisines à gauche et à droite d'une zone. Pour la gauche :
- **Probe via CC backlink** (delta 1 à 4 : `doc.Range(p, p+1).ParentContentControl`)
  pour absorber les gaps invisibles entre `cc.End` et le caret.
- Fallback : probe OMath endpoint.

Tolère 1 espace simple entre la zone et le voisin (= comportement naturel
quand user tape un espace de séparation).

### `IntraOMathsMerger.TryMergeWithLeft(absStart, absEnd, currentSource, currentLatex)` → `MergeResult?`

`Host/Merging/IntraOMathsMerger.cs`

Fusionne avec le voisin gauche si la source actuelle commence par un
marker de continuation (`=`, `<=>`, `=>`, `{`). LaTeX-preserving :
`mergedLatex = leftLatex + newLatex` (lu depuis `cc.Tag.Latex`, **pas
de re-rendu**).

Drift hash en WARN-only (Word mute le `WordOpenXML` post-commit, le hash
n'est pas fiable comme détecteur d'édition manuelle).

---

## 6. UX flèches : sélection OMath au lieu d'entrer dans CC

### `SuggestionService.TrySelectOMathOnLeft()` / `TrySelectOMathOnRight()` → `bool`

Hookés sur `KeyboardInterceptor.OnLeftPressed/OnRightPressed`. Quand
l'utilisateur appuie ←/→ et qu'il est adjacent à une OMath MathCursor,
**sélectionne l'OMath entière** au lieu de laisser le caret y entrer.
Mimétisme Word inline-shape : double-press collapse, Suppr/Backspace
supprime, Enter ouvre l'edit mode.

Probe sur 2 positions à gauche/droite (= post-CcSticky escape).

### `SuggestionService.EjectCaretFromLockedCcIfAny()` → `bool`

Si le caret se retrouve coincé dans une CC (cas rare avec le pattern
anchor), Esc l'éjecte vers le côté le plus proche basé sur `_lastCaretPos`.

---

## 7. Revert (= revenir à la sténo brute)

### `EditModeController.OnRevertRequested()`

Séquence simple :
1. `FindOMathAtCaret()` → om
2. `CcMetaResolver.ResolveAt(om)` → cc, meta (= source steno)
3. `sel.SetRange(cc.Range.Start, om.Range.End)`
4. Unlock le CC (best-effort)
5. `sel.Delete() + sel.TypeText(revertText)`
6. `cc.Delete(false)` pour disposer un éventuel wrapper ghost

Évite `Range.Text =` et `ZoneCleaner` — la séquence directe est plus
robuste pour ce cas spécifique.

---

## 8. Debug variants (Host/Debug/OMathInsertVariants.cs)

Classe `OMathInsertVariants` exposant 8+ variantes d'insertion en boutons
ribbon (`Variant E`, `G`, `G1`-`G4`, `POC DELETE`). Sert à comparer les
ordres `TypeText/BuildUp/CC.Add` et à diagnostiquer les structures OOXML
produites par chaque combinaison.

Chaque variante :
- Insère `"g(x)=1/x"` au caret avec une recette spécifique
- Dump l'OOXML body final + char codes du paragraphe
- Affiche le résultat dans le pane inspecteur

**Quand l'utiliser** : avant de modifier l'ordre d'opérations dans
`InsertOMathAt`, valider l'effet via la variante correspondante. Cf.
règle "POC minimal AVANT la prod" du workflow Word API.
