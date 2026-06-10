# Feat — Contrat undo système : 1 action = 1 entrée, walker OMML→OMath (fin d'InsertXML dans le doc user)

**Date :** 2026-06-10
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-02-Feat-omml-insertion.md](2026-06-02-Feat-omml-insertion.md)
(la décision OMML est CONSERVÉE — seul le véhicule de pose change) ; ADR DocMath
`2026-05-11-Fix-commit-grouped-in-single-undo-record` (l'UndoRecordScope) ;
[2026-06-10-Feat-multiline-chain-eqarr-architecture.md](2026-06-10-Feat-multiline-chain-eqarr-architecture.md)
(les blocs eqArr passent par le même inserter).

## Citation acté

> « non mais du coup ce que je veux c'est grouper dans un customRecord le
> ctrl+Z le supprimera d'un coup » … « je veux que tu supprime ton
> contournement » … « je suis pas fan du ghost doc malgré tout ca me parait
> fou de devoir faire ca » … « c'est pas que les equations le probleme hein
> c'est tout le systeme ;) » … « ok go » — utilisateur, 2026-06-10

## Contexte

Après un commit, il fallait 3-4 Ctrl+Z pour retrouver le texte sténo. Un
contournement (interception Ctrl+Z + replay programmatique de `doc.Undo()`)
a été essayé puis **rejeté par l'utilisateur** — supprimé.

Diagnostic par sondes (`UndoRecordScope.Probe`, log `IsRecordingCustomRecord`
à chaque étape du commit) :

```
probe [après ZWSP TypeText]  recording=True
probe [après InsertXML]      recording=False   ← le tueur
```

**`Range.InsertXML` ferme le custom record Word.** Le « 1 Ctrl+Z » de
DocMath datait de l'ère TypeText+BuildUp (recette mai, groupage prouvé) ;
le passage à l'OMML chirurgical (ADR 2026-06-02) l'a cassé sans bruit —
personne n'avait re-testé le Ctrl+Z après le switch.

Banc POC 4 variantes (menu ribbon « POC undo », chacune dans un custom
record avec sondes, validé en Word par l'utilisateur) :

| Variante | Record intact ? | Verdict |
|---|---|---|
| V1 InsertXML direct | non | témoin négatif confirmé |
| V2 TypeText + BuildUp | oui | groupable mais re-parse (bugs lim/frac) — mort |
| V3 ghost doc + transplant FormattedText | oui | écarté : « pas fan du ghost doc » |
| V4 OMaths.Add + Functions.Add | **oui** | **retenu** |

## Décision

### 1. Invariant système (le contrat)

**1 action perçue par l'élève = 1 entrée dans l'historique undo Word.**
Vaut pour TOUS les flows qui mutent le doc, pas seulement la pose d'une
équation :

- commit simple (ClearZone + ZWSP + équation + anchor CC + Tag + caret) ;
- commit chaîne/système (suppression du bloc au-dessus + re-génération
  complète — le cas le plus sensible : un Ctrl+Z = « bloc d'avant + ma
  ligne sténo » d'un coup) ;
- suppression atomique anchor+OMath (Backspace/Suppr via hygiène) ;
- edit mode / revert (M3) — même contrat dès l'écriture.

Chaque flow est wrappé dans un `UndoRecordScope` nommé. Les sondes
(`Probe`, log si `START FAILED` / record fermé prématurément) restent en
place en permanence : tout flow qui fragmente est dénoncé dans le log.

Corollaire : les mutations correctives post-hoc (balayage d'anchors
orphelines de l'hygiène) créent des entrées undo hors record — avec le
vrai groupage, les états partiels qui fabriquaient ces orphelines
disparaissent ; le balayage doit devenir exceptionnel et loggé comme
anomalie. La boucle ZoneCleaner qui « supprime » N fois la même CC
(observée au log, ~20 itérations) est un bug de convergence à corriger
dans ce chantier.

### 2. Walker OMML→OMath (la première brique)

`LatexToOmml` **reste l'émetteur et la source de vérité** (les fixtures
restent le contrat). Un nouveau composant adapter
(`Host/OmmlToOMathBuilder.cs`) consomme son arbre `<m:oMath>` et le
construit dans le doc user via l'object model natif :
`OMaths.Add` + `Functions.Add` (Frac, ScrSub/Sup/SubSup, Delim, Nary,
Rad, Func, LimLow, Acc, Mat, EqArray) + runs texte — **plus aucun
`InsertXML` dans le doc utilisateur**. Trois chemins, pas deux :

| Chemin | Ce que Word reçoit | Re-parse ? | Undo |
|---|---|---|---|
| BuildUp (mort, mai) | ligne linéaire ambiguë | oui → bugs lim/frac | ok |
| InsertXML (2026-06-02) | arbre OMML explicite | non | **cassé** |
| Walker (cette ADR) | le même arbre, nœud par nœud via l'OM | non | ok |

La raison d'être de l'ADR OMML (structure explicite, zéro ambiguïté) est
conservée à l'identique.

**Fallback transitoire** : le walker pré-valide récursivement l'arbre
(whitelist stricte éléments + propriétés). Tout nœud non supporté →
l'équation ENTIÈRE retombe sur l'ancien chemin InsertXML (rendu correct,
undo dégradé), avec une ligne de log dédiée. On rétrécit le fallback au
fil des constats — jamais de demi-équation.

### 3. Conformance runner in-Word (le harnais)

Bouton debug : batterie de LaTeX couvrant tous les constructs émis par
`LatexToOmml` → walker → relecture `om.Range.WordOpenXML` → comparaison
normalisée à l'OMML attendu (drop `m:ctrlPr`/`w:*`, fusion des runs
adjacents). Rapport PASS/DIFF/FALLBACK par cas dans le log. C'est lui qui
prouve la fidélité propriété par propriété (begChr/endChr, chr nary,
marques d'alignement `&` des eqArr, espaces fines U+2009…).

### 4. Test d'acceptation (le bon test, pas le banc)

Torture par flow, en Word réel : tape sténo → commit → **un** Ctrl+Z =
sténo restituée, caret en fin ; étend une chaîne → un Ctrl+Z = bloc
d'avant + ligne sténo ; Backspace équation → un Ctrl+Z = équation de
retour. Sondes muettes dans le log = aucun flow ne fragmente.

## Tradeoff & alternatives écartées

- **Interception Ctrl+Z + replay `doc.Undo()`** : pile undo trafiquée
  depuis l'extérieur, heuristique de match texte, risque de sur-undo —
  rejeté explicitement par l'utilisateur.
- **V3 ghost doc + transplant `FormattedText`** : groupage prouvé au POC
  et zéro réécriture — mais un document fantôme permanent pour contourner
  un effet de bord d'API est de la dette déguisée ; rejeté par
  l'utilisateur (« ça me paraît fou de devoir faire ça »). Reste documenté
  comme plan de secours si un construct s'avère inexprimable via l'OM.
- **Retour BuildUp** : re-parse ambigu, bugs de précédence insolubles
  (cf. ADR 2026-06-02) — mort.
- **`UndoClear()` post-commit** : nuke TOUT l'historique du doc,
  destructif pour le travail de l'élève — rejeté.
- **Re-Start du record après InsertXML** : donnerait 2-3 records au lieu
  d'un, l'invariant n'est pas atteint.

## Conséquences

- **Code touché** : suppression du contournement (`TryUndoLastCommit`,
  `InvalidateUndoGrab`, `OnUndoPressed`/VK_Z — ConversionController,
  ThisAddIn, KeyboardInterceptor) ; `UndoRecordScope` instrumenté
  (log START/END + `Probe` statique) ; sondes dans `OMathInserter` ;
  banc POC `Host/UndoPoc.cs` + menu ribbon (téméraire, à retirer une fois
  le walker acté en prod) ; à venir : `Host/OmmlToOMathBuilder.cs`,
  bascule de `BuildOMathViaOmml` sur le walker avec fallback InsertXML,
  conformance runner.
- **Tests** : le walker est du COM pur (non mockable) → harnais =
  conformance runner in-Word sur la batterie + torture par flow.
  Les tests `LatexToOmmlTests` (39) et `OmmlCoverageTests` restent le
  contrat de l'émetteur, inchangés.
- **API publique** : aucune — tout est interne à l'adapter.
- **Perf** : N appels COM par nœud (~10-30 nœuds pour une équation
  lycée) vs 1 InsertXML (~120 ms mesuré) — à mesurer au runner, aucun
  problème attendu.

## Retour Word (2026-06-10, même jour) — walker v1 RETIRÉ de la prod

> « non en fait c'est foireux tu peux regarder les logs, y'a des trucs qui
> sont bouffés.. repart sur la version d'insertion avant, tant pis pour le
> controle z foireux » — utilisateur, 2026-06-10

Test réel : fidélité KO — exposant mangé (`x^{4}` perdu dans
`\frac{1}{x+x^2+x^3+x}`), OMath vide résiduelle (« Tapez une équation
ici. »). **Prod re-basculée sur InsertXML** (un seul edit dans
`BuildOMathViaOmml`). L'invariant du contrat reste acté ; le walker est à
reprendre HORS prod via le conformance runner (bouton POC conservé) et ne
sera re-branché qu'à 100 % PASS.

Acquis des sondes pendant l'essai : le record **survit** au walker ET à
`CC.Add` — mais meurt ensuite **entre CC.Add et la fin du Tag** (suspects :
écriture `cc.Tag`, lecture `om.Range.WordOpenXML` du hash). Il y a donc un
2ᵉ tueur de record à identifier dans le même chantier ; corriger
l'insertion seule ne suffira pas.

## Validation post-fix

1. Conformance runner : 0 DIFF sur la batterie (les FALLBACK sont
   tolérés, comptés, et listés pour résorption).
2. Torture par flow (ci-dessus §4) par l'utilisateur en Word réel.
3. Log : aucune ligne `undo-record record FERMÉ PRÉMATURÉMENT` ni
   `START FAILED` en usage courant.
