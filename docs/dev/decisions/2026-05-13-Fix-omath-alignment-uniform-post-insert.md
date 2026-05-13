# Fix — Alignment `m:jc=left` uniforme post-insert (tous chemins)

**Date :** 2026-05-13
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [`2026-05-05-Limit-omath-jc-stripped-on-fusion.md`](2026-05-05-Limit-omath-jc-stripped-on-fusion.md) (contexte historique m:jc stripped on fusion) + commit `5eb68ff` (alignment OMath display left)

## Citation acté

> « pour moi ca fait une duplication de code et c'est antipattern, il faudrait remonter à l'appel du dessus => faire l'insertion (une des trois strategie) puis envoyer l'alignement » — utilisateur, 2026-05-13
>
> « go » — utilisateur, 2026-05-13 (validation du plan)

## Contexte

Pipeline d'insert avait **3 stratégies** dans
`SuggestionService.TryInsertStrategies` :

| Inserter | Cas couvert | m:jc=left posé ? |
|---|---|---|
| `PureFastPathInserter` | ¶ pur (formule seule sur sa ligne) — cas dominant | **Non** — bypass voie XML, applique `range.OMaths.Add` + `BuildUp` direct |
| `InlineSpliceInserter` | inline single-¶ (formule au milieu d'une phrase) | **Non** — splice `<m:oMath>` pur sans wrapper `<m:oMathPara>` |
| `AtomicRangeInserter` | catch-all (display math, échecs) | **Oui** — `OMathParaJcPatcher.EnsureDisplayWithLeftJc` avant `InsertXML` |

Conséquence : un élève qui tape `x^2` seul sur sa ligne (= `fast_path`,
cas dominant chez le PAP) obtient un OMath sans `m:jc=left` → Word
applique son default (`centerGroup` = centré).

`PostCommitLayoutFinalizer.EnforceOMathParagraphAlignment` existait
déjà avec exactement la bonne logique (Word `OMath.Justification`
setter + patch OOXML `<m:jc m:val="left"/>` via `OMathParaJcPatcher`).
Mais n'était appelé que pour `WasCrossParagraphMerge`, et gardé sous
un flag `_wasXmlTransplant` qui skipait quand l'atomic XML avait déjà
patché.

## Décision

**Remonter l'appel à l'appelant.** Le pipeline devient linéaire :

```
InsertOMathAt(ctx) {
    foreach (strategy in TryInsertStrategies(ctx)) {
        if (ok) break;
    }
    if (ok) EnforceOMathParagraphAlignment(doc, newStart);  // UNIFORM
    SetCaret + NudgeOutOfMath
}
```

Un seul call site, tous les chemins couverts par construction.

### Code

`SuggestionService.InsertOMathAt` (`SuggestionService.cs:~1700`) :
```csharp
if (!ok) LogDiag("commit ABORTED…");

// Alignment uniforme post-insert : pose m:jc=left sur l'OMath
// pour TOUS les chemins (fast_path / splice / atomic). Sans ça
// Word applique son default centerGroup → OMath centré.
if (ok)
{
    try { _layoutFinalizer.EnforceOMathParagraphAlignment(doc, newStart); }
    catch (Exception ex) { LogDiag("enforce_align_post_insert_error: " + ex.Message); }
}

int afterPos = _caretPositioner.ComputeAfterOMath(doc, newEnd);
…
```

`PostCommitLayoutFinalizer.FinalizeCrossMerge` simplifié :
```csharp
public void FinalizeCrossMerge(Word.Document doc, int replaceStart,
    ref int newStart, ref int newEnd, out bool didCreateAnchorPara)
{
    StripLeadingResidualEmptyParagraph(doc, replaceStart, ref newStart, ref newEnd);
    // EnforceOMathParagraphAlignment SUPPRIMÉ ici (uniforme post-insert)
    int caretPos = AppendEmptyParagraphAfterOMath(doc, newStart, out didCreateAnchorPara);
    if (caretPos >= 0) SetCaretAtPosition(caretPos);
}
```

### Cleanup

- `_wasXmlTransplant` (champ + paramètre constructeur du
  `PostCommitLayoutFinalizer`) supprimé : la logique conditionnelle
  qui l'utilisait n'a plus de raison d'être.
- `_lastInsertUsedXmlTransplant` (champ `SuggestionService` + assignation
  `= ok` ligne 1708) supprimé.

## Tradeoff & alternatives écartées

- **Alt A — Déplacer l'appel dans `LayoutImpl` pour tous les chemins**.
  Rejetée : `LayoutImpl` est en aval du pipeline et orchestre cross-merge,
  list-mode, cases. Y ajouter l'alignment mélangerait deux responsabilités
  (post-insert vs post-layout cross-merge). L'utilisateur a explicitement
  signalé que c'était de la duplication : « pour moi ca fait une
  duplication de code et c'est antipattern ».
- **Alt B — Ajouter `EnsureDisplayWithLeftJc` dans chaque Inserter**.
  Rejetée : duplication × 3 inserters au lieu de × 1 call site centralisé.
  Aussi : `fast_path` n'a pas de pipeline XML — il faudrait re-architecturer
  pour y intégrer un patch m:jc.

## Conséquences

### Code touché

- `adapter-vsto/src/MathCursor/Host/SuggestionService.cs`
  - Champ `_lastInsertUsedXmlTransplant` + commentaire bloc supprimés
  - `new PostCommitLayoutFinalizer(_app, () => _lastInsertUsedXmlTransplant, LogDiag)`
    → `new PostCommitLayoutFinalizer(_app, LogDiag)`
  - Assignation `_lastInsertUsedXmlTransplant = ok;` supprimée
  - Ajout du bloc `EnforceOMathParagraphAlignment` post-strategies
- `adapter-vsto/src/MathCursor/Host/Layout/PostCommitLayoutFinalizer.cs`
  - Champ `_wasXmlTransplant` + paramètre constructeur supprimés
  - Branche `if (!_wasXmlTransplant()) EnforceOMathParagraphAlignment(...)`
    de `FinalizeCrossMerge` supprimée
  - Doc de `FinalizeCrossMerge` mise à jour (référence à l'ADR)

### Tests

- Core : **939/946 verts** (inchangé vs S1).
- Adapter : **419/419 verts** (0 régression).
- Pas de nouveau test xUnit ajouté : l'invariant `m:jc=left` est un
  comportement Word interop, non couvrable en xUnit (mock Word
  insuffisant — cf. mémoire `reference_office_2019_omath_limits`).
  **Validation manuelle Word obligatoire** : voir section ci-dessous.

### API publique

- Constructeur `PostCommitLayoutFinalizer` : signature passe de
  `(app, wasXmlTransplant, log)` à `(app, log)`. Internal sealed,
  pas d'impact en dehors du `SuggestionService`.

### Règles MC

- Aucune (pas de regex ajoutée, pas de splice latex).

### Perf

- Coût additionnel par insert : `EnforceOMathParagraphAlignment` appelle
  Word `OMath.Justification` setter (idempotent, ~ms) + tentative de
  patch OOXML qui ne fait `InsertXML(patched)` **que si `changed=true`**
  (cf. `OMathParaJcPatcher.Patch`). Sur un insert qui produit déjà du
  `m:jc=left`, le patcher retourne `changed=false` → pas de second
  `InsertXML`. Coût observable : ~0-30ms par insert, négligeable.

## Validation manuelle Word (obligatoire)

Lancer l'add-in dans Word, scénarios à reproduire :

1. **Fast path (cas dominant)** : nouveau doc, taper `x^2 + 3x - 1`
   seul sur sa ligne → Ctrl+Espace. **Attendu** : l'OMath produit est
   **aligné à gauche** (pas centré).

2. **Splice inline** : taper `Soit x^2 + 1` (avec texte avant) →
   placer le caret après `x^2 + 1`, Ctrl+Espace. **Attendu** : OMath
   inline aligné à gauche dans le ¶ courant.

3. **Cross-merge multi-ligne (régression à vérifier)** : ligne 1
   `AB+BC=CD` Ctrl+Espace, ligne 2 `=CH+HD` Ctrl+Espace (déclenche
   align*). **Attendu** : align* multi-ligne aligné à gauche (pas
   régressé par la suppression du call dans `FinalizeCrossMerge`).

4. **Tableau** : insérer un tableau, dans une cellule taper `x^2` →
   Ctrl+Espace. **Attendu** : OMath aligné à gauche dans la cellule.

Si l'un de ces 4 scénarios reste centré → fix incomplet, signaler.

## Plan en cours — état d'avancement

Fix interstitiel hors des 4 chantiers principaux ROADMAP. Lie au
chantier de **validation user-facing** de l'insertion Word (4 invariants
posés par l'utilisateur 2026-05-13) :

- [x] **Invariant 4 — alignement gauche** : posé uniformément
- [ ] **Invariant 3 — curseur post-commit dans même ¶** : repro précis
  encore à fournir par l'utilisateur
- [x] **Invariant 1 — position d'insertion** : déjà OK (pipeline 3 stratégies)
- [x] **Invariant 2 — comportement en tableau** : présumé OK (même
  pipeline), à confirmer en validation manuelle (scénario 4 ci-dessus)
