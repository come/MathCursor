# Test Protocol — math-addon

Protocole manuel à rejouer dans Word (sideload) pour valider les 4 user stories.
Chaque scénario : action à taper → résultat attendu. Un échec = un bug à corriger.

Entre chaque scénario : `Ctrl+A` puis `Suppr` pour nettoyer la page.
Observer le panneau latéral (`lastAction`, `debugInfo`) pour confirmer les traces.

---

## US1 — Conversion TAB isolée du texte prose

### 1.a — Phrase courante FR
```
Taper : On a f(x)=1/x<TAB>
Attendu : "On a " reste tel quel, puis OMath "f(x)=1/x" (fraction)
```

### 1.b — Soit … FR
```
Taper : Soit g(x) = x^2 + 3<TAB>
Attendu : "Soit " intact, OMath "g(x) = x² + 3"
```

### 1.c — Expression pure (pas de prose)
```
Taper : f(x)=1/x<TAB>
Attendu : OMath "f(x)=1/x"
```

### 1.d — Stopwords multilingues
```
Taper : Let g(x) = 2x<TAB>
Attendu : "Let " intact, OMath "g(x) = 2x"
```

### 1.e — Pas de math
```
Taper : Bonjour tout le monde<TAB>
Attendu : aucune conversion, le tab reste (ou est ignoré)
```

---

## US2 — Ctrl+Z fonctionne

### 2.a — Undo simple
```
Taper : f(x)=1/x<TAB>  → OMath apparaît
Presser : Ctrl+Z
Attendu : retour au texte "f(x)=1/x" (avec ou sans le tab)
```

### 2.b — Re-TAB après undo
```
Taper : f(x)=1/x<TAB>  → OMath
Ctrl+Z  → texte restauré
Ajouter <TAB> manuellement (si pas déjà présent)
Attendu : RE-conversion en OMath. Aucun blocage "undo guard".
```

### 2.c — Undo puis édition puis re-TAB
```
Taper : f(x)=1/x<TAB>  → OMath
Ctrl+Z → "f(x)=1/x"
Corriger en "f(x)=2/x"<TAB>
Attendu : OMath "f(x)=2/x"
```

---

## US3 — Clic dans l'OMath → décomposition + curseur au bon endroit

### 3.a — Décomposition simple
```
Taper : f(x)=1/x<TAB>  → OMath
Cliquer au milieu de l'OMath (ex : sur le "x" du numérateur)
Attendu :
  - OMath remplacé par le texte source "f(x)=1/x"
  - Curseur positionné approximativement sur le "x" cliqué
  - PAS de perte du reste du paragraphe s'il y en avait
```

### 3.b — Décomposition avec prose autour
```
Taper : On a f(x)=1/x<TAB>  → "On a " + OMath
Cliquer dans l'OMath
Attendu :
  - "On a " PRÉSERVÉ
  - OMath remplacé par "f(x)=1/x"
  - Curseur dans la zone f(x)=1/x
```

### 3.c — Pas de boucle undo
```
Après 3.b, presser Ctrl+Z
Attendu : l'OMath revient, PAS de re-décomposition immédiate tant que le curseur reste à la même position
```

---

## US4 — Plusieurs formules par ligne

### 4.a — Deux formules consécutives
```
Taper : f(x)=1/x<TAB>
Taper : <ESPACE> et <ESPACE>
Taper : g(x)=2x<TAB>
Attendu :
  - Deux OMath distincts dans le même paragraphe
  - Le premier OMath NON modifié par la 2e conversion
```

### 4.b — Clic dans la 1ère formule (multi-formule)
```
Depuis l'état 4.a (deux OMath sur une ligne)
Cliquer dans le 1er OMath
Attendu :
  - Seul le 1er OMath décompose en "f(x)=1/x"
  - Le 2e OMath reste intact
  - La prose "et" entre les deux reste intacte
```

### 4.c — Clic dans la 2e formule
```
Depuis l'état 4.a, cliquer dans le 2e OMath
Attendu :
  - Seul le 2e décompose en "g(x)=2x"
  - Le 1er reste intact
```

### 4.d — Trois formules + prose
```
Taper : Soit f(x)=1/x<TAB>, on a g(x)=2x<TAB>, donc h(x)=x^2<TAB>
Attendu : 3 OMath distincts, prose "Soit ", ", on a ", ", donc " préservée entre chaque
Cliquer dans le 2e → seul "g(x)=2x" décompose
```

---

## Traces à vérifier dans la task pane

Pour chaque scénario observer :
- `lastAction` : affiche la dernière conversion/décomposition
- `debugInfo` : affiche la zone détectée avec scores
- `replaceCount` : s'incrémente à chaque conversion

---

## Matrice état (à tenir à jour)

| US   | Description                                  | Statut | Bug connu |
|------|----------------------------------------------|--------|-----------|
| 1.a  | "On a f(x)=1/x" + TAB                        | ?      |           |
| 1.b  | "Soit g(x)=x^2+3" + TAB                      | ?      |           |
| 2.a  | Ctrl+Z simple                                | ?      |           |
| 2.b  | Re-TAB après undo                            | ?      | guard bloque (à fixer) |
| 3.a  | Clic OMath → source restauré                 | ?      | efface le paragraphe |
| 3.b  | Prose autour préservée                       | ?      | efface la prose |
| 4.a  | Deux OMath consécutifs                       | ?      |           |
| 4.b  | Clic 1er OMath                               | ?      | storage écrasé |
| 4.c  | Clic 2e OMath                                | ?      | storage écrasé |
