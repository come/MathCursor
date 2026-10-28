# Fix — Nom indicé appliqué à une parenthèse : P_A(B), f_n(x), u_n(1+r)

**Date :** 2026-10-28
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Lié à :** commit `4ed2461` (double exposant/indice parenthésé, sans ADR) ; exception « script atomique devant une unité » de [2026-10-14-Feat-scientific-notation-linear-minus](2026-10-14-Feat-scientific-notation-linear-minus.md)

## Citation acté

> « répare P_A(B) avant, ca n'a jamais marché ou c'est une regression ? » puis, sur le plan
> (nom indicé d'un bloc devant une parenthèse, indice qui avale une application pénalisé) :
> « oui on fait le plan » — utilisateur, 2026-10-28

## Contexte

Rédaction d'un exercice de probabilités conditionnelles : `P_A(B)` donnait **P_{A(B)}**, le
(B) dans l'indice, quelle que soit l'écriture. Même défaut pour `f_n(x)` → f_{n(x)} et
`u_n(1+r)` → u_{n(1+r)} (suites géométriques). **Jamais marché** : depuis le portage,
l'application `·apply` (looseness APP = 0) est plus serrée que l'indice (POW = 1) ; la lecture
(P_A)(B) existe dans la forêt mais coûte une inversion (application au-dessus d'un indice),
et P_{A(B)} n'en coûte aucune. Aucune fixture ne couvrait le cas ; la seule voisine,
`f_1_n(x)`, attendait f_{1_{n(x)}}.

## Décision

Dans le coût des inversions (`Score.Inversions` C#, `score::inversions` Rust, à l'identique) :

1. **Nom indicé d'un bloc** : une application dont l'opérande gauche est un indice à
   opérandes atomiques (P_A, f_n, u_n, x_1) n'est pas une inversion — comme l'exception
   existante du script atomique devant une unité (x² m).
2. **Indice qui avale une application** : un indice dont l'opérande droite est une
   application coûte 2 inversions (≥ l'écart de popup) → la bonne lecture passe en auto.

Indice seulement : l'exposant n'est pas touché (x^2(x+1) reste une popup à deux lectures).
Un indice qui contient vraiment une application s'écrit avec ses parenthèses : `x_(n(x))`.

## Tradeoff & alternatives écartées

- **Changer la looseness de l'application** (APP au-dessus de POW) : bouleverse toutes les
  priorités (sin x², f(x)², e^x…), risque large.
- **Règle lexicale** (« _ lettre ( » → jonction forcée) : contourne la forêt, casse
  l'indice composé `x_(n+1)`.
- **Popup à deux lectures** (pénalité 1) : la bonne lecture d'abord, mais une popup à chaque
  P_A(B) ; la lecture P_{A(B)} n'a pas d'usage au lycée.

Précisions venues de la mesure :
- « nom indicé » = indice dont la base est un atome et l'indice un atome ou une décoration
  tight d'atome (P_Ā : `P_bar(A)(B)`, où « (A)(B) » est un produit implicite collé et non une
  application → l'exception d'élargissement vaut pour l'application ET le produit collé) ;
- l'indice « avalant » est repéré aussi sous un script : `u_n(x)^2` lisait u_{n(x)^{2}}.

## Validation

- **Fixtures** : 618 → 626 (`P_A(B)`, `P_bar(A)(B)`, `f_n(x)`, `u_n(1+r)`, `P_A(B) = 0,3`,
  `P_A(B) + P_A(C)`, `P(A inter B) = P(A) * P_A(B)`, `x_(n(x))`) ; `f_1_n(x)` (double indice,
  popup à égalité) mise à jour. C# et Rust 626/626.
- **Mesure** `engine_diff.py` (référence `aa23396`, corpus 25 191 entrées dont 7 601 saisies
  réelles du journal et cas de probabilités), mode fr : 1 158 entrées changent.
  - **717** : la tête était un indice avalant (P_{A(B)}, f_{n(x)}, u_{n(1+r)}, u_{n(x)^{2}}…),
    elle devient le nom indicé appliqué ;
  - **74** : même tête, la lecture avalante disparaît de la popup ;
  - **65** réordonnées, toutes vers la lecture attendue : `1/f_1(x)` → \frac{1}{f_1(x)},
    `f_1(x)/x` → \frac{f_1(x)}{x}, `H_2O` → H_{2}O (au lieu de H_{2O}) ;
  - **291** même tête, popup élargie ou resserrée ; hors saisies absurdes du corpus généré
    (`abs_n(x)`, `cos_n(x)`, `f2_n(x)`, `f_(n+1)_n(x)` : la bonne lecture y entre en popup),
    131 cas, dont 22 popup → auto (`f_1(1/x)`) ;
  - **11** autres : doubles indices et `(x y)`, tous des saisies du corpus généré.
  Aucune saisie réelle du journal ne régresse.
- **Parité C#/Rust** : les 5 écarts préexistants (« lim n +inf 1/nT … »), aucun nouveau.
- Gate `scripts/run-tests.ps1` verte (dont le compteur de fixtures de `FixtureTests` → 626).
- Essai dans Word : à faire après recompilation de l'add-in.

## Revue avant commit (2026-10-28)

> « je veux etre safe sur l'architecture, et verifier que ca a pas bavé en patch » — utilisateur

Relecture indépendante (aucun point bloquant ; C#/Rust équivalents, pureté respectée). Resserrages :
- **Pénalité « indice avalant » limitée à l'application d'une lettre latine** (A(B), n(x)) :
  `u_phi(n)` passait en auto u_φ(n) alors que u_{φ(n)} (sous-suite) existait avant ;
  désormais popup à égalité, l'utilisateur choisit. Coût nommé `SWALLOW = 2` (≥ PopupGap).
- **Base d'un nom indicé = lettre ou symbole, jamais un nombre** : `f_1_n(x)` ne lit plus
  « 1_n » comme un nom (sa fixture redevient la popup d'avant).
- **Exception d'élargissement réservée** à l'application ou au produit implicite **devant
  un groupe** (`P_bar(A)(B)`) : `x_1y` garde sa popup à égalité d'avant.
- Fixtures : 626 → 629 (`u_phi(n)` popup, `P_A(B)^2` → P_{A}(B)^{2} en tête, `x_1y` popup).
- Mesure refaite : 721 têtes corrigées, 74 lectures fausses retirées, 67 réordonnées ;
  les 22 « autres » sont des saisies générées (doubles indices `f_(n+1)_n(…)`, `(x y)`),
  absentes des saisies réelles ; **leur lecture en tête change** (ex. (f_{n+1})_{n(−x)} →
  f_{(n+1)_n(−x)}), entre deux lectures également discutables, non départagées par une
  mesure ; aucune saisie réelle ne régresse ; parité C#/Rust : les 5 écarts préexistants.
- Limite connue : double indice (`f_1_n(x)`, `f_(n+1)_n(x)`) — lectures toujours à
  départager en popup.
