# Feat — Tableau de signe / variation flexible : rôles de cases + popup contextuelle

**Date :** 2026-10-19
**Kind :** Feat
**Température :** molle (POC du 2026-10-19 concluant, voir « Résultats du POC »)
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-10-16-Feat-sign-variation-table-insert](2026-10-16-Feat-sign-variation-table-insert.md) (gabarit, colonnes alternées), [2026-10-19-UX-variation-arrow-palette](2026-10-19-UX-variation-arrow-palette.md) (palette ↗ ↘ →, généralisée ici)

## Citation acté

> « ok fais l'adr et lance le poc » — utilisateur, 2026-10-19, après une série
> de retours d'usage : « il faut vraiment arriver à etre super flex sur ce
> "tableau" pouvoir rajouter une ligne de tableau de signe / ou une ligne de
> variation […] pouvoir rajouter une colonne », « on peut meme mettre au clic ?
> plutot qu'au clic droit », « pour les etiquette de ligne ou les signes on peut
> laisser l'utilisateur ecrire + - », « quand on clique sur une etiquette de
> ligne ca vaudrait pas le coup ? », « les valeur de x on doit pouvoir mettre
> une formule classique math cursor », « j'aurai plutot reduis la police du
> texte si ca rentre pas ».

## Contexte

En jouant avec le gabarit du 2026-10-16, l'utilisateur constate qu'un tableau
figé ne suffit pas : on ajoute des valeurs de `x` et des lignes (facteurs,
dérivée, fonction) au fil de l'exercice. Les outils natifs de Word ne savent
pas le faire proprement :

- une valeur de `x` est une **frontière** (où tombent les `0`, doubles barres,
  extremums), les signes et flèches vivent **entre** deux frontières.
  L'alternance de colonnes étroites (frontière) / larges (intervalle) du
  gabarit rend ce décalage, mais « Insérer une colonne » de Word n'en ajoute
  qu'une et casse l'alternance ;
- une ligne de signe et une ligne de variation n'ont pas la même mise en forme
  (hauteur, alignement haut/bas des extremums, cases de flèche).

## Décision

### Toute case reste une case où l'on tape

Étiquettes (`f′(x)`, `x² − 1`…), valeurs de `x` (`√3/2`…), extremums
(`f(2)`…), signes `+` / `−` : texte ou formule MathCursor normale. La popup
n'est qu'un **raccourci pour ce qu'on ne peut pas taper** : la structure, la
double barre, les flèches.

### Popup au clic, selon le rôle de la case

Même famille que la palette de flèches (en ligne, ↓ / ← → / Entrée / Échap /
clic), affichée quand le curseur entre dans la case :

| Case | Choix |
|---|---|
| Étiquette de ligne | ＋ ligne de signe · ＋ ligne de variation · supprimer la ligne |
| Valeur de `x` | ＋ valeur à gauche · ＋ valeur à droite · valeur interdite · supprimer |
| Frontière (ligne de signe ou de variation) | `0` · `‖` (valeur interdite) |
| Intervalle de variation | `↗` · `↘` · `→` |
| Intervalle de signe | aucune popup |

Règles : la popup **s'efface à la première touche** (on écrit directement) et
**cède toujours la place** aux popups de conversion et d'édition de MathCursor.
Les nouvelles lignes s'insèrent sous la ligne cliquée ; une valeur de `x`
ajoute une **paire** intervalle + frontière dans toutes les lignes, chaque case
mise en forme selon le type de sa ligne.

### Rôles portés par des styles de paragraphe

Chaque case porte son rôle par un style (« MathCursor Flèche » existe déjà) :
étiquette de signe, étiquette de variation, valeur de `x`, frontière,
intervalle, flèche. Les actions lisent ces rôles plutôt que de deviner à partir
des positions : robuste aux modifications de l'élève, voyage avec le
copier-coller.

### Police réduite si une valeur ne tient pas

Les colonnes de frontière restent étroites (grille régulière). Si le contenu
d'une case de valeur passe sur deux lignes, sa taille baisse d'un cran jusqu'à
tenir, plancher **9 pt** (lisibilité, élèves avec PAP) ; au-delà, la colonne
s'élargit pour cette valeur. La taille remonte si la valeur raccourcit.
Déclenché à la sortie d'une case de valeur et après ajout d'une valeur de `x`
(events natifs, pas de polling).

