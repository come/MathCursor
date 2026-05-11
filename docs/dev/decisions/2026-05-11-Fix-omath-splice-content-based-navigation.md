# Fix — Splice XML navigué par parent/siblings et matching par contenu (durcit le pattern XML pour tableaux + tout conteneur)

**Date :** 2026-05-11
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** ADR `2026-05-07-Fix-insert-via-paragraph-xml-splice`, ADR `2026-05-04-Refactor-omath-via-xml-transplant`

## Citation acté

> "il me semble que on devrait partir du curseur, pour voir ou il est..
> et ensuite decider ce qu'on fait en fonction des siblings et du
> courant non ? qu'il soit dans un tableau ou non" — utilisateur,
> 2026-05-11
>
> "je veux qu'on assainisse et durcisse le systeme de recherche en
> utilisant le parse xml correctement pour naviguer dans le parent et
> les siblings du curseur"

## Décision

Le splice XML d'`InlineOMathSplicer` cesse de raisonner par **index global
plat** dans `body.Elements("w:p")` et passe à un raisonnement **local par
contenu + parent immédiat**. Le code marche par construction dans tout
conteneur (`<w:body>`, `<w:tc>` de tableau, `<w:sdt>`, `<w:hdr>`,
`<w:ftr>`, …) sans jamais détecter explicitement le type de conteneur.

Concrètement, deux changements :

