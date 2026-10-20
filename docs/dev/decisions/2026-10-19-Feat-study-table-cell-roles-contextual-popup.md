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
| Valeur de `x` | **aucune popup** (2026-10-20, voir « Ligne x sans popup ») |
| Frontière (ligne de signe ou de variation) | `0` · `‖` (valeur interdite) |
| Intervalle de variation | `↗` · `↘` · `→` |
| Intervalle de signe | aucune popup |

Règles : la popup **s'efface à la première touche** (on écrit directement) et
**cède toujours la place** aux popups de conversion et d'édition de MathCursor.
**Exception (retour d'usage, 2026-10-19)** : dans une **étiquette de ligne**, une
formule convertie occupe toute la case, le clic ouvrait la popup d'édition et
les actions de ligne devenaient inaccessibles. Choix utilisateur (« Actions de
ligne seulement ») : sur une formule convertie d'étiquette, pas de mode édition,
la popup de ligne s'affiche ; pour corriger la formule, on la retape. Le texte
pas encore converti garde la popup de conversion. Ailleurs (valeurs,
extremums, reste du document), mode édition inchangé.
Les nouvelles lignes s'insèrent sous la ligne cliquée ; une valeur de `x`
ajoute une **paire** intervalle + frontière dans toutes les lignes, chaque case
mise en forme selon le type de sa ligne.

### Rôles portés par des styles de paragraphe

Chaque case porte son rôle par un style (« MathCursor Flèche » existe déjà) :
étiquette de signe, étiquette de variation, valeur de `x`, frontière,
intervalle, flèche. Les actions lisent ces rôles plutôt que de deviner à partir
des positions : robuste aux modifications de l'élève, voyage avec le
copier-coller.

### Les `0` posés sur une simple ligne (ajout du 2026-10-19)

> « et classiquement la valeur interdite avec double ligne c'est super, et du
> coup les 0 qui sont aussi des "separation" on le met avec une simple ligne de
> separation » puis, après les spikes, « ok c'est bien ! » — utilisateur.

Dès qu'une frontière contient un `0` (popup ou frappe), **toute la colonne sous
x** reçoit une simple ligne verticale centrée : les cases `0` = trois
paragraphes [bout de ligne / `0` centré / bout de ligne] (la ligne est
interrompue par le `0`), les cases vides = ligne pleine ; une case portant une
autre valeur (extremum d'une ligne de variation) garde sa valeur sans ligne. La
ligne disparaît avec le dernier `0` de la colonne ; la valeur interdite (double
barre) reste prioritaire. Bout de ligne = paragraphe vide à bordure gauche,
retrait gauche = milieu de la case, interligne exact = longueur du bout.

Spikes (Word COM, PDF relu) : **B écarté** (retrait de première ligne négatif
pour poser le `0` sur la bordure) : Word dessine la bordure au début du texte,
le `0` reste toujours à côté de la ligne. **A retenu** (trois paragraphes) : `0`
centré sur la ligne, hauteur des lignes quasi inchangée ; la ligne pleine dans
les autres cases de la colonne donne la ligne continue des manuels.

### Une valeur de x écrite pose aussi la séparation (ajout du 2026-10-19)

> « quand j'ecris quelque chose dans l'axe des x dans une case "vide" il
> faudrait que ca declenche une separation. tu vois le truc ? et inversement si
> j'efface ? » — utilisateur.

La simple ligne d'une colonne (« séparation ») est posée par un `0` sous x
**ou** par une valeur écrite dans sa case de la ligne x, et retirée quand les
deux disparaissent (détecté à la sortie de la case, comme l'ajustement de
police). Exception : pas de séparation sous la **première et la dernière**
valeur de x (`−∞`, `+∞` ou une borne) — la ligne doublerait le bord du tableau ;
un `0` à un bout garde la sienne. La valeur interdite reste prioritaire.

**Valeur écrite dans un intervalle de la ligne x** (retour d'usage : l'élève
écrit dans la case vide *entre* deux valeurs, que le tableau tenait pour un
intervalle) : à la sortie de la case, elle devient une **nouvelle valeur de x**
— un intervalle est inséré de chaque côté, la case devient frontière, et dans
les autres lignes le contenu de l'intervalle d'origine (signe, flèche) est
recopié dans les deux nouveaux intervalles, la nouvelle frontière restant vide.
La séparation suit par la règle ci-dessus.

### Formules centrées dans le tableau (ajout du 2026-10-19)

> « idealement dans le tableau (et uniquement ici) il faudrait que l'alignement
> des formules mathcursor soit "centré" » — utilisateur.

Une équation seule dans sa case (display) est alignée à **gauche** partout
(règle d'insertion inchangée), **sauf** dans une case d'un tableau MathCursor :
`OMathInserter.DecideOMathTyping` choisit alors `wdOMathJcCenter` (seule la
valeur change, pas l'ordre des opérations d'insertion). Chaque réapplication
du tableau recentre aussi les équations display déjà présentes (formules
converties avant ce changement). Les blocs (chaînes / systèmes) gardent leur
alignement à gauche.

### Extremums placés d'après les flèches

Dans une ligne de variation, une frontière est alignée **en haut** si elle est
entre ↗ et ↘ (maximum), **en bas** entre ↘ et ↗ (minimum), au milieu sinon ;
en bout de tableau, la seule flèche voisine décide (↗ en début : en bas).
Aucun choix à faire pour l'élève : l'alignement suit les flèches à chaque
réapplication. (Précision ajoutée à l'implémentation, 2026-10-19.)

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

### Ligne x sans popup (2026-10-20)

> « comme on a fait la separation naturelle quand on ajoute un x, la petite
> popup valeur a gauche valeur a droite etc est un peu de trop » puis « valeur
> interdite et supprimer la valeur aussi doivent sauter.. dans la ligne x pour
> moi toute la popup est en trop. on le fait ailleur » — utilisateur.

La ligne x n'a plus de popup. Les gestes passent par la saisie :

- **ajouter** une valeur : la taper entre deux valeurs (intervalle de la ligne x) ;
- **supprimer** une valeur (choix utilisateur « Effacer = supprimer ») : vider une
  valeur **intérieure** puis quitter la case → la frontière et l'intervalle de
  droite disparaissent, l'intervalle de gauche garde son contenu (signe,
  flèche) ; les **bornes** (première / dernière valeur) sont seulement vidées.
  Un Ctrl+Z annule ;
- **valeur interdite** : depuis les frontières du dessous (popup `0 · ‖`).

Conséquence : ajouter une valeur au-delà de la première ou de la dernière
(borne au-delà de `0` dans `[0 ; 5]`) n'a plus de geste — rare, `−∞` / `+∞`
couvrent la plupart des cas. Les actions `AddValueLeft/Right` et `DeleteValue`
restent dans `StudyTableActions` (harnais) sans entrée dans l'interface.

### Passe visuelle (2026-10-20)

> « header et colonne de gauche avec une legere background ? + legere graisse ?
> et la fleche du tableau de variation, y'a moyen de faire moins rough » —
> utilisateur ; choix sur rendu d'essai (PDF) : « Bleu-gris + gras + bleu nuit »,
> « Cambria Math 26 gris ».

- **En-tête (ligne x) et étiquettes** : fond bleu-gris `EEF2F8` (trame de
  cellule, posée aux réapplications) ; texte **gras bleu nuit** `1F3A5F` porté
  par les **styles** (étiquettes + deux styles dédiés à la ligne x,
  « MathCursor En-tête valeur / intervalle », relus comme frontière /
  intervalle ; l'ancien style de ligne x reste reconnu et migre à la
  réapplication). Ce qu'on tape hérite du style : aucune mise en forme directe
  à réécrire en sortie de case (sinon une entrée d'annulation par saisie,
  régression attrapée par le harnais). Les équations de l'en-tête passent en
  gras mathématique.
- **Flèches** : police **Cambria Math** (livrée avec Office) **26 pt gris
  foncé** `404040` dans le style « MathCursor Flèche », réimposée à chaque
  insertion (les tableaux existants en profitent). Écartés au rendu : police du
  texte (pointes grossières), Segoe UI Symbol (trop petit), flèches doubles
  `⇗` (chargées), Latin Modern / STIX (pas installées partout).

### Durcissement (2026-10-20)

Après revue des écueils (« ok faisons ca! » — utilisateur) :

- **Performance** : à la sortie d'une case modifiée, seule sa **colonne** est
  remise en forme (`ReapplyColumn` : police, élargissement, séparation,
  alignements, équations centrées) ; le reste du tableau n'est retouché que si
  la colonne s'élargit. À chaque clic, la case est lue par une **lecture légère**
  (`ReadCell` : quelques styles) au lieu de tout le tableau. L'élargissement saute
  directement d'après le débordement mesuré (3 lignes → ~3 fois plus large) au
  lieu de pas fixes de 3 %. Mesures : ~150 ms par sortie de case dans Word
  (journal), ~400 ms piloté hors processus.
- **Ctrl+Z** : une action de structure (dont la valeur tapée entre deux valeurs)
  s'annule en **un** Ctrl+Z, styles compris ; une sortie de case sans changement
  de mise en forme ne crée **pas** d'entrée d'annulation vide (vérifiés par le
  harnais). Limite connue : si la sortie de case ajuste la mise en forme (police,
  séparation), le premier Ctrl+Z annule cet ajustement, le second la saisie.
- **Case « 0 sur la ligne »** : un curseur posé dans un bout de ligne (clic
  au-dessus ou en dessous du 0) est ramené juste après le 0.
- **Non-régression** : `scripts/harness-study-table.ps1` exécute le
  `MathCursor.dll` compilé dans Word (COM, réflexion sur les types internes) et
  vérifie la structure après chaque action (32 vérifications, code de sortie 1
  en cas d'échec). Il demande Word installé : hors du gate `run-tests.ps1`, à
  lancer avant une livraison touchant aux tableaux.

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

- **Code touché** : `StudyTableTemplate` (modèle pur : rôles, choix, largeurs,
  plans de colonnes, extremums), `StudyTableFormatter` (styles, lecture de
  structure, réapplication, valeur interdite, ajustement de police),
  `StudyTableActions` (actions de structure), `StudyTableInserter` (passe par
  les rôles), `ArrowCellController` → `StudyTableController`,
  `ArrowPalettePopupWindow` → `TableChoicePopupWindow` (symboles ou libellés),
  `ConversionController` / `EditModeController` (événement `PopupShown`),
  `ThisAddIn`, `Strings` (libellés des actions).
- **Tests** : `StudyTableTemplateTests` (gabarit, rôles, choix, largeurs,
  plans de colonnes, extremums). Le code Word est vérifié par un harnais qui
  exécute le `MathCursor.dll` compilé dans Word (insertion, ajout / suppression
  de valeurs et de lignes, flèche, valeur interdite, ajustement de police,
  export PDF relu).
- **Documents** : sept styles « MathCursor … » dans la liste des styles.
