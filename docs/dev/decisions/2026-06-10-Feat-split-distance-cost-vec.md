# Feat — Distance de découpe = coût (plus un filtre) + « vec » splittable

**Date :** 2026-06-10
**Kind :** Feat
**Température :** molle (constante SplitPenalty ajustable ; principe ferme)
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-culture-scoped-aliases.md](2026-06-10-Feat-culture-scoped-aliases.md), [2026-06-10-Fix-nbsp-keyword-case-tolerance.md](2026-06-10-Fix-nbsp-keyword-case-tolerance.md)

## Citation acté

> « je pense que c'est la responsabilité du moteur est de proposer un choix. avec sera dans les choix au même titre que a.vector » puis « ok go ! » — utilisateur, 2026-06-10 (demande initiale : accepter `vecAB` en plus de `vec AB`, en préférant `Splittable` à une règle restreinte aux majuscules).

## Contexte

Demande : accepter `vecAB` collé. La mécanique existe (`Splittable`, celle de
`cosx`→`\cos(x)`), MAIS la règle de sélection des flux héritée du JS était
« le flux le plus découpé qui parse GAGNE, les autres disparaissent » :
vérifié empiriquement, `asin` → `a\sin(\square)` en auto, candidat UNIQUE —
la lecture « mot entier » n'était jamais proposée. Avec `vec` splittable,
`avec` serait devenu `a·⃗□` en auto sans alternative. Contraire au principe
produit : le moteur propose, la popup arbitre.

## Décision

1. **`ForestEngine.Run` : la distance de découpe devient un terme de COÛT**
   — les candidats de TOUS les flux lexicaux sont fusionnés, pénalisés de
   `SplitPenalty × (découpes_max − découpes)` avec `SplitPenalty = 3`
   (> PopupGap=2 : une découpe PROPRE reste en auto, ex. `sinx` ;
   < widen+trou=4 : le mot entier reste compétitif face aux découpes SALES).
   La note « expression dense » reste celle du flux le plus découpé.
2. **`Splittable.Add("vec")`** — `vecAB` → `\overrightarrow{AB}`.
3. **`Decompose` : une suite de MAJUSCULES consécutives = UN morceau** —
   découvert en route : dans `vec·A·B`, « A » (ampère) et « B » (byte) sont
   des mots-unités qui ne se joignent pas aux noms, la découpe mourait. Le
   regroupement `vec + AB` colle à la sémantique géométrie (paire de points,
   même esprit que l'ADR geo-point-pairs) et donne directement la flèche
   large `\overrightarrow{AB}`. NOTE : ce blocage unité est aussi ce qui
   protège `sinus`/`cosinus`/`spin` (fixtures baseline : « s » seconde,
   « us » µs) — NE PAS « corriger » les unités hors contexte nombre, c'est
   un garde-fou de la baseline (tenté, 3 fixtures cassées, retiré).
4. **`Decompose` : capitale de début de phrase en tête de run** — attrapé par
   le test de mutation du corpus (« VecAB » ne se découpait pas) : en tête de
   run, retenter le match Splittable avec la 1re lettre abaissée et émettre
   la clé MINUSCULE (la capitale d'AutoCorrect est involontaire — `VecAB` →
   `\overrightarrow{AB}`, `Sinx` → `\sin(x)` ; le `Pi` délibéré → `\Pi` passe
   par le chemin mot-entier, intact).

Effets verrouillés par fixtures : `sinx`/`taux` inchangés (auto) ; `avec` →
popup `["avec", "a\overrightarrow{\square }"]` ; `asin` → popup
`["asin", "a\sin(\square )"]` ; `vecAB`/`vecAB collé` → flèche.

## Tradeoff & alternatives écartées

- **Règle « préfixe en tête + majuscules »** (proposée initialement) :
  plus sûre localement mais ad hoc — l'utilisateur préfère le mécanisme
  générique existant ; le vrai correctif était la sélection de flux.
- **Liste de mots à ne jamais découper (PlainWords par culture)** : curation
  manuelle sans fin ; la fusion à coût rend la garde inutile (le mot entier
  est toujours dans la course).
- **Fusion par coût pur sans pénalité** : `sinx` deviendrait popup
  `[\sin(x), sinx]` à égalité — bruit sur le cas d'usage central.

## Conséquences

- **Code touché** : `ForestEngine.cs` (Run + Finish, ~15 lignes),
  `Vocabulary.cs` (1 ligne Splittable).
- **Fixtures** : les entrées dont la découpe est « sale » peuvent gagner une
  alternative en popup — diff mesuré sur les 310 et régénéré explicitement ;
  nouvelles fixtures `vecAB`, `vec AB`, `avec`, `asin`.
- **Divergence JS assumée** : la baseline JS garde l'ancienne règle ; le
  contrat de vérité est désormais fixtures.json (évolution produit).

## Validation post-fix

- Suites moteur/serialization/adapter vertes, mutations corpus comprises.
- Word : taper `vecAB` → flèche AB ; taper `avec` puis Ctrl+Espace → la popup
  propose « avec » en premier.
