# Feat — Anchor CC pattern : CC adjacent à l'OMath au lieu de wrap

**Date :** 2026-05-19
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-18-Feat-intra-omaths-merger-revival.md](2026-05-18-Feat-intra-omaths-merger-revival.md) (qui s'appuyait sur le pattern CC-wrap)

## Citation acté

> « direct en prod.. » — utilisateur, 2026-05-19, après validation des
> 4 scénarios d'acceptation et de l'analyse des implications G1.

> « ok c'est parfait maintenant on touche plus on commit et on tag » —
> utilisateur, 2026-05-20, après validation de l'insertion (display ¶ vide
> + cellule de tableau) et du revert.

## Contexte

Le pattern actuel (Phase B) wrap l'OMath dans un `<w:sdt>` (ContentControl
RichText). Tag JSON contient sténo + LaTeX + hash. Backlink via
`om.Range.ParentContentControl`.

**Problèmes observés** :
- En mode display, le setter `om.Type = wdOMathDisplay` à l'intérieur de
  l'SDT déclenche l'ajout de `<w:br/>` (= soft line breaks Chr(11)) de
  chaque côté de `<m:oMathPara>` par Word, pour isoler visuellement. Résultat :
  lignes vides au-dessus/dessous de l'équation.
- L'auto-grow du CC : taper à la sticky-zone (juste à `cc.End`) absorbe
  le contenu dans la CC. Bug reproductible : `g(x)` convert + flèche-gauche
  + Enter → CC absorbe le ¶ suivant.
- Soft-lock (verrouillage indirect) quand on tente de bloquer l'auto-grow
  via `LockContents = true`.

