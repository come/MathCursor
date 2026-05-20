# Refactor — Merger pur + insert atomique (élimine le legacy path et la pré-suppression)

**Date :** 2026-05-12
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** ADR `2026-05-07-Fix-insert-via-paragraph-xml-splice`, ADR `2026-05-11-Fix-omath-splice-content-based-navigation`, ADR `2026-05-12-Perf-commit-pipeline-three-stage-stack`

## Citation acté

> "ok nettoie, completement le code, et fait le truc propre je veux plus
> de legacy.. la le produit sent le produit baclé... je veux du robuste
> et du propre ! jusque dans l'archi" — utilisateur, 2026-05-12
>
> "que le core soit sobre concis, et rock solide"
>
> "si il faut creer un doc word fantome pour hoster le omath en attendant
> de l'inserer c'est ok"

## Contexte — le bug qui révèle la dette architecturale

Bug observé 12-05 : commit d'une OMath sur 2 lignes (cross-paragraph
merge `F(x)=1/x` + `<=> f(x) = 2/x` → `\begin{align*}`). La première
ligne est rayée du doc, le bloc align n'apparaît pas. Perte de donnée
nette.

Trace du log :
```
xparMerge_mode1: absorbed OMath top range=[0,13] source="F(x) = 1/x"
bookmark deleted: mcEq_eq_b09b70b8339d
merge_pre_delete_omath: deleted len=13 totalShrink=13     ← pré-suppression
...
insert_transplant: failed to splice doc XML (no match for paragraph sources)
commit ABORTED                                             ← échec après mutation
```

L'`InserterImpl` pré-supprime les OMaths absorbées (`DeleteOMathsInRange`)
AVANT de tenter `InsertOMathAt`. Si l'insert abort, la pré-suppression
n'est pas rollbackée → perte.

## Diagnostic — la pré-suppression est vestigiale

Le commentaire à `SuggestionService.cs:1991` justifie le pré-delete :
> "Cleanup post-merge : doit être fait AVANT InsertOMathAt (sinon Word
> refuse d'écraser un OMath via Range.Text)."

Audit du pipeline actuel : `Range.Text =` n'est utilisé QUE dans
2 endroits :
- `BuildOMathXmlIsolated` ligne 3267 → range zero-width à `doc.Content.End - 1`,
  aucun OMath là. Safe par construction.
- Fast path B1 (ADR 2026-05-12 perf stack) → gated par
  `paraRange.OMaths.Count == 0`. Impossible si OMath absorbée.

Les autres chemins (splice inline, legacy multi-ligne) utilisent
`Range.InsertXML` qui **gère l'overwrite d'OMath atomiquement** (MS Learn
confirme : `InsertXML` replace range content peu importe ce qui s'y
trouve).

→ Le pré-delete n'est nécessaire pour AUCUN chemin du pipeline actuel.
C'est une dette héritée d'une version antérieure où `Range.Text` était
utilisé sur le range absorbé. Aujourd'hui : vestige dangereux.

Et le legacy multi-ligne (`doc.Content.WordOpenXML` 637KB + tail-match
des paragraphes + `doc.Content.InsertXML` full) est fragile :
- Lourd (637KB read + 637KB write = ~750ms sur gros doc)
- Fail mode "no match for paragraph sources" quand les `<w:r>` ont des
  résidus (`<w:proofErr>`, `<w:bookmarkEnd>` orphelin du pré-delete)
  qui cassent `TryMatchTailRunSequence`
- Et le tail-match ne sait pas matcher un paragraphe qui contient une
  OMath voisine (Range.Text renvoie un char spécial pour OMath, pas le
  contenu `<w:t>`)

## Décision

Refactor en 3 invariants durs :

### Invariant 1 — Merger PUR (aucune mutation du doc)

Le merger calcule un **CommitPlan** (positions PRE-deletion, contenu,
handles absorbés). Il ne touche pas au doc. `InserterImpl` ne call plus
`DeleteOMathsInRange`.

### Invariant 2 — Insert ATOMIQUE via `Range.InsertXML`

Une seule transaction Word par commit. Pour chaque scénario :

