# Feat — Duo Convertir/Colonnes dans TabHome + onglet "MathCursor" dédié pour le reste

**Date :** 2026-05-11
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** étend `2026-05-06-Feat-ribbon-pane-examples-pivot`
**Lié à :** `Ribbon.xml`, `RibbonCallback.cs`, `Strings.cs`

## Citation acté

> "on peut rajouter le bouton pour faire les colonnes dans l'accueil ?
> et le remettre avec tout le reste dans le tab ruban specifique avec
> la galerie constructions et signaler un bug. sur l'accueil y'a ptetre
> la place pour les colonnes et convertir."
> — utilisateur, 2026-05-11
>
> "A tout à droite. 4 max ca suffit"

## Décision

Deux points d'entrée ruban :

### 1. TabHome (Accueil Word) — duo immédiat
Groupe "MathCursor" placé tout à droite de TabHome, avec **2 boutons
seulement** :
- **Convertir** (déclenche la conversion sur la zone autour du caret,
  équivalent Ctrl+Espace)
- **Colonnes** (split-button avec dropdown 1/2/3/4) → insère un tableau
  1×N au curseur, **toutes bordures off sauf la barre verticale**
  interne entre cellules (= séparatrices visuelles sans cadre).

Rationale : c'est l'usage du PAP au quotidien. Le reste = noise.

### 2. Onglet dédié "MathCursor" — galerie complète
Onglet visible en permanence (pas contextuel), 4 sous-groupes :

- **Saisie** : Convertir (gros), Cheatsheet (placeholder bouton, pane
  paused par ADR pivot — réintégré quand galerie d'exemples livrée)
- **Mise en page** : Colonnes (même dropdown 1-4)
- **Constructions** (galerie roadmap, boutons **désactivés** avec
  tooltip "à venir v0.6+") :
  - Tableau de signe
  - Tableau de variation
  - Courbe / repère
  - Figure géométrique
- **Outils** : Paramètres (dialog langue / mult symbol / popup auto /
  logs), Signaler un bug (existant), Inspecteur (debug, gardé),
  À propos (existant)

## Pourquoi

- **PAP/lycéen = vitesse + simplicité.** L'usage quotidien est 2 boutons
  (Convertir + Colonnes). Mettre les 10 boutons en TabHome sature.
- **Prof beta-testeur = découverte produit.** L'onglet dédié signale
  l'ambition produit (galerie Constructions grisée motivante) et
  regroupe les outils d'admin (Paramètres, Signaler bug, Inspector).
- **Continuité du pivot 06-05.** L'onglet dédié n'est pas un retour en
  arrière sur la pivot "intégration TabHome" : il EN PLUS de TabHome,
  pas À LA PLACE.
- **Colonnes via tableau** : zéro perte de contrôle Word natif (Tab
  navigue entre cellules, le contenu reste éditable). Word `Columns`
  natifs (View > Layout) imposent une césure de page, trop intrusif.

## Alternatives écartées

- **Tout en TabHome** : trop chargé, perd la lisibilité du groupe pour
  l'usage quotidien.
- **Tout en onglet dédié, rien en TabHome** : oblige le PAP à changer
  d'onglet en permanence pour Convertir/Colonnes, friction.
- **Galerie Constructions cachée derrière un menu "Plus…"** : moins de
  bruit mais cache la roadmap aux profs. On l'affiche grisée pour
  signaler la direction.
- **Colonnes via Word `Columns` natif (section break)** : impose une
  césure de page, casse le flow de saisie. Tableau 1×N respecte le
  flow.
- **Picker grid visuel pour Colonnes (style "Insérer tableau")** :
  reporté V2. V1 = menu déroulant 4 boutons texte, faisable
  immédiatement.

## Détails techniques

### Tableau colonnes — style "barres séparatrices"
- `Range.Tables.Add(rng, 1, N)` → tableau 1 ligne × N colonnes
- Pour chaque cellule :
  - `Borders[wdBorderTop].LineStyle = wdLineStyleNone`
  - `Borders[wdBorderBottom].LineStyle = wdLineStyleNone`
  - `Borders[wdBorderLeft].LineStyle = wdLineStyleNone` sur la 1ère
  - `Borders[wdBorderRight].LineStyle = wdLineStyleNone` sur la dernière
  - Internes (Left/Right entre cellules) : `wdLineStyleSingle`,
    fine épaisseur
- Largeur : `PreferredWidthType = wdPreferredWidthPercent`,
  `PreferredWidth = 100 / N`
- Curseur positionné dans la 1ère cellule après insertion

### Sous-menu Colonnes (V1)
- Ribbon `<menu>` avec 4 `<button>` ("1", "2", "3", "4" colonnes)
- Callback unique `OnInsertColumnsClicked(IRibbonControl control)`,
  l'`id` du bouton porte le N (`InsertColumns1Button` …
  `InsertColumns4Button`), parsed depuis `control.Id`

### Boutons "Constructions" (placeholder)
- `getEnabled` callback retournant `false`
- `getScreentip` : "À venir — v0.6+"
- Pas de `onAction`

### Paramètres (placeholder V1)
- Bouton qui ouvre une `MessageBox` "Paramètres à venir v0.6" pour
  l'instant. Vraie dialog dans un ADR séparé quand on en aura besoin.

## Plan d'exécution

1. ADR posée + index.
2. `Ribbon.xml` : ajouter Convertir + Colonnes dropdown dans TabHome,
   créer l'onglet dédié `MathCursor` avec ses 4 sous-groupes.
3. `Strings.cs` : ajouter les labels FR/EN.
4. `RibbonCallback.cs` :
   - `OnConvertClicked` → délègue à `Suggestions.TriggerManualConversion`
     (ou équivalent existant).
   - `OnInsertColumnsClicked` → nouvelle méthode dans une classe
     `ColumnLayout.cs` ou via `SuggestionService`.
   - `OnConstructionsXxxClicked` (4 stubs grisés).
   - `OnSettingsClicked` → MessageBox V1.
   - `OnAboutClicked` → ouvre `HelpDialogBody`.
5. `Host/ColumnLayoutInserter.cs` : logique pure VSTO pour le tableau
   avec bordures.
6. Test en VSTO : Convertir, Colonnes 1-2-3-4, navigation des cellules.

## Risques

- Onglet dédié = surface ruban supplémentaire (perçu chargé). Mitigé
  par l'option "Custom Office Add-ins > MathCursor" qu'on peut masquer.
- Boutons Constructions grisés peuvent frustrer si on prend trop de
  temps à les livrer. Réévaluer si on n'a rien sorti d'ici v0.6.
- Tableau colonnes pour multi-colonne : si l'utilisateur tape une
  équation longue dans une cellule, elle peut wrapper et casser la
  largeur. À voir à l'usage.
