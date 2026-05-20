# Feat — Intra-OMaths merger : revival LaTeX-preserving, voisin gauche uniquement

**Date :** 2026-05-18
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-12-Refactor-pure-merger-atomic-insert.md](2026-05-12-Refactor-pure-merger-atomic-insert.md) (refacto précédent du merger, désormais débranché en phase B CC migration), [feedback_caret_local_probes.md](../../../C:/Users/wanadev/.claude/projects/D--Software-DocMath/memory/feedback_caret_local_probes.md) (probes locales + backlinks natifs)

## Citation acté

> « oui » — utilisateur, 2026-05-18, validant le plan « merge upstream
> d'InsertOMathAt + leftLatex préservé via cc.Tag.Latex + skip si hash drift »
> dans l'enchaînement précédent (« attention si la source a deja ete validée
> sur la partie gauche il faut bien garder ce qui a été validé mais oui »).

## Contexte

Bug observé après le fix « string-pos → Word interne » (commit `3f277bb`,
2026-05-18) :
- L'utilisateur tape `F(x)` puis convert → 1ère OMath `F(x)`.
- L'utilisateur tape `=1` à droite puis convert → 2ème OMath `=1` collée.
- **Résultat actuel** : 2 OMaths distinctes. **Souhaité** : 1 seule OMath `F(x)=1`.

Le merger historique (`Host/Merging/IntraOMathsMerger.cs` + `NeighborFinder.cs`)
a été débranché lors de la phase B (migration CustomXMLPart → CC.Tag JSON,
commit `91473ec`). Il est désormais en dead code mais reste fonctionnel
côté détection.

## Décision

### 1. Approche : merge UPSTREAM d'`InsertOMathAt`

Le merge se fait **avant** la création de l'OMath, en étendant la zone
d'insertion vers la gauche pour couvrir le neighbor :

```
NeighborFinder → trouve F(x) à gauche
  ↓
IntraOMathsMerger.TryMergeWithLeft(currentSource, currentLatex)
  → MergeResult { mergedSteno, mergedLatex, newAbsStart, leftCcToCleanup }
  ↓
SuggestionService : absStartForCtx = newAbsStart
                    source = mergedSteno
                    latex = mergedLatex
  ↓
InsertOMathAt(zone élargie, mergedLatex)
  → SetRange écrase F(x) + "=1" en une passe
  → 1 OMath produite (pas 2)
```

Alternative écartée : merge **post-Word** (insérer puis fusionner deux OMaths
via `OMaths.Add` sur la range combinée). Testé phase A, fragile sur wrappers
display/inline mixte. Bugs de Word qui re-rendait les sources tout seul.

### 2. LaTeX préservé : pas de re-rendu du voisin gauche

`mergedLatex = leftLatex + currentLatex`, où :
- `leftLatex` = lu depuis `cc.Tag.Latex` du voisin (= ce qui a été stocké
  au moment du commit validé du neighbor).
- `currentLatex` = LaTeX rendu pour la zone courante (déjà calculé en amont
  par le pipeline).

Aucun appel au renderer pour le voisin → pixel-identique avec ce que
l'utilisateur a vu/validé. Si la grammaire ou le renderer évoluent entre
deux versions, l'ancienne OMath reste intacte.

### 3. Drift detection : SHA1 du OMML actuel vs `meta.OmmlHash`

À la lecture du voisin :
```
currentHash = Sha1Helper.Compute(om.Range.WordOpenXML)
if (currentHash != meta.OmmlHash) → log WARN, mais merge quand même (phase 1)
```

