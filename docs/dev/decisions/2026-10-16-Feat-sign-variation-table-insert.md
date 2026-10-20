# Feat — Insertion d'un tableau de signe / de variation préformaté

**Date :** 2026-10-16
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-11-Feat-ribbon-home-duo-plus-dedicated-tab](2026-05-11-Feat-ribbon-home-duo-plus-dedicated-tab.md) (« Tableau de signe / Tableau de variation » annoncés « à venir »), [2026-06-10-Feat-ribbon-columns-settings-culture](2026-06-10-Feat-ribbon-columns-settings-culture.md) (`ColumnLayoutInserter`, même mécanique `Tables.Add`)

## Citation acté

> « vas y » — utilisateur, 2026-10-16, en réponse à la proposition : bouton
> « Tableau » avec deux entrées (Signe, Variation), tableau Word préformaté,
> flèches en caractères ↗ ↘ plutôt que formes dessinées.

## Contexte

Un testeur demande « est-ce possible de créer des tableaux de variation avec des
flèches ». Au lycée, tableaux de signe et de variation reviennent à chaque
chapitre d'analyse ; les construire à la main dans Word (fusion de cellules,
bordures, flèches) est exactement la friction que MathCursor veut supprimer.
Les libellés existaient depuis l'ADR 2026-05-11 (`Strings.ConstructionVariationTableLabel`)
sans aucune implémentation.

## Décision

### Un menu « Tableau » dans le groupe Mise en page du ruban

Deux entrées : **Tableau de signe** et **Tableau de variation**. Même règle de
position que les colonnes (`ColumnLayoutInserter`) : ¶ vide → le tableau prend
sa place ; sinon il est inséré sous le ¶ courant. La sélection éventuelle n'est
pas absorbée dans le tableau.

### Gabarit = un vrai tableau Word, pas une équation

- Une colonne d'étiquettes (`x`, `f′(x)`, `f(x)`) + **5 colonnes** qui alternent
  *valeur / intervalle / valeur / intervalle / valeur*. Les valeurs de `x` sont
  dans les colonnes impaires (`−∞`, vide à remplir, `+∞`).
- Bordures : cadre extérieur + séparateurs horizontaux entre lignes + trait
  vertical après la colonne d'étiquettes. Pas de trait vertical entre les
  colonnes de valeurs (rendu « manuel scolaire »).
- Le gabarit est pré-rempli d'un **exemple cohérent** (`+ 0 −`, maximum au
  milieu) que l'élève modifie : il montre d'emblée où vont les signes, les
  valeurs et les flèches.
- **Tableau de signe** : lignes `x` et `f(x)`.
- **Tableau de variation** : lignes `x`, `f′(x)`, `f(x)`. La ligne `f(x)` est
  haute ; ses cellules de valeur sont alignées **en haut** (maximum) ou **en
  bas** (minimum), ses cellules d'intervalle portent une **flèche caractère**
  `↗` / `↘` centrée, en plus grand corps.
- L'élève ajoute lignes / colonnes avec les outils natifs de Word (clic droit >
  Insérer) : la mise en forme suit.

### Contenu des cellules en texte simple

Les étiquettes et valeurs (`x`, `−∞`, `+∞`) sont du **texte** (italique pour les
variables), pas des OMath : aucun passage par le pipeline d'insertion OMath / CC
(ordre ZWSP → math → BuildUp → CC). L'élève convertit ce qu'il tape dans les
cellules avec MathCursor comme partout ailleurs.

### Code

- Gabarit **pur** (`Host/StudyTableTemplate.cs`, zéro interop) : grille de
  cellules (texte, italique, alignement vertical, flèche) — testé en xUnit.
- Insertion Word (`Host/StudyTableInserter.cs`) : applique le gabarit via
  `Tables.Add`, bordures, largeurs, hauteur de ligne, alignements.

### Ajout du 2026-10-19 : un seul bouton, 0 par défaut

> « j'aurai bien changé le ruban pour mettre un gros bouton tableau de
> signe/variation (on fait les deux en fait !) et par defaut une valeur de x à 0
> (la y'a rien) » — utilisateur.

Le menu à deux entrées devient **un seul gros bouton « Tableau de signe /
variation »** ; les lignes de signe ou de variation s'ajoutent ou se retirent
ensuite depuis les étiquettes (ADR 2026-10-19-Feat-study-table-cell-roles-contextual-popup).
La valeur de x du milieu vaut **`0`** par défaut et arrive sélectionnée (taper
la remplace).

Puis : « par defaut met une seule ligne (tableau de signe) avec un f(x) en mode
equation deja » — le bouton insère finalement le gabarit **signe** : ligne x +
**une seule ligne de signe**, dont l'étiquette `f(x)` est **une équation** (texte
posé puis `OMaths.Add` sur la plage non vide + `BuildUp`, display centrée,
police math du réglage ; hors pipeline de conversion, sans entrée de source —
le mode édition est coupé sur les étiquettes). Les étiquettes du gabarit
variation (`f′(x)`, `f(x)`) sont aussi des équations.

## Tradeoff & alternatives écartées

- **Flèches dessinées (formes Word ancrées)** : flottent au-dessus du texte, se
  décalent dès qu'on ajoute une ligne avant, redimensionne une colonne ou change
  une valeur. Fragile pour une prise de notes rapide ; les flèches caractère
  suivent l'édition, le copier-coller, l'impression et l'export PDF.
- **Tableau en OMath (`\begin{array}`)** : non éditable cellule par cellule de
  façon naturelle, pas d'alignement haut/bas par cellule, pas d'ajout de colonne
  natif.
- **Dialogue « nombre de valeurs / de facteurs »** avant insertion : friction en
  plus ; Word sait déjà ajouter des colonnes et lignes.
- **Cellules en OMath pré-converties** : impose le pipeline OMath/CC dans des
  cellules de tableau (non validé par POC) pour un gain cosmétique.

## Conséquences

- **Code touché** : `Ribbon.xml` (menu `StudyTableMenu`), `RibbonCallback.cs`
  (labels + `OnInsertStudyTableClicked`), `Strings.cs` (libellés existants
  réutilisés + screentip + message d'échec), `Host/StudyTableTemplate.cs`,
  `Host/StudyTableInserter.cs`, `ColumnLayoutInserter.cs` (position d'insertion
  partagée), les deux csproj (VSTO + tests pour le gabarit pur).
- **Tests** : `StudyTableTemplateTests` (forme de la grille, colonnes alternées,
  flèches et alignements de la ligne `f(x)`).
- **API publique** : aucune. Moteur et sérialisation non touchés.

## Validation post-fix

Observation dans Word : insertion des deux tableaux (¶ vide et ¶ non vide),
ajout d'une colonne et d'une ligne par Word, conversion MathCursor (popup,
Ctrl+Espace) dans une cellule, export PDF.