### POC d'abord (règle dure Word API)

`scripts/poc-study-table-columns.ps1`, sans ruban ni pipeline :
1. insertion d'une paire de colonnes au milieu d'un tableau mis en forme :
   styles, bordures, largeurs, hauteur de la ligne de variation après coup ;
2. rendu de la valeur interdite (bordures de cellule vs caractère `‖`) ;
3. détection fiable du débordement d'une **équation** dans une cellule étroite
   et réduction de sa taille.

La température passe à **molle** quand le POC a tranché ces trois points.

### Résultats du POC (2026-10-19)

Word piloté en COM, rendu vérifié sur l'export PDF.

1. **Paire de colonnes / lignes : OK, à condition de tout réappliquer.**
   `Columns.Add(avant)` recopie le **style** de la colonne de référence mais
   pas l'alignement vertical, et Word **rétrécit toutes les colonnes
   proportionnellement** (largeurs toujours en %). `Rows.Add(avant)` recopie la
   ligne de référence (hauteur, styles, alignements), `Rows.Add()` la dernière.
   Après une action de structure, on **réapplique explicitement** rôle (style),
   alignement, bordures et largeurs : le résultat est propre. Aucune fusion de
   cellules n'est utilisée (elle bloquerait `Columns.Add`).
2. **Valeur interdite : bordure de paragraphe.** Écartés au rendu : bords
   gauche + droit de la case (deux traits espacés de la largeur de colonne =
   une boîte), caractère `‖` (petit, ne couvre pas la hauteur), bord double
   gauche (tombe sur le bord de colonne, pas sous la valeur centrée). Retenu :
   bordures **de paragraphe** gauche + droite dans la case frontière, retraits
   calculés pour resserrer les deux traits (~2,5 pt) au centre, interligne
   exact = hauteur de la ligne, marges haut/bas de cellule à 0 → double barre
   centrée sous la valeur de `x`, sur toute la hauteur (ligne haute comprise).
   Les retraits sont recalculés après chaque changement de largeur.
3. **Débordement : détectable, y compris pour une équation.** Comparer
   `Information(wdFirstCharacterLineNumber)` au début et à la fin du contenu de
   la case marche sans fenêtre visible, pour du texte comme pour une OMath
   (`√3/2` tient à 11 pt dans une frontière à 10 % ; `f(−1/2)=3/4+√5` passe de
   3 à 2 lignes à 9 pt). `Range.Font.Size` sur la case s'applique bien à
   l'OMath (relu à 9). Une valeur trop longue à 9 pt reste sur deux lignes :
   le repli « la colonne s'élargit » est nécessaire.

## Tradeoff & alternatives écartées

- **Menu au clic droit** (`ContextMenuTableCell`) : moins découvrable, deux
  gestes différents (clic pour les flèches, clic droit pour la structure), peu
  d'actions.
- **Modèle du tableau stocké à part (`CustomXMLParts`) + régénération** : le
  modèle et le contenu tapé par l'élève divergent ; la régénération perd sa
  mise en forme et ses formules.
- **Popup sur les étiquettes et intervalles de signe pour `+` / `−`** :
  trivial à taper, la popup gênerait la saisie de formules.
- **Colonnes de valeurs qui s'élargissent selon le contenu** : déforme la
  grille dès qu'une valeur est longue ; préféré : réduire la police.
- **« Ajuster le texte » de Word (`Cell.FitText`)** : écrase l'espacement des
  caractères au lieu de réduire la taille, rendu médiocre, comportement
  incertain sur les équations.
- **Boutons du ruban à sens contextuel (insérer un tableau / ajouter une
  ligne)** : moins prévisible qu'une popup sur l'étiquette.

## Conséquences

- **Code touché (prévu)** : `StudyTableTemplate` (rôles), `StudyTableInserter`
  (styles de rôle), `ArrowCellController` → contrôleur de popup du tableau
  (choix par rôle, actions de structure, priorité aux autres popups),
  `ArrowPalettePopupWindow` (libellés d'actions en plus des symboles),
  `ThisAddIn` (sortie de case → ajustement de police).
- **Documents** : quelques styles « MathCursor … » dans la liste des styles.
