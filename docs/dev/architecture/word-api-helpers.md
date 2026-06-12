# Word API helpers — inventaire et règles d'usage

Référence pour les helpers de bas niveau qui encapsulent les comportements
piège de l'API Word (positions internes, frontières de zone math, prompts
placeholder, record undo). Si tu touches à l'insertion/suppression/probe
d'OMath, **lis cette page d'abord**.

**RÉÉCRITE 2026-06-12** pour le pipeline hash-source-map (ADR 2026-06-11 +
amendement) : plus d'anchor CC, plus de ZWSP, plus d'InsertXML. Les sections
historiques (CcMetaResolver, NeighborFinder, TrySelectOMathOnLeft, variantes
debug) décrivaient du code supprimé — purgées. Git garde l'historique.

Voir aussi : [feedback-word-api-workflow](../../../C:/Users/wanadev/.claude/projects/D--Software-DocMath/memory/feedback_word_api_workflow.md) (workflow anti-tournage-en-rond).

---

## 1. Positions internes Word

### `ParagraphPositionTranslator.StringPosToInternal(paraRange, stringPos)`

`Host/Detection/ParagraphPositionTranslator.cs`

Traduit une position string (= offset dans `paragraph.Range.Text`, ce que
voit le NER) vers une position interne Word (= ce que `SetRange` attend).
Itère `Range.Characters` (gère les surrogate pairs ; la variante native
`Range.MoveStart` foire dessus, bug `gg` 2026-05-19).

### Pattern de normalisation `sel.SetRange + readback`

TOUJOURS, avant de passer une position calculée à une opération Word :

```csharp
sel.SetRange(rawPos, rawPos);
int internalPos = sel.Start;  // ← position Word interne réelle
```

Word snap silencieusement les positions invalides.

### ⚠ Les FRONTIÈRES de zone math sont AMBIGUËS

