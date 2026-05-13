# Fix — Garde strip list_mode : ¶ avec OMath ne peut JAMAIS être effacé

**Date :** 2026-05-13
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** Log user 2026-05-13 13:29 (reproduction du bug) + ADR
[`2026-05-07-Feat-list-mode-marker-injection.md`](2026-05-07-Feat-list-mode-marker-injection.md) (origine du list_mode marker)

## Citation acté

> « la j'ai un soucis bizarre, de temps en temps, juste le fait de revenir
> dans la formule avec la fleche supprime purement et simplement la
> formule.. j'ai du mal à reproduire j'essaie de t'envoyer un log »
> — utilisateur, 2026-05-13
>
> « voila ! perte de caret, puis perte complet de la formule »
> — utilisateur, 2026-05-13 (en fournissant le log de reproduction)
>
> « oui super par contre je veux un test (TDD) RED avant ! »
> — utilisateur, 2026-05-13
>
> « go phase 2 » — utilisateur, 2026-05-13 (validation GREEN)

## Contexte

Log user 2026-05-13 13:29:18-23 capture la séquence :

1. Cross-merge align* multi-ligne réussit (range [0,29], 1 OMath dans ¶)
2. `list_mode_inject_error: Les objets mathématiques ne peuvent pas
   inclure de marques de paragraphe ou de caractères de saut` — Word
   refuse l'injection du marker `=` dans le ¶ qui contient l'OMath
3. Avant le fix : `_listMode.ClearAnchor()` seul → state machine reste
   active
4. User fait Escape → `OnListModeEnterPressed` ou équivalent →
   `ExitListMode` → `StripListModeMarkerFromCurrentLine()`
5. **Bug** : la méthode strip fait `Range(start, end).Text = ""` sur tout
   le ¶ sans vérifier qu'il contient une OMath → **toute la formule
   disparait**

## Décision

### Fix A — Garde pure `ListModeStripGuard.CanStripMarkerFromLine`

Nouvelle classe statique pure dans `adapter-vsto/src/MathCursor/Host/`,
testable en isolation :

```csharp
internal static class ListModeStripGuard
{
    private const int MaxMarkerLength = 4;  // "= " / "{ " / "&= " + marge

    public static bool CanStripMarkerFromLine(int omathsInPara, int contentLength)
    {
        if (omathsInPara > 0) return false;           // jamais effacer une formule
        if (contentLength <= 0) return false;         // rien à strip
        if (contentLength > MaxMarkerLength) return false; // texte user, pas un marker
        return true;
    }
}
```

`StripListModeMarkerFromCurrentLine` interroge la garde avant de toucher
au `Range.Text` et abort si refus, avec log explicite.

### Fix B — `InjectListModeMarker` catch → `_listMode.Reset()`

Avant : le catch faisait `_listMode.ClearAnchor()`, ce qui ne reset PAS
la state machine. Le list_mode restait actif → `ExitListMode` plus tard
appellait le strip sur un ¶ qui n'avait jamais reçu de marker.

Après : `_listMode.Reset()` (state machine + ancre) → impossible d'arriver
au strip si l'inject a échoué.

### TDD RED → GREEN

Phase 1 (commit `8789625`) : helper inerte qui retourne `true` toujours,
3 tests dont 2 RED.
Phase 2 (cet ADR) : garde implémentée + branchée + reset list_mode, 3
tests GREEN.

## Tradeoff & alternatives écartées

- **Alt — Just early-return dans `StripListModeMarkerFromCurrentLine` sans
  extraire en classe pure**. Rejetée : la logique de décision « ¶ contient
  une OMath, pas de strip » est non-triviale et mérite des tests
  unitaires dédiés (TDD demandé par utilisateur). Une classe pure isolée
  permet la couverture par xUnit sans Word interop.
- **Alt — Refacto plus large du list_mode flow**. Rejetée : scope du fix
  est ciblé (garde + reset), pas justifié d'étendre. Le list_mode flow
  reste à auditer plus tard si d'autres bugs émergent.
- **Alt — `MaxMarkerLength = 2`** (strict, juste pour `"= "` et `"{ "`).
  Rejetée : `"&= "` (align* marker côté multi-ligne) fait 3 chars, et
  certains markers futurs pourraient atteindre 4. Marge sans coût.

## Conséquences

### Code touché

- `adapter-vsto/src/MathCursor/Host/ListModeStripGuard.cs` (nouveau,
  logique pure)
- `adapter-vsto/src/MathCursor/Host/SuggestionService.cs`
  - `StripListModeMarkerFromCurrentLine` : branche la garde avant le
    `Range.Text = ""`, log explicite si refus
  - `InjectListModeMarker` catch : `_listMode.ClearAnchor()` →
    `_listMode.Reset()` avec commentaire explicatif
- `adapter-vsto/src/MathCursor/MathCursor.csproj` : `<Compile Include>`
  pour le nouveau fichier
- `adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj` :
  même fichier en mode `Link` (recompile dans assembly test)
- `adapter-vsto/tests/MathCursor.Tests/Host/ListModeStripGuardTests.cs`
  (nouveau, 3 tests)

### Tests

- Adapter : **429/429 verts** (baseline 426 + 3 nouveaux)
- TDD respecté : commit RED (`8789625`) avant ce commit GREEN

### API publique

- Aucun changement (classe interne, branchement interne).

### Règles MC

- Aucune.

## Validation post-fix

Reproduire le scénario log user :

1. Doc vide, taper `F(x) = 1/x` → Ctrl+Espace (formule 1 commit)
2. Taper `=2/x` → Ctrl+Espace (cross-merge align* déclenché)
3. Observer dans le log : `list_mode_inject_error: ... — list_mode reset
   to inactive` (au lieu de juste `list_mode_inject_error: ...`)
4. Taper flèche gauche puis Escape
5. **Attendu** : la formule est préservée. Le log montre soit
   - rien (list_mode déjà reset, pas d'appel à strip)
   - soit `list_mode: strip REFUSED for ¶[X,Y] omaths=1 contentLen=29
     — guard preserve user content` si strip est quand même appelé

Si la formule disparait quand même → la garde a un trou, signaler avec
log du compteur `omathsCount` rapporté.

## Plan en cours — état d'avancement

Fix interstitiel hors des 4 chantiers principaux ROADMAP. Lié au
chantier de **validation user-facing** de l'insertion Word (4 invariants
posés par l'utilisateur 2026-05-13) :

- [x] Bug **perte formule** sur cross-merge + Escape (cet ADR)
- [ ] Invariant 3 — curseur post-commit dans même ¶ (les 3
  `compute_after_omath_para_error` du log user signalent une cause
  séparée, à traiter)
- [ ] Validation manuelle Word sur les fix posés (warmup, alignment,
  strip guard)
