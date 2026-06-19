# Fix — Ctrl+Espace : span étendu jusqu'à la parenthèse ouvrante non fermée (matrices)

**Date :** 2026-06-19
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-18-Fix-input-autocorrect-fraction-factorial.md](2026-06-18-Fix-input-autocorrect-fraction-factorial.md) (mêmes délimiteurs de span), `adapter-vsto/src/MathCursor/Host/SpanComputer.cs`

## Citation acté

> « (a,b,c,d ;e,f ne me propose plus rien […] (a b c d; e f non plus » puis
> « si je ctrl espace ca devrait monter à minima non ? » → « oui » —
> utilisateur, 2026-06-19

## Contexte

Sur une **matrice en cours de frappe** (parenthèse pas encore fermée), Ctrl+Espace
ne propose rien ou un fragment faux :

- `(a b c d; e f` → span tronquée à `e f` → moteur **erreur** (deux lettres
  isolées) → **rien.**
- `(a,b,c,d ;e,f` → span `e,f` → propose `e,f` (moitié, faux).

Le moteur, lui, sait faire : `Analyze("(a,b,c,d ;e,f")` → matrice 2×4 complète.
Le bug est dans `SpanComputer` (calcul de la zone du Ctrl+Espace manuel).

**Cause :** `;` et `,` sont des délimiteurs de phrase, *sauf* à l'intérieur d'une
parenthèse. La détection « dans une parenthèse » de `ComputeSpanStart` compte les
fermantes rencontrées en **remontant** depuis le caret ; or pour une `(` **non
fermée**, sa profondeur ne « s'ouvre » jamais avant qu'on l'atteigne (en dernier)
→ les `;`/`,` à droite de la `(` sont vus hors-parenthèse → coupés.

## Décision

Quand le caret est **à l'intérieur d'un groupe `(`…/`[`… non fermé** (matrice /
tuple / intervalle en cours de frappe), la zone englobe tout le groupe :

- **`ComputeSpanStart`** : si une ouvrante non fermée englobe le caret
  (`EnclosingOpenBracket`), la zone **démarre à cette ouvrante** (les contraintes
  OMath et stopword restent appliquées ensuite). Sinon, repli sur le calcul
  existant (dernier délimiteur).
- **`ComputeSpanEnd`** (symétrie, pour un caret replacé au **milieu** d'une
  matrice) : la marche avant initialise sa profondeur de brackets au nombre
  d'ouvrantes non fermées **avant** le caret (`OpenDepthBehind`) → les `;`/`,`
  du groupe englobant ne coupent plus.

Le scan d'englobement s'arrête au **saut de ligne** (un groupe ne traverse pas
une ligne) — pas au `.` (sinon `(1,5 ;2,5` casserait sur le séparateur décimal).

Effet : `(a,b,c,d ;e,f` + Ctrl+Espace → span = tout → **matrice complète proposée**,
virgules **ou** espaces.

## Tradeoff & alternatives écartées

- **Ne rien faire côté span, tout régler par le moteur** : impossible, le moteur
  ne voit que ce que la span lui donne.
- **Traiter `;`/`,` comme jamais-délimiteurs** : casse la chaîne de raisonnement
  multi-zones (`x=2 ; y=3` doit rester deux zones hors parenthèses) — d'où la
  condition « uniquement dans un groupe ouvert ».
- **Étendre aussi l'auto-détection (NER)** : sujet **séparé** — le NER tronque
  aussi au `;` car le corpus a 0 matrice et 579 `;`-séparateurs ; ça relève d'une
  extension de corpus + retrain (hors périmètre de ce fix code).

## Conséquences

- **Code (L3, pur)** : `SpanComputer.cs` — `EnclosingOpenBracket` +
  `OpenDepthBehind` ; `ComputeSpanStart`/`ComputeSpanEnd` adaptés. Aucune API
  Word touchée (calcul de chaîne).
- **Tests** : `SpanComputerTests` — matrices non fermées (virgules/espaces),
  caret au milieu, fermées inchangées, `;` hors parenthèse coupe toujours,
  décimales dans la parenthèse. Les 5 cas existants restent verts (aucun n'a de
  parenthèse non fermée → repli inchangé).
- **Hors périmètre** : auto-détection NER sur matrices (corpus + retrain).

## Validation post-fix

`Span("(a,b,c,d ;e,f", end)` = `(a,b,c,d ;e,f` ; `Span("(a b c d; e f", end)` =
`(a b c d; e f` ; `Span("x=2 ; y=3", end)` = `y=3` (séparateur préservé) ;
`Span("(a,b,c;d,e,f)", end)` = inchangé.