| Scénario | Range cible | Contenu inséré |
|---|---|---|
| Inline simple, ¶ pur | typedRange | UnicodeMath + BuildUp (fast path B1) |
| Inline simple, ¶ avec voisins texte | firstPara.Range | ¶ XML splicé (existing) |
| Inline avec absorb voisin | firstPara.Range | ¶ XML splicé enrichi (retire bookmarks + OMath absorbés) |
| Cross-para / display math | doc.Range(absStart, absEnd) | pkg du `BuildOMathXmlIsolated` directement |

Dans tous les cas : opération atomique. Si l'XML est invalide ou la
range est cassée, Word abort sans muter. Pas de demi-état.

### Invariant 3 — Cleanup POST-success uniquement

Suppression des handles absorbés du store + bookmarks ne se fait
qu'APRÈS `Range.InsertXML` réussi. Si l'insert fail, doc + store +
bookmarks restent dans l'état pré-commit. Cohérent.

## Code mort à retirer

- `SuggestionService.DeleteOMathsInRange` (lignes 3165-3194) — plus
  appelée.
- `InlineOMathSplicer.ReplaceParagraphsInDocXml` (lignes 359-450) —
  plus appelée. Le tail-match multi-¶ ne sert plus.
- Bloc legacy dans `InsertOMathAt` (lignes ~3611-3705) — la lecture
  `doc.Content.WordOpenXML` full + replace + `doc.Content.InsertXML`
  remplacée par 1 ligne `doc.Range.InsertXML`.
- `InlineOMathSplicer.ExtractFirstWPElement` si plus de caller.

## Gains

- **Zéro perte de donnée** sur abort (atomique by Word API design).
- **Perf legacy** : ~750ms → ~150ms (1 InsertXML scoped au lieu de
  full-doc).
- **Surface de code réduite** : ~250 lignes de dead code supprimées.
- **Robustesse** : élimine 2 modes d'échec connus (`merge_pre_delete`
  non-rollbackée, tail-match fragile sur `<w:proofErr>`).

## Risques + mitigation

**R1 — `Range.InsertXML(absStart, absEnd, capturedPkg)` quand range
contient une OMath absorbée.**
Mitigation : confirmé par MS Learn (`InsertXML` REPLACES range
content). Si défaillance in vivo, log clean + abort visible. Pas de
mutation partielle.

**R2 — Splicer inline enrichi doit virer les `<w:bookmarkStart>` /
`<w:bookmarkEnd>` des handles absorbés.**
Mitigation : la liste `RemovedHandles` est dispo dans le CommitContext.
Le splicer scanne les bookmarks `mcEq_<handle>` dans le `<w:p>` et les
retire avec leur OMath.

**R3 — Range non-paragraph-aligned passé à `Range.InsertXML` avec un
`<w:p>` dans le pkg.**
Mitigation : on opère TOUJOURS sur des ranges paragraph-aligned
(`firstPara.Range` pour inline, `Paragraph.Range` étendu pour cross-
para). Pas d'insertion mid-paragraph avec une nouvelle ¶.

## Ordre d'exécution

**Phase C.1 — bug data loss + insert atomique** (priorité) :
1. ADR (ce fichier) + index README.
2. Splicer enrichi : `InlineOMathSplicer.SpliceOMathInDocXml` étendu
   pour retirer absorbed bookmarks/OMaths.
3. `InsertOMathAt` refactor : route display math → atomic
   `Range.InsertXML(absStart, absEnd, capturedXml)`.
4. `InserterImpl` : suppression de l'appel à `DeleteOMathsInRange`.
   Cleanup store + bookmarks déplacé en post-success.

**Phase C.2 — ghost doc + cleanup dead code** :
5. `OMathStagingService` : `Word.Document` fantôme (lazy, hidden) qui
   héberge `BuildOMathXmlIsolated`. Zéro mutation du doc actif user.
6. Kill dead code : `DeleteOMathsInRange`, `ReplaceParagraphsInDocXml`,
   bloc legacy dans `InsertOMathAt`, `ExtractFirstWPElement` orphelin.
7. Bump 0.6.0.

Chaque étape testée in vivo avant la suivante. Code sobre, concis,
rock solid : pas de commentaires bruyants sur le QUOI, juste le WHY
non-obvious.
