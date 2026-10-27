# Fix — Plus de trou gauche en tête de formule (« □, ∀x… », « □/x »)

**Date :** 2026-10-27
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** la sous-décision du commit `1a44dc5` (2026-06-10) « le trou gauche des infixes non-relation (« /2 » → □/2) reste un aperçu de saisie légitime »
**Lié à :** garde « relation en tête » (`ForestEngine.Assemble`, `LeadingRelationTests`, 2026-06-10) ; analyse des documents du testeur (point « un trou □ ne devrait jamais être en auto », mis de côté)

## Citation acté

> « si qu'est ce qui est detecté quand on voit le carré puis virgule ? ca ne devrait pas
> sortir en formule et certainement pas en conversion finale » puis « je valide le plan mais
> je veux du testing fort avant apres, ca m'a l'air assez sensible » — utilisateur, 2026-10-26/27

## Contexte

Cas réel : « Dériver f(x) = …, V x in R/{3} ». Le NER a inclus la virgule de
la phrase dans la zone (`", V x in R/{3}"`) ; le moteur a lu une liste « (vide), ∀x… » et
la conversion a produit « □, ∀x ∈ ℝ∖{3} » dans le document.

Le moteur refusait déjà une **relation** en tête (« =2x » : membre gauche vide = pas une
expression, garde du 2026-06-10), mais pas un **séparateur** ni un **opérateur binaire** :
`, x` → □,x (auto), `: c'` → □:c', `* x` → □×x (auto), `/ x` → □/x (auto).

Un trou **à droite** (`x +` → x+□) est l'aperçu légitime d'une frappe en cours, de gauche à
droite. Un trou **à gauche** n'est jamais une frappe en cours : c'est une zone mal découpée.

## Décision

Aucune lecture quand l'entrée commence par un opérande gauche manquant :

- **Parseur** (`Parser.ParseSpan` / `parse_span`) : une plage vide tout au **début** d'une
  suite de jetons **non vide** ne donne plus de trou (`, x`, `*x`, `/x`, `: c'`…). Une suite
  entièrement vide reste un trou (membre droit « a= » → a=□) ; les trous à droite et les
  gabarits (`int` → ∫□, matrices à case vide) sont inchangés.
- **Segmentation** (`ForestEngine.Assemble` / `assemble`) : opérateur espacé en tête
  (`* x`, `/ x`) → premier segment vide → aucune lecture, comme la relation en tête.
- `+`, `-` (signes) et `^` (chapeau) ont une forme unaire : `-x`, `+ 3`, `^a` inchangés.

C# et Rust identiques. Côté Word, rien à changer : quand la zone NER ne donne aucune lecture,
l'auto-détection réessaie déjà une zone plus courte (« , définie V x in R/{3} » échouait puis
retombait sur « V x in R/{3} ») ; « , V x in R/{3} » suit maintenant le même chemin.

## Tradeoff & alternatives écartées

- **Retirer la ponctuation en tête de zone côté Word** (`ZoneRefiner`) : pansement local,
  VSCode et LibreOffice gardaient le défaut, et `* x` passait toujours.
- **Garder le trou gauche des infixes non-relation** (décision de juin, héritée du moteur JS) :
  aucun usage documenté (ni tutoriel, ni gabarit, ni mode édition) ; produisait des
  conversions fausses.
- **Réentraîner le NER** pour ne pas englober la virgule : lourd, et la règle moteur couvre
  tous les hôtes et toutes les zones.

Contrepartie : pendant la frappe, un état transitoire qui commence par un opérateur (« x + .. »
avant le 3e point de « ... », « = / ») n'a plus d'aperçu, au lieu d'un aperçu faux (« □·□·□ »).
Si le NER coupe la tête d'une fraction (« /2 » d'un « x/2 »), la popup se masque au lieu de
montrer « □/2 » ; le repli d'ancrage de l'auto-détection doit retrouver la zone entière (à
surveiller en usage).

## Conséquences

- **Code** : `engine/.../Parser.cs`, `ForestEngine.cs` ; `rust/mc-engine/src/parser.rs`,
  `engine.rs`.
- **Tests** : `LeadingRelationTests` (le test « /2 garde □/2 » devient « aucune lecture » sur
  7 cas + témoins unaires) ; 13 fixtures (7 « erreur », 6 témoins : `-x`, `+ 3`, `a=`, `x +`,
  `x ,`, matrice à case vide), 605 → 618 ; gate `run-tests.ps1` vert, conformance Rust 618/618.
- **Outil** : `engine_diff.py --extra FICHIER` (saisies supplémentaires dans le corpus).

## Mesure avant / après

Corpus de 25 880 entrées × 4 modes (fr, us, physique-chimie) : corpus large existant +
**7 293 saisies réelles relevées dans `mathcursor.log`** + 1 131 entrées ciblées (49
opérateurs/séparateurs en tête, au milieu, en fin ; exercices types ; tutoriel).

- **Avant** (moteur inchangé) : 0 écart HEAD/arbre ; parité C#/Rust : **5 écarts préexistants**
  (saisie réelle « lim n +inf 1/nT … » : lecture en C#, erreur en Rust), sans lien, à traiter
  à part.
- **Après** : 148 entrées changent en fr (126 en us). Revue exhaustive : **145 = ancienne
  lecture à trou gauche → aucune lecture** ; **3 = états de frappe transitoires** (« .. »,
  « = / ») dont l'aperçu était déjà faux. Composition multiligne et systèmes (1 242 requêtes) :
  0 écart. Parité C#/Rust : les 5 mêmes écarts préexistants, aucun nouveau.

## Validation post-fix

Dans Word (add-in recompilé) : « Dériver f(x) = …, V x in R/{3} » → ∀x ∈ ℝ∖{3}, sans □ ni
virgule ; taper « x/2 » d'un trait : popup stable jusqu'au bout.
