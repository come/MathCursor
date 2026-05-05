# Refactor — Insertion OMath via build isolé + transplant XML

**Date :** 2026-05-04
**Kind :** Meta
**Température :** molle
**Statut :** acté

## Contexte

Le pipeline `InsertOMathAt` actuel fait :

```csharp
replaceRange.Text = unicodeMath;       // remplace la zone par le linéaire
mathRange.OMaths.Add(mathRange);       // crée la math zone
mathRange.OMaths.BuildUp();            // convertit en display OMath
```

Cette séquence est **comportementalement instable** quand la zone cible est
proche d'un OMath voisin :

- **Bug 04-05 absorption** : revert d'un multi-ligne P1 puis re-conversion
  fait que `BuildUp` aspire l'OMath voisin P2 dans le nouvel OMath
  (vérifié sur le docx user : 4 paragraphes → 3, P1 contient 2 `<m:eqArr>`
  côte à côte, le `<w:bookmarkEnd>` de P2 est dans le même `<m:oMath>`).
- **Bug 04-05 ¶ mangé** : la combinaison Delete + Replace + BuildUp peut
  consommer un `¶` voisin et faire remonter le paragraphe d'après.
- **Effets collatéraux multiples** : strip ¶ vide trop large, caret resté
  dans la zone math, etc. — chacun corrigé individuellement, mais la
  classe entière de bugs vient de la fragilité du pattern Replace+BuildUp
  *in place*.

## Décision

Refactoriser l'insertion OMath via un pattern **build-isolated → transplant
XML** :

1. **Build isolé** : `BuildOMathXmlIsolated(latex)` insère le
   `unicodeMath` à la fin du document (zone temporaire, isolée des
   autres OMaths), fait `OMaths.Add + BuildUp`, capture le
   `<m:oMathPara>...</m:oMathPara>` via `Range.WordOpenXML`, supprime la
   zone temporaire.
2. **Clean cible** : supprime le contenu du range cible
   (`Range.Text = ""`).
3. **Transplant XML** : `Range.InsertXML(capturedXml)` à la position
   cible. Pas de BuildUp à la cible → pas d'absorption possible des
   OMaths voisins (l'XML transplanté est figé, structurel).

### Pourquoi ça marche

- Word docx = OOXML (Office Open XML) — structure XML stable et
  documentée. `Range.WordOpenXML` et `Range.InsertXML` sont des API
  publiques utilisées par Word lui-même pour le copier-coller.
- L'isolation en zone temporaire évite que la BuildUp du nouvel OMath
  scanne ou interagisse avec les OMaths existants (bug d'absorption
  observé).
- Le transplant XML est purement déclaratif : on dit à Word « voici un
  paragraphe avec un `<m:oMathPara>` complet », Word l'intègre tel quel
  sans re-process la math zone.

### Compat .doc legacy

`Range.WordOpenXML` n'est dispo qu'en mode Word 2010+ (docx OOXML).
Détection via `doc.CompatibilityMode >= wdWord2010 (= 14)`. Sinon
fallback sur l'ancien pattern API (Replace + BuildUp). Aujourd'hui 99 %
des docs sont en mode 2010+, le fallback est de pure sécurité.

## Impact

- **`SuggestionService.cs`** :
  - Nouveau helper privé `BuildOMathXmlIsolated(Word.Document doc, string latex) → string`
  - Refactor de `InsertOMathAt` : pattern build-isolated → transplant
    XML quand `CompatibilityMode >= wdWord2010`, fallback API sinon
- Pas de changement core/lattice : le pipeline produit toujours le même
  latex/unicodeMath ; seule la stratégie d'insertion change.
- Pas de changement Mode 2 cascade, finalize, alignement : ces étapes
  restent intactes.

## Bénéfices attendus

- Élimine la classe de bugs « BuildUp absorbe le voisin » et « ¶ vide
  bouffé » qu'on a chassés en série.
- Code plus déterministe : on raisonne au niveau **noeud XML** et non
  sur les comportements opaques de l'API Word.
- Ouvre la voie à d'autres refactorisations XML-first (ex : revert via
  manipulation directe des noeuds XML, génération d'OMath sans BuildUp
  pour les cas simples).

## Tradeoff

- Code +~150 lignes (helper + branche XML dans `InsertOMathAt`).
- Coût performance d'un round-trip via zone temporaire (négligeable
  perceptuellement, < 50 ms pour un OMath multi-ligne).
- Si `Range.InsertXML` rejette le XML transplanté (rare mais possible si
  Word mute la version OOXML), fallback sur le pattern API.

## Validé par l'utilisateur

> « par contre on est sur de l'analyse XML non on va etre capable de
> reflechir au niveau noeud tout le temps non ? sauf si l'utilisateur à
> dit au debut de word que sn format etait open office / office (je sais
> plus trop la diff) »

> « oui merci go go go »

## Liens

- ADR refactor cross-merge 4 phases : [`2026-05-04-Meta-cross-merge-pipeline-refactor.md`](2026-05-04-Meta-cross-merge-pipeline-refactor.md)
- ADR multi-ligne edit cascade : [`2026-05-04-Feat-multiline-edit-cascade-merge.md`](2026-05-04-Feat-multiline-edit-cascade-merge.md)
- Brief multi-ligne Phase 1 : [`../briefs/2026-04-30-multiline-systems-equivalences.md`](../briefs/2026-04-30-multiline-systems-equivalences.md)