1. **`SpliceOMathInDocXml(fullDocXml, mathSource, newOMathXml)`** —
   identification du `<w:p>` cible par **contenu** :
   - Scanner `xdoc.Descendants(W + "p")` (au lieu de
     `body.Elements(W + "p")[targetIdx0]`).
   - Filtrer les `<w:p>` dont les derniers `<w:r>` concaténés finissent
     par `mathSource` (le matching existant en queue, juste appliqué par
     contenu et non par index).
   - S'il y a plusieurs candidats (rare — la source brute fraîchement
     tapée est presque unique) → prendre le dernier dans l'ordre
     document (l'utilisateur vient de taper là).
   - Splicer **dans ce `<w:p>` localement** (`targetPara.ReplaceNodes` +
     `WrapStandaloneOMathWithJcLeft`) sans jamais redescendre au body
     global.
   - Le paramètre `targetParaIdx0` disparaît de la signature publique.

2. **`ReplaceParagraphsInDocXml(fullDocXml, mathSource, newParaWp)`** —
   identification du groupe contigu de `<w:p>` siblings :
   - Localiser le **dernier `<w:p>`** dont la queue match `mathSource`
     (cf. point 1).
   - Remonter `paraTarget.ElementsBeforeSelf(W + "p")` siblings stricts
     du même `.Parent` tant que la **concaténation** de leurs sources
     (de haut en bas) ne couvre pas `mathSource` complet.
   - **Refuser** (`return null`) si :
     - on traverserait un changement de `.Parent` (frontière
       `<w:tc>` ↔ `<w:body>` par exemple — la cross-merge entre cellules
       n'a pas de sens et était cassée silencieusement) ;
     - on rencontre un sibling non-`<w:p>` (table, SDT, image isolée)
       qui n'a pas vocation à être avalé par la cross-merge ;
     - aucun groupe contigu ne couvre `mathSource`.
   - Remplacer les N `<w:p>` du groupe par un seul `newParaWp` via
     `firstPara.ReplaceWith(newParaEl)` + `Remove()` sur les suivants
     (= comportement actuel, juste sur le groupe trouvé localement).
   - `targetIdx0` et `targetCount` disparaissent de la signature publique.

Côté appelant (`SuggestionService.InsertOMathAt`), cela retire ~30 LoC :

- Plus de calcul `firstTargetIdx0` / `targetCount` via boucle
  `doc.Paragraphs[i]` (ligne 3286-3297 actuelle).
- Plus de `safeProbeStart / safeProbeEnd` pour identifier les ¶s cibles
  par range Word.
- On passe `mathSource` (déjà calculé via `doc.Range(absStart, absEnd).Text`,
  safe car ces bornes sont purement texte par construction) au splicer.

## Pourquoi

- **Bug actuel (rapporté 2026-05-11) : `body.Elements("w:p")` ne descend
  pas dans les tableaux.** Dans une cellule, les `<w:p>` sont enfants de
  `<w:tc>` → `<w:tr>` → `<w:tbl>` → `<w:body>`. La sélection `Elements`
  retourne uniquement les enfants directs du `<w:body>`. Résultat :
  `targetParaIdx0` calculé via `doc.Paragraphs[i]` (flat, inclut les
  paragraphes des cellules) pointe sur un index qui n'existe pas (ou
  pire, sur un mauvais `<w:p>` hors table). Le splice retourne `null`
  silencieusement, le pipeline **n'a pas de fallback API** (ADR 04-05),
  donc rien ne s'insère. C'est ce que l'utilisateur a observé : « rien
  ne marche dans un tableau ».

- **L'index global est un proxy fragile.** Il marche tant que le doc est
  plat. Il se casse dans les tableaux, mais aussi en théorie dans :
  - SDT (`<w:sdt>` avec content control) — un `<w:p>` à l'intérieur
    n'est pas enfant direct du body ;
  - Headers / footers (rares pour math, mais existants) ;
  - Footnotes / endnotes.
  Raisonner par contenu (= la source brute fraîchement tapée) supprime
  cette dépendance.

- **Le `.Parent` immédiat est le bon scope pour la cross-merge.** Un
  système `{` multi-ligne dans une cellule a ses ¶s siblings dans le
  même `<w:tc>`. Un système multi-ligne dans le body a ses ¶s siblings
  dans le `<w:body>`. La cross-merge **ne doit jamais** traverser un
  `<w:tc>` (= passer d'une cellule à un ¶ hors table, ou pire à une
  autre cellule) — c'est sémantiquement absurde et impossible à
  reconstruire en OOXML. Le critère « même `.Parent` » exprime cette
  contrainte directement, sans détecter explicitement les tables.

- **Le `<w:p>` cible par contenu est non-ambigu en pratique.**
  L'utilisateur vient de taper `mathSource` à la position courante. La
  probabilité que le même texte existe ailleurs dans le doc en queue
  d'un `<w:p>` est négligeable, et de toute façon « prendre le dernier
  dans l'ordre document » match l'intuition utilisateur (le plus récent
  / celui où il a tapé).

## Plan TDD

Tests synthétiques (fragments XML hard-codés, pas de Word interop) à
écrire **avant** le refactor, dans `InlineOMathSplicerTests.cs` :

| # | Fragment | mathSource | Attendu | État avant fix |
|---|---|---|---|---|
| 1 | `<w:body><w:p>texte + math</w:p></w:body>` | `math` | Splice OK | **GREEN** (régression à préserver) |
| 2 | `<w:body><w:tbl><w:tr><w:tc><w:p>texte + math</w:p></w:tc></w:tr></w:tbl></w:body>` | `math` | Splice OK dans le `<w:p>` de la cellule | **RED** → doit devenir GREEN |
| 3 | Cellule avec 2 `<w:p>` consécutifs, math en queue du 2e | `math` | Splice dans le 2e `<w:p>` uniquement | **RED** → GREEN |
| 4 | Cellule avec 2 `<w:p>` siblings, source d'une chaîne `=` répartie sur les 2 | source concat | `ReplaceParagraphsInDocXml` remplace les 2 par 1 | **RED** → GREEN |
| 5 | Source qui s'étendrait d'une cellule au paragraphe suivant hors table | source impossible | `return null` (refus propre) | (nouveau) GREEN par construction |

Les tests existants (`InlineOMathSplicerTests`,
`InsertTransplantIntegrationTests`) doivent rester verts — ils utilisent
des fragments avec `<w:p>` direct sous `<w:body>`, donc le passage à
`Descendants` + identification par contenu doit toujours les satisfaire.
Risque de régression à surveiller : tests qui passent **deux**
`<w:p>` au même `mathSource` (collision) — peu probable mais à vérifier
sur la batterie existante.

## Hors scope

- Pas de support cross-merge multi-cellules (refusé, sémantiquement
  invalide).
- Pas de support cross-merge cellule↔body (refusé pour la même raison).
- Pas de fallback API `OMaths.Add+BuildUp` réintroduit (ADR 04-05
  toujours valide — pas de fallback, le splice doit marcher ou rien).
- Pas de détection runtime de « est-on dans une table ? » côté
  `SuggestionService`. Le code adapter reste agnostique du type de
  conteneur ; la robustesse vient du splicer XML.

## Suivi

- Après le fix, retirer du log diag les messages
  `para_splice: skip (no match)` qui devenaient le symptôme silencieux
  du bug table — remplacer par un message qui dit pourquoi (no `<w:p>`
  with `mathSource` in tail, vs. multi-paragraph group not coherent in
  same parent).
- Probable que d'autres usages de `body.Elements(...)` ailleurs dans le
  codebase aient la même fragilité (à grepper). Si trouvés, créer un
  ADR de cleanup séparé.
