# Feat — Expérience : map hash(OMath) → source, suppression CC anchor + ZWSP

**Date :** 2026-06-11
**Kind :** Feat
**Température :** provisoire
**Statut :** acté
**Supersedes :** — (supersede conditionnel de
[2026-05-19-Feat-anchor-cc-pattern](2026-05-19-Feat-anchor-cc-pattern.md) et
[2026-06-10-Fix-anchor-cc-deletion-hygiene](2026-06-10-Fix-anchor-cc-deletion-hygiene.md)
SI l'expérience est GO et mergée — ils restent actés sur main/beta-clean)
**Lié à :** branche `exp-hash-source-no-cc`, word-api-helpers.md §5 (drift hash
WARN-only), ADR 2026-05-12 perf 3 couches, ADR 2026-06-10 undo-contract

## Citation acté

> « j'aimerai tester un truc c'est d'enlever completement la notion de CC et de
> charactere bizarre pour stocker la source. et plutot essayer de stocker
> naivement un tableau de correspondance inversée : hashduomath-> source ca
> permettrait d'etre plus sobre et plus rapide dans l'insertion, la ca n'a pas
> l'air fluide » — utilisateur, 2026-06-11 ; plan détaillé approuvé via plan
> mode le même jour (options actées : édition manuelle = lien perdu acceptable ;
> pas de rétro-compat legacy CC sur la branche).

## Contexte

Le pattern anchor CC (ADR 2026-05-19) sème par équation : 1 CC RichText +
1 ZWSP caché (Font.Hidden) + 1 Tag JSON, et impose en aval AnchorHygiene
(3 défenses), le démasquage avant suppression, des probes CC partout.
L'insertion « n'a pas l'air fluide » (ressenti utilisateur) et chaque commit
paie plusieurs allers-retours COM pour l'anchor seul.

Expérience : QUE l'OMath dans le document (sobriété), la correspondance
équation → source vivant dans une part `Document.CustomXMLParts` (conforme
CLAUDE.md) sous forme de map inversée `hash(OMath) → source`.

**Risque n°1 connu** (word-api-helpers §5) : Word **mute le `WordOpenXML`
post-commit** — le hash brut n'est pas stable. L'expérience repose sur une clé
canonique, validée par POC AVANT toute bascule (règle dure du projet).

## Décision

### Clé bi-niveau
- **K1 (accès, cheap)** : SHA1 de `om.Range.Text` (~0 ms vs ~60 ms WordOpenXML).
- **K2 (confirmation)** : SHA1 d'un **OMML canonique** — sous-arbre `m:oMath`
  seul, via XLinq (MC0001 : jamais de regex sur XML) : attributs supprimés,
  `w:rPr`/`m:rPr`/`w:*` de formatage supprimés, runs `m:r` adjacents fusionnés
  (absorbe le re-split save/reopen), sérialisation sans formatage.
- Lookup `K1 → liste` ; 1 entrée = match direct ; ambiguïté = K2 départage.
  Le **revert** (destructif) confirme TOUJOURS par K2.
- **Doublons** : équations identiques = 1 entrée, dernier-écrit-gagne.
- **Entrées mortes** : pas de GC ; cap 500 entrées, éviction par `parsedAt`.

### Store
`Host/SourceMap/` : `EquationSource` (POCO), `OmmlCanonicalizer` (pur),
`SourceMapXml` (part XML native `urn:mathcursor:source-map:v1`, pas de
JSON-in-CDATA), `SourceMapStore` (CustomXMLParts, delete+re-add, cache mémoire
write-through keyé `part.Id`), `SourceMapResolver` (surface API de
`CcMetaResolver` : ResolveAt/IsOurs/ResolveBehindCaret).
`IEquationStore` (host-contract, keyé handle) : non touché sur cette branche.

### Gate POC (obligatoire avant bascule)
Sondes ribbon debug (`Host/Debug/HashKeyPoc.cs`) : P1 insertion sans ZWSP/CC
(4 contextes, timings), P2 snapshot clés, P3 drift 5 scénarios (dont
save+close+reopen et copy-paste, snapshots persistés en part), P4
discrimination (x^2/x_2, frac/linéaire…), P5 roundtrip part + record undo.

| Gate | Seuil GO |
|---|---|
| G1 | K2 stable 5/5 scénarios drift |
| G2 | K1 stable 5/5 |
| G3 | K2 départage 100 % des paires piégeuses |
| G4 | médiane insertion ¶ vide ≤ baseline − 15 ms (baseline même jour) |
| G5 | display/eqArr corrects sans ZWSP (patches ×2-3 → STOP) |
| G6 | write part ≤ 10 ms @100 entrées, record undo intact |

NO-GO G1/G2 → l'identité par contenu est impossible dans Word (ADR Limit à
écrire) ; le canonicaliseur + fixtures restent réutilisables (conformance
runner). NO-GO G4 → la fluidité ne venait pas de la CC ; profil affiné.

## Tradeoff & alternatives écartées

- **K2 seul** : ~60 ms par atterrissage caret (WindowSelectionChange) — tue
  l'objectif fluidité.
- **K1 seul** : faux positifs structurels possibles (x² vs x₂) → revert sur la
  mauvaise source, inacceptable en correction.
- **JSON-in-CDATA dans la part** : double échappement, illisible — une
  CustomXMLPart EST du XML, schéma natif retenu.
- **Filet de rattrapage post-édition manuelle** (re-association heuristique) :
  écarté par décision produit — une équation éditée ne correspond plus à sa
  source, le lien perdu est cohérent.

## Conséquences

- **Sobriété doc** : plus aucun CC ni caractère caché MathCursor ; AnchorHygiene
  (H2 orphelines, H3 caret piégé) caduc par construction ; H1 (suppression
  atomique) réincarné en `EquationDeletionGuard` minimal.
- **Perf** : retrait par commit de TypeText ZWSP + Font.Hidden + CC.Add + Tag ;
  `Record()` recalcule K2 post-settle (~60 ms) mais remplace le hash Tag actuel
  qui lisait déjà WordOpenXML — net ≈ gain des opérations CC.
- **Undo** : moins d'opérations par `UndoRecordScope` ; write de part hors
  scope si G6 l'exige.
- **Pertes assumées** : équation éditée à la main = plus d'edit-mode/revert
  dessus ; doublons = dernier-écrit-gagne ; `HandleId` ne survit pas au doublon
  (usage actuel : logging + event, sans conséquence).
- **Code touché si GO** : OMathInserter (retrait étapes ZWSP/CC/Tag),
  ChainController, EditModeController, ThisAddIn, suppression Host/CCMeta/ et
  AnchorHygiene.cs, élagage ZoneCleaner (passe CC conservée pour CCs
  étrangères), 2 csproj.
- **Docs** : word-api-helpers.md largement périmé (§5/§6/§8 décrivent du code
  supprimé au rewrite Phase 2) — purge prévue dans le lot.

## Validation post-fix

Grille manuelle Word (14 scénarios) : ¶ vide display, inline prose, re-commit,
edit-mode, revert inline + multiligne, Backspace/Suppr, copy-paste + edit-mode
sur la copie, save/reload + revert, chaîne 3 lignes, système 2 lignes,
2 équations identiques + revert de chacune, équation éditée à la main (lien
perdu = attendu), Ctrl+Z post-commit, liste à puces. Timing vs baseline.