**Note 2026-05-19** : la garde drift en SKIP s'est avérée trop sensible
dès le 1er test réel (commit `=1` adjacent à `F(x)` → drift `85647cc1 →
8beaa2b8` alors que rien n'a été édité manuellement). Word mute le
`WordOpenXML` de l'OMath entre le moment où `Tag` est posé (= hash stocké)
et le probe-time du commit suivant. Causes probables : post-commit layout,
`CcSticky`, autoformat Word, namespaces XML non-stables.

→ Phase 1 = **log WARN, on continue**. On garde la trace pour
investigation phase 2.

Phase 2 envisageable :
- Hash content-only canonicalisé (strip namespaces XML, attributs
  d'instance, ordre stable des éléments).
- OU extraire le LaTeX courant via Word → LaTeX (API
  `om.Range.WordOpenXML` + transform) et l'utiliser à la place de
  `meta.Latex`. Bénéfice : robuste aux édits manuels Word, plus de
  faux positifs.

### 4. Marker guard : merge SEULEMENT si source commence par marqueur

Liste : `=`, `<=>`, `=>`, `{`.

Sinon (ex: `g(x)` tapée après `f(x)`), pas de merge → 2 OMaths voulues
(cas légitime : deux expressions distinctes côte à côte).

### 5. Phase 1 : voisin GAUCHE uniquement

L'ancien `NeighborFinder.FindAdjacent` cherchait gauche ET droite. Le
besoin actuel est uniquement gauche (cas user : on tape **après** une OMath
existante). La détection droite est conservée dans `NeighborFinder` (utile
pour cross-merge multi-ligne phase 2) mais le merger intra-¶ phase 1 ignore
le retour `Right`.

## Tradeoff & alternatives écartées

- **Re-rendre depuis `mergedSteno = leftSteno + currentSource`** : casse
  l'invariant « ce que tu as validé reste validé ». Si le renderer change ou
  l'utilisateur a édité, on écrase. Rejeté explicitement par l'utilisateur.
- **Merge gauche + droite simultanément** : élargit le scope sans use case
  immédiat. Phase 2 si besoin observé.
- **Skip drift sans détection** : risque d'écraser un manuel sans avertir.
  Mieux vaut détecter et skip.
- **Tolérance 1 espace entre zone et neighbor** : présente dans l'ancien
  `NeighborFinder`. Conservée (l'utilisateur peut taper espace par habitude).
- **Re-créer NeighborFinder from scratch** : l'existant est déjà Phase-B
  aware (utilise `CcMetaResolver`). On le réutilise tel quel.

## Conséquences

### Code touché

- **`Host/Merging/MergeResult.cs`** : ajouter champ `MergedLatex` (nullable
  string). Si non null, `InsertOMathAt` l'utilise tel quel ; si null, le
  pipeline re-render depuis `MergedSource` (ancien comportement, plan B).
- **`Host/Merging/IntraOMathsMerger.cs`** : nouvelle méthode
  `TryMergeWithLeft(int absStart, int absEnd, string currentSource,
  string currentLatex) → MergeResult?`. Ne touche pas l'ancienne
  `TryMerge` (mais celle-ci reste dead code).
- **`Host/Merging/NeighborFinder.cs`** : inchangé, réutilisé tel quel.
- **`Host/SuggestionService.cs`** : appel au merger juste après le bloc
  translator string→internal, avant la création du `CommitContext`. Si
  succès → expand `absStartForCtx`, swap `source`/`latex`, log trace.
- **`Host/CCMeta/CcSticky.cs`** ou équivalent : supprimer le CC du voisin
  gauche au moment du commit (sinon orphelin). Investigation au câblage.

### Tests

- xUnit pour la logique de marker guard (pure fonction `IsMergeMarker(string)`).
- xUnit pour drift detection (mock CcMetaResolver, assert skip).
- Validation Word manuelle (= bouton ribbon debug) : `F(x)` + `=1` → 1 OMath.
- Cas régression : `f(x)` puis `g(x)` (pas de marker) → reste 2 OMaths.

### API publique

- `MergeResult.MergedLatex` ajouté (extension, rétro-compat).
- Nouvelle API publique côté `IntraOMathsMerger` : `TryMergeWithLeft`.

### Règles MC impactées

Aucune. Pas de regex sur XML, pas de splice LaTeX (on concatène 2 LaTeX
déjà valides — distinct du splice qui découpe un LaTeX rendu).

## Validation post-fix

Manuelle en Word :
1. Taper `F(x)` → Ctrl+Espace → 1 OMath `F(x)`.
2. Curseur derrière, taper `=1` → Ctrl+Espace → **1 seule OMath `F(x)=1`**
   (pas 2).
3. Taper `f(x)` → convert. Taper `g(x)` → convert. **Reste 2 OMaths**
   (marker guard ne déclenche pas).
4. Sur l'OMath `F(x)`, éditer manuellement dans Word (ex : changer en `G(x)`).
   Taper `=1`, convert. **Reste 2 OMaths** (drift détecté, skip merge).

## Plan en cours

Tasks #104-#108 :
- #104 ADR (en cours)
- #105 NeighborFinder — réutilisé tel quel, pas de modif
- #106 IntraOMathsMerger.TryMergeWithLeft + MergeResult.MergedLatex
- #107 Câblage SuggestionService
- #108 Tests + validation Word
