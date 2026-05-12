# Perf — Stack 3 couches sur le commit pipeline (gros doc, ~290ms → ~30-90ms)

**Date :** 2026-05-12
**Kind :** Perf
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** ADR `2026-05-07-Fix-insert-via-paragraph-xml-splice`, ADR `2026-05-11-Fix-omath-splice-content-based-navigation`

## Citation acté

> "c'est bcp trop du coup ... la latence est bonne ! mais ca marche pas …
> je veux etre ceinture bretelle sur ce sujet donc fait le combo le plus
> robuste !" — utilisateur, 2026-05-12
>
> "go"

## Contexte

Sur le gros doc utilisateur (~78KB, 1300 ¶s, 64 OMaths), un commit
prenait ~290ms en pipeline complet — ressenti utilisateur "naze". Les
fixes correctness (ordre `read paraXml` AVANT `BuildOMathXmlIsolated`,
scope paragraphe pour `LocateInsertedOMath`, scoped merge scan) ont déjà
ramené ça d'une situation cassée (1.2s d'abort+retry) à 290ms qui
*marche*. Mais le ressenti n'est toujours pas fluide.

Les leviers identifiés (cf. recherche `Range.XML` qui s'est révélée
dead-end car schéma 2003 sans namespace `m:`) :
- ~60ms : `firstPara.Range.WordOpenXML` (260KB pkg:package wrapper Word).
- ~70ms : `BuildOMathXmlIsolated` (insert+BuildUp+capture+delete à la
  fin du doc, requis pour éviter l'absorption des voisins).
- ~110ms : `InsertXML` (Word re-parse + re-render le ¶ patché 260KB+).
- ~50ms : reste (target_count, locate, post-commit).

## Décision

On empile **3 couches d'optimisation indépendantes**, chacune avec un
fallback vers le chemin actuel (= rien ne casse si un layer rate). Les
gains se cumulent dans le cas favorable mais chaque layer s'active
indépendamment.

### Couche 1 — Pure-paragraph fast path (~150ms gagné sur ¶ vide)

**Détection stricte** : `paraRange.Text.Trim() == mathSource.Trim()`
ET `paraRange.OMaths.Count == 0` ET `paraRange.Tables.Count == 0`.

**Action** : on appelle `range.OMaths.Add(typedRange)` + `BuildUp()`
directement sur le range typé. Plus de splice XML, plus de
`BuildOMathXmlIsolated`, plus de `InsertXML` 260KB. Cas dominant chez
l'utilisateur cible (lycéen PAP qui tape une formule sur sa ligne
vide).

**Risque** : si la détection est trop laxe et qu'il y a du texte avant
ou après, `BuildUp` absorbe les voisins (bug ancien). Mitigation =
détection au char près (Trim égalité stricte).

### Couche 2 — LRU LaTeX → OMath XML cache (~70ms sur répétitions)

**Pattern** : `Dictionary<string, string>` clé = LaTeX brut, valeur =
résultat de `BuildOMathXmlIsolated`. Capacité bornée (~32 entrées) avec
eviction LRU manuelle (pas de dépendance NuGet, contrainte CLAUDE.md
"pas de dépendances lourdes").

**Cas typiques** : un élève qui retape la même formule plusieurs fois
au fil d'un cours (`f(x)`, `f'(x)`, `\frac{a}{b}`, etc.). Skip
`BuildOMathXmlIsolated` complet → ~70ms gagné.

**Risque** : OMath dépendant du contexte de paragraphe (style, font).
Mitigation = invalidation du cache sur changement de document (event
`DocumentChange`).

### Couche 3 — Pré-fetch `paraXml` sur idle (~60ms cache hit)

**Pattern** : 1 seule entrée de cache `(paraStart, paraText hash) →
paraXml`. Refresh sur `WindowSelectionChange` quand le curseur entre
dans un ¶ différent ET reste stable > 200ms (via le `DispatcherTimer`
existant à `SuggestionService.cs:300`).

**Au commit** : on vérifie le hash. Cache hit → 0ms. Miss → fallback
live read (~60ms).

**Risque** : cache stale si user édite très vite. Mitigation =
validation du hash + fallback live read, c'est gratuit.

## Cible perceptuelle

| Cas | Latence (avant) | Latence (après stack) |
|---|---|---|
| ¶ vide, formule simple (cas dominant) | ~290ms | **~30-90ms** |
| ¶ avec voisins, 1re fois | ~290ms | ~230ms (cache prefetch) |
| ¶ avec voisins, formule répétée | ~290ms | ~160ms (prefetch + LRU) |

Sub-100ms = perception "instantané" sur le cas dominant. C'est le palier
visé.

## Alternatives écartées

- **`Range.XML` au lieu de `WordOpenXML`** : retourne du WordML 2003,
  pas de namespace `m:` → injection OMath impossible. Confirmé par MS
  Learn (Brian Jones archive). Dead end.
- **OpenXML SDK direct sur `.docx`** : nécessite save (prompt user ou
  silent save lent + risque sur fichier). Non viable phase 1.
- **Async pre-fetch via `Task.Run`** : Word COM est STA, sérialise sur
  UI thread de toute façon. Pas de gain réel.
- **`ExportAsFixedFormat` XML** : `wdExportFormatXML` n'existe pas (LLM
  hallucination). Seulement PDF/XPS supportés.

## Implementation order

1. **Couche 1 d'abord** (pure-paragraph fast path) — gain consistant
   sur le cas dominant, isolation propre du chemin existant.
2. **Couche 2 ensuite** (LRU latex→omath) — simple, peu de surface.
3. **Couche 3 en dernier** (pré-fetch) — la plus complexe (event
   wiring + invalidation), gain marginal vs les 2 précédentes.

Chaque couche dans son propre commit pour faciliter le revert si
régression.

## Fallback / dégâts limités

Chaque couche conserve le chemin de splice XML actuel en fallback :
- Couche 1 : si détection pure-paragraph rate, on passe en splice XML
  comme aujourd'hui.
- Couche 2 : cache miss → on rebuild via `BuildOMathXmlIsolated`.
- Couche 3 : cache miss ou hash diverge → live read.

Donc en pire cas (tous les layers ratent), on retombe sur la perf
actuelle. Aucune régression possible.