**Variantes testées** (cf. `Host/Debug/OMathInsertVariants.cs`) :
- **A** (CC-first wrap, Type=Display) = production actuelle → `<w:br/>` ajoutés
- **E** (BuildUp first, CC block-level, no Type set) → caret stuck post-commit
- **G** (no CC, display propre) → pas de backlink possible
- **G1** (G + anchor CC ZWSP hidden avant l'OMath) → **TOUT PROPRE** : display
  pixel-parfait, caret naturel après l'OMath, backlink via probe.
- G3 (G + CC sur om.Range) → caret stuck identique à E

## Décision

### 1. Pattern d'insertion : anchor CC, pas wrap

Structure finale dans le doc :

```
[¶ start]   [CC anchor: 1 char ZWSP, Font.Hidden=true, Tag=JSON]   [m:oMathPara/m:oMath]   [\r]
            ↑                                                       ↑
            tiny CC, métadonnée                                     OMath "naked"
```

La CC ne wrappe PAS l'OMath. Elle vit **juste avant**, sur un caractère
ZWSP (`​`) caché. Son `Tag` contient la métadonnée (sténo, LaTeX, hash,
handleId, version) au format JSON, comme avant. `LockContentControl = false`
**volontairement** (le lock bloquait `cc.Delete` au revert — défense
laissée aux flèches qui sélectionnent l'OMath + ZoneCleaner anti-bloated).

### 1bis. Séquence d'insertion (ORDRE CRITIQUE, validé 2026-05-20)

```csharp
// Dans InsertOMathAt :
1. ZoneCleaner.ClearZone(doc, internalStart, internalEnd)
2. sel.SetRange(afterCleanupPos, afterCleanupPos)

3. sel.TypeText("​")              // ZWSP plain text (PAS encore CC)
   doc.Range(zwspStart, zwspEnd).Font.Hidden = -1
4. sel.TypeText(unicodeMath)            // math plain text APRÈS le ZWSP

5. typedRange.OMaths.Add(...).BuildUp() // BuildUp sur la math range SEULE
                                        // ZWSP exclu du range = pas absorbé
   om.Type = decideOMathTyping(...)    // Display si seule dans contexte
   om.Justification = wdOMathJcLeft

6. anchorRange.ContentControls.Add(wdContentControlRichText)
   cc.Title = "MathCursor"
   cc.Appearance = wdContentControlHidden
   cc.Tag = MCMeta JSON

7. sel.SetRange(om.Range.End, om.Range.End)  // caret APRÈS l'OMath
```

**Pourquoi cet ordre** :
- Si on wrap le CC AVANT BuildUp (étape 6 avant 5) → Word absorbe le ZWSP
  dans l'OMath (testé, échec).
- Si on wrap le CC APRÈS BuildUp avec `Range.InsertBefore(om.Range, ZWSP)` →
  Word met le ZWSP DANS la math zone (testé, échec).
- Si on tape le ZWSP en PLAIN TEXT d'abord, puis la math, puis BuildUp sur
  la math SEULE, l'OMath se construit sans toucher au ZWSP qui est OUTSIDE
  son range. Le wrap CC en dernier ne perturbe plus la structure math.

### 1ter. Sémantique « tout seul » (Display vs Inline)

`DecideOMathTyping` détecte si l'OMath est seule dans son **contexte
structurel** :
- paragraphe vide à part la formule → Display
- **cellule de tableau** vide à part la formule → Display (= idem)
- mixé avec de la prose → Inline
- source démarre par espace → Inline (override user explicite)

Pour ça, on strip TOUS les chars structurels Word non-prose de
`paragraph.Range.Text` : `\r`, `\n`, `\v` (soft break), `\a` (Chr 7 = cell
marker), `\t`, `\f`. Si le reste est vide → seule dans son contexte.

### 2. Lookup OMath → metadata (backward probe)

`CcMetaResolver.ResolveAt(om)` :
- Ancien : `om.Range.ParentContentControl` (= la CC qui wrappait)
- Nouveau : **backward probe** de 1 à 3 positions avant `om.Range.Start`,
  cherche un `ParentContentControl` avec `Title == "MathCursor"`.

```csharp
for (int delta = 1; delta <= 3; delta++) {
    var probe = doc.Range(om.Range.Start - delta, om.Range.Start - delta + 1);
    var cc = probe.ParentContentControl;
    if (cc != null && cc.Title == MCMetaJson.CcTitle) return cc;
}
return null;
```

Reste O(1) (au plus 3 probes).

### 3. CcSticky escape : caduc

Plus besoin d'éjecter le caret de la sticky-zone — la CC est tiny, pas
sur l'OMath. Le caret naturel après TypeText est juste après l'OMath, donc
prêt à taper.

### 4. ZoneCleaner : cleanup l'anchor avec l'OMath

Quand on supprime une OMath dans une zone, il faut aussi supprimer son
anchor CC (juste avant). Sinon orphan metadata.

Logique :
- Pour chaque OMath dans la zone : backward probe son anchor
- Supprime `cc.Range.Delete()` + `om.Range.Delete()` (ou suppression
  groupée via doc.Range englobante)

### 5. Auto-grow : neutralisé naturellement

La CC contient juste 1 char ZWSP. L'utilisateur ne peut pas la faire
grossir avec du contenu math (il y a l'OMath voisine qui prend la place).

Si l'utilisateur tape dans la sticky-zone de la mini-CC, il insère
1-2 chars qui restent dans la CC (ZWSP + char tapé). Pas critique — la
CC peut grossir un peu mais reste séparée de l'OMath. ZoneCleaner peut
trim si besoin via le hash mismatch.

## Tradeoff & alternatives écartées

- **Wrap CC (production actuelle)** : `<w:br/>` parasites en display, sticky
  auto-grow, soft-lock potential. Rejeté.
- **OOXML manipulation** (G2) : bypass l'API ContentControls.Add, wrap via
  XDocument. Fonctionne mais fragile (Word peut re-écrire au save).
- **CC sur paragraph block-level** (E) : pas de `<w:br/>` MAIS caret stuck
  post-commit (block-level CC = sticky pour tout le ¶).
- **No CC, hash-based store** (= option (ii) phase 2) : nécessite un store
  doc-level + cache + rebuild au load. Plus gros refactor.

## Conséquences

### Code touché

- **`Host/CCMeta/CcMetaResolver.cs`** : `ResolveAt(om)` = backward probe.
  `ResolveBehindCaret` inchangé en signature (appelle ResolveAt).
- **`Host/SuggestionService.InsertOMathAt`** : nouvelle séquence :
  1. ZoneCleaner cleanup
  2. SetRange + TypeText
  3. OMaths.Add + BuildUp + Type=Display + Justification
  4. **Insert ZWSP + wrap anchor CC + Tag + LockContentControl**
  5. Pas de CcSticky escape (caret reste où TypeText l'a laissé)
- **`Host/CCMeta/CcSticky.cs`** : caduc, à supprimer ou laisser inerte
  (= helper non appelé).
- **`Host/ZoneCleaner.cs`** : pour chaque OMath dans la zone, backward
  probe + supprime anchor + supprime OMath.
- **`Host/Merging/NeighborFinder.cs`** : `ProbeLeftViaCcBacklink` adapté
  pour backward probe sur OMath candidats.

### Tests

- Les 4 scénarios d'acceptation validés manuellement :
  1. Formule sur ¶ vide → display + caret continue
  2. Formule après prose → inline + caret continue
  3. Revert + re-commit → propre, pas de ¶ orphelin
  4. Adjacent OMath → merge correct

### API publique

Inchangé en surface. Les changements sont dans l'implémentation interne
de `CcMetaResolver` et `InsertOMathAt`.

### Règles MC impactées

Aucune.

## Validation post-fix

- Tester chaque scénario d'acceptation.
- Vérifier qu'aucun `<w:br/>` n'apparaît dans le OOXML d'un commit display.
- Vérifier que click-on-OMath ouvre toujours le popup edit mode.
- Vérifier que revert vide proprement et permet re-commit.

## Plan en cours

Tasks (créées au moment du go) :
- ADR (en cours, ce fichier)
- `CcMetaResolver.ResolveAt` backward probe
- `InsertOMathAt` nouveau flow anchor
- `ZoneCleaner` anchor cleanup
- `NeighborFinder` adapt
- Test des 4 scénarios