Un même numéro de position désigne la fin de la prose ET le début de la zone
math qui la suit. Une écriture `doc.Range(p,p).Text = …` à cette position
atterrit **côté prose** (mesuré « soit f(x)=1/x » 2026-06-12 : le `f(x)=`
sortait de l'équation). Règle : **ne jamais écrire sur un bord de zone math,
toujours à une position encadrée par du contenu déjà en zone** (cf. §4,
double seed).

---

## 2. Résolution équation → source : `SourceMapResolver`

`Host/SourceMap/SourceMapResolver.cs` (+ `SourceMapStore`, part
CustomXMLParts `urn:mathcursor:source-map:v1`)

La correspondance équation → sténo/LaTeX vit dans une map doc-level indexée
par le CONTENU de l'OMath, bi-clé :
- **K1** (accès, ~0 ms) : SHA1 de `om.Range.Text` ;
- **K2** (confirmation, ~60 ms) : SHA1 de l'OMML **canonique**
  (`OmmlCanonicalizer` — le `WordOpenXML` brut N'EST PAS stable, Word le
  mute ; la projection canonique l'est : 0 drift mesuré, save/reopen compris).

| Appel | Coût | Usage |
|---|---|---|
| `ResolveAt` | K1 (+K2 si ambigu) | affichage (popup edit, merge chaînes) |
| `ResolveConfirmed` | K1+K2 | **obligatoire avant tout revert** (destructif) |
| `IsOurs` | K1 | deletion guard |
| `ResolveBehindCaret` | K1 | OMath collée derrière le caret (Backspace) |

Équation éditée à la main = hash changé = plus « à nous » (acté ADR).
Doublons = 1 entrée, dernier-écrit-gagne. Cap 500 entrées, éviction
par ancienneté. Entrées mortes inoffensives, pas de GC.

---

## 3. Cleanup structurel : `ZoneCleaner.ClearZone(doc, absStart, absEnd, log)`

`Host/ZoneCleaner.cs` — vide structurellement `[absStart, absEnd)` :
CCs (désormais uniquement ÉTRANGÈRES — garde anti-bloated conservée),
OMaths à re-committer, plain text. Mesure le shift réel via
`doc.Content.End` (jamais d'arithmétique `cc.End-cc.Start`).

### Remplacer un ¶ entier (blocs multilignes)

`ChainController.ReplaceStart` : début du ¶ si rien de visible ne précède
l'OMath (sinon un ¶ squelette survit — preuve paras-diag 2026-06-10),
`om.Range.Start` sinon (équation inline dans la prose).

### Alignement eqArr : layout ADAPTATIF (preuve V1-V5, 2026-06-10)

- Chaîne sans connecteur : UN `&` par ligne devant le signe.
- Chaîne avec ⟺/⟹ : DEUX `&` par ligne (single-`&` désaligne).
- L'alignement n'agit qu'en **display** → les blocs forcent
  `om.Type = wdOMathDisplay`, SANS poser `Justification` (le setter jette
  sur les eqArr frais ; le défaut rend l'alignement voulu).

---

## 4. Insertion d'OMath : `OMathInserter` + walker `OmmlToOMathBuilder`

Pipeline (ADR hash-source-map, amendement « pas de fallback ») :

```
0. IsSupported (whitelist pure OmmlWalkerWhitelist — VERROU : le test
   WalkerCoverageTests exige que TOUT candidat du corpus moteur soit
   constructible ; inconstructible = bug CI, jamais un cas runtime)
1. normalisation bornes (SetRange + readback)
2. ZoneCleaner.ClearZone
3. probe liste (ListFormat) → Inline forcé dans une puce
4. OmmlToOMathBuilder.Build   ← zéro InsertXML
   └ échec → ROLLBACK : la sténo est RETAPÉE, jamais de demi-équation
5. typage : Display si seule dans le ¶ + Justification gauche (display
   SEULEMENT — le setter jette en inline) ; blocs : Display sans Jc
6. échappement caret (cf. §5)
7. source en map DIFFÉRÉE — FlushPendingRecord HORS du scope undo (§6)
```

### Règles dures du walker (toutes MESURÉES, 2026-06-11/12)

- **JAMAIS `OMaths.Add` sur une range VIDE** : Word insère DANS l'équation
  un `w:sdt` placeholder « Tapez une équation ici. »
  (`temporary`/`showingPlcHdr`) **invisible pour `om.Range.ContentControls`**
  (count=0) → insupprimable proprement. Trimmer son texte le ré-affiche ;
  vider la zone (`om.Range.Text = ""`) laisse une frame fantôme ET éjecte
  la première écriture hors zone.
- **DOUBLE seed `¤¤`** : l'équation est créée par `OMaths.Add` SUR deux
  caractères seed, et tout le contenu s'insère ENTRE les deux (positions
  intérieures garanties, cf. §1 frontières ambiguës). Les seeds (premier +
  dernier `¤` de la zone) sont supprimés après construction.
- Runs posés par `Range.Text`, structures par `om.Functions.Add`
  (frac/scrSup/scrSub/delim/nary/rad/func/limLow/acc/mat/eqArray), args
  remplis récursivement via leurs `OMath` imbriquées.
- **Matrices** : `Functions.Add(…, rows, cols)` peut IGNORER les dimensions
  (« le membre de la collection requis n'existe pas ») → compléter par
  `mat.Rows.Add()` / `mat.Cols.Add()` avant de remplir les cellules.
- Échec en cours de build : `om.Range.Delete()` PUIS re-probe local — un
  squelette vide peut survivre au Delete.

### Comparer deux OMML (conformance, clé K2)

Word OMET les propriétés à valeur par défaut au stockage (begChr `(`,
endChr `)`, chr `∫` du nary, chapeau U+0302 de l'acc, subHide/supHide `0`,
limLoc `undOvr`, type `bar` du fPr) et AJOUTE les `mPr/mcs` des matrices
(« N colonnes, centrées »). Toute comparaison doit replier les deux côtés —
cf. `OmathWalkerConformance.IsDefaultProp` et `OmmlCanonicalizer` (drop
rPr/ctrlPr, fusion des runs adjacents, attributs m: seuls).

---

## 5. Échappement caret post-commit

`SetRange(om.Range.End)` seul laisse Word en « math input mode » (frappe
suivante en italique math). **`MoveRight(wdCharacter, 1)`** franchit la
frontière et clôt la saisie (= flèche droite). Banc POC Escape : les
alternatives `SetRange(om.End+1)` / `EndKey` / `Italic off` échouent.

**Post-condition LISTE** (litest.docx 2026-06-12) : équation dernier contenu
d'une puce → UN MoveRight peut laisser le caret sur la frontière intérieure,
et Entrée Y PROLONGE la zone math sur la puce suivante. Boucle « tant que
`sel.OMaths.Count > 0`, re-MoveRight (cap 3) », hops loggés.
TODO(escape-liste) : fonctionne (validé user), mais une alternative type
MoveEnd aurait été testée dans cette version — retrouver ce banc et comparer.

---

## 6. Record undo (contrat « 1 Ctrl+Z = 1 commit »)

`UndoRecordScope` (custom record nommé) + sondes `UndoRecordScope.Probe`.

| Opération | Effet sur le record custom |
|---|---|
| TypeText / Range.Text / Delete | OK |
| `OMaths.Add` + `Functions.Add` (walker) | **OK — record intact** |
| écriture CustomXMLPart (delete+add) | OK (mesuré P5, 5 ms @100 entrées) |
| `Range.InsertXML` | **FERME le record** (raison de la mort du pipeline OMML chirurgical) |
| lecture `WordOpenXML` (Record/K2) | **FERME le record** → `FlushPendingRecord` APRÈS le Dispose du scope |

La map n'est pas annulable (les CustomXMLParts ne sont pas dans la pile
undo) : un Ctrl+Z qui retire l'équation laisse une entrée morte — politique
cap+éviction, sans conséquence.

---

## 7. Revert (= revenir à la sténo brute)

`EditModeController.OnRevertRequested` :
1. `FindOMathAtCaret()` → om
2. `SourceMapResolver.ResolveConfirmed(doc, om)` → source (**K2 obligatoire**)
3. `sel.SetRange(om.Range.Start, om.Range.End)` + `sel.Delete()` +
   `sel.TypeText(steno)`

Le panneau edit s'ancre au DÉBUT de la formule (`Window.GetPoint(om.Range)`
converti en DIP via `CaretScreenPositionReader.GetDpiScale`), repli
caret-rightaligned si GetPoint échoue.

---

## 8. Suppression au clavier : `EquationDeletionGuard`

Backspace collé derrière (resp. Suppr collé devant) une équation À NOUS
(`IsOurs`, K1) → sélection de l'OMath ENTIÈRE comme une unité, la frappe
suivante supprime tout (mimétisme inline-shape). Une équation éditée à la
main n'est plus à nous → suppression Word native char à char.
H2 (orphelines) et H3 (caret piégé) de l'ex-AnchorHygiene : caducs par
construction — plus de CC ni de caractère caché dans le document.
