# Brief — Extension itérative de la zone via Ctrl+Espace répété

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-29
**Branche :** `lattice-engine`
**Public cible :** agent C#/VSTO autonome qui ne connaît pas le projet.

---

## 1. Le besoin

Aujourd'hui, Ctrl+Espace force l'ouverture de la popup sur la zone math
détectée par le NER (ou par la heuristique si pas détecté). C'est le
trigger explicite, fallback fiable quand la détection automatique rate.

**Mais** la zone détectée est parfois **trop courte** : le NER s'arrête
sur un caractère ambigu, ou la heuristique manque un mot-clé en début.
Aujourd'hui l'élève n'a aucun moyen d'élargir cette zone — il doit
fermer, déplacer son curseur, et re-tenter.

**Comportement voulu** : chaque appui sur Ctrl+Espace, **tant que la
popup est ouverte**, étend la zone d'un cran vers la gauche jusqu'au
prochain stop word. La popup re-render avec les nouvelles propositions
de conversion sur la zone élargie. Quand la popup se ferme (Esc, click
ailleurs, validation d'une suggestion), le mécanisme se reset : le
prochain Ctrl+Espace repart d'une détection neuve.

Cas d'usage typique :

```
"On a vu que f(x) = 2x + 1"
                       ^ caret ici, élève fait Ctrl+Espace
→ popup ouvre, zone détectée = "2x + 1"
→ élève voit que "f(x) = 2x + 1" serait mieux, fait Ctrl+Espace à nouveau
→ popup ré-ouvre avec zone étendue = "f(x) = 2x + 1"
→ élève accepte (Entrée) → conversion finale du span élargi
```

Ou si la détection rate complètement :

```
"Soit g de R dans R"
                  ^ caret, Ctrl+Espace → détection ∅, popup ouvre vide ou
                                          sur une mini-zone
→ Ctrl+Espace → étend de "R", puis "dans R", puis "R dans R", puis...
→ jusqu'à atteindre "g de R dans R" complet, l'élève accepte
```

## 2. UX — état machine

```
       Ctrl+Espace (1er appui)
            │
            ▼
   ┌────────────────────┐    Ctrl+Espace
   │ Popup ouverte      │◄─────────────┐
   │ zone = N (initiale)│              │
   └────────┬───────────┘              │
            │                           │
            │ Ctrl+Espace tant que pas  │
            │ atteint début paragraphe  │
            ▼                           │
   ┌────────────────────┐               │
   │ zone = N+1 (étend  ├───────────────┘
   │ jusqu'au stop word)│
   └────────┬───────────┘
            │
            │ Esc / click ailleurs / Entrée (validation)
            ▼
   ┌────────────────────┐
   │ Popup fermée       │
   │ état reset         │
   └────────────────────┘
```

### Règles
- **Tant que la popup est ouverte**, chaque Ctrl+Espace étend.
- **Dès que la popup ferme** (Esc, validation, focus perdu, click
  ailleurs, déplacement curseur, frappe quelconque), l'état d'extension
  se reset à 0.
- L'extension est **uniquement vers la gauche** dans ce brief. Vers la
  droite peut être un v2 si le besoin se confirme.
- Si la zone atteint le début du paragraphe, ne plus étendre — laisser
  la popup affichée sur la zone max. Pas de "ding" ni de message.

### Indication visuelle
La zone considérée doit être visible quelque part — idéalement le texte
sélectionné dans Word (Word `Selection.Range` étendu chaque cran). Si
ça pose problème UX, alternative : la popup affiche le texte source en
haut (déjà le cas probablement).

## 3. Architecture

### 3.1. État côté SuggestionService
Fichier : `adapter-vsto/src/MathCursor/Host/SuggestionService.cs`.

Ajouter un état session :
```csharp
private int _expansionLevel;     // 0 = zone initiale, 1+ = étendue
private Range _initialZone;      // zone détectée au 1er Ctrl+Espace
private Range _currentZone;      // zone actuellement affichée dans la popup
```

Ces champs sont remis à zéro quand la popup ferme.

### 3.2. Hook Ctrl+Espace
Le handler actuel de Ctrl+Espace doit être modifié pour distinguer :

```csharp
void OnCtrlSpace()
{
    if (popup.IsOpen)
    {
        ExtendZoneOneMoreStop();   // §3.3
    }
    else
    {
        DetectAndShowPopup();      // comportement actuel, reset _expansionLevel = 0
    }
}
```

### 3.3. Logique d'extension
Méthode `ExtendZoneOneMoreStop()` :

1. Partir du début du `_currentZone`.
2. Scanner le texte du paragraphe vers la **gauche** caractère par
   caractère.
3. S'arrêter au prochain **stop word boundary** (cf. §3.4).
4. Étendre `_currentZone` jusqu'à ce point.
5. Si `_currentZone.Start == _initialZone.ParagraphStart` (déjà au
   début), ne rien faire — popup reste avec dernière zone valide.
6. Re-déclencher la conversion via le LatticeEngine sur la nouvelle zone.
7. Re-render la popup avec les nouvelles suggestions.
8. `_expansionLevel++`

### 3.4. Définition de "stop word"
À aligner avec ce qui existe déjà dans le projet. Pistes :

- **`data/`** : chercher un fichier `stopwords.json` ou similaire. La
  branche v3 du corpus NER avait des stopwords FR/EN multilingues
  documentés dans `CLAUDE.md` § "Données multilingues".
- **`core-csharp/src/MathCursor.Core/Lattice/Vocabulary.cs`** : peut
  contenir une liste de boundary tokens.

Définition fonctionnelle pour ce brief, si rien d'existant ne convient :
- **Stop word** = mot lexicalement reconnu comme non-math (préposition,
  conjonction, déterminant), définissant une frontière syntactique.
  Exemples FR : `que`, `et`, `ou`, `donc`, `mais`, `car`, `or`, `ni`,
  `si`, `quand`, `alors`, `puis`, `comme`, `sur`, `dans`, `avec`,
  `pour`, `sans`, `sous`, `depuis`, `entre`, `chez`, `vers`.
- **Stop char** = ponctuation forte : `.`, `,`, `;`, `:`, `?`, `!`.
- L'extension va jusqu'au stop word/char le plus proche à gauche
  (exclus, donc on ne l'inclut pas dans la zone).

À l'agent de :
1. Vérifier si une liste existe déjà dans le repo (probable).
2. La réutiliser.
3. Sinon, créer un fichier minimal `data/stopwords-fr.txt` +
   `data/stopwords-en.txt` (sans dépendre du tokenizer NER, juste un set
   de mots au boundary).

### 3.5. Reset de l'état
Quand la popup ferme (peu importe la cause), reset :
```csharp
_expansionLevel = 0;
_initialZone = null;
_currentZone = null;
```

Hooks où ça doit se déclencher :
- Validation d'une suggestion (Entrée, click sur une option)
- Esc / fermeture explicite
- Click ailleurs dans le document
- `WindowSelectionChange` (caret déplacé par l'utilisateur)
- Frappe d'un caractère hors Ctrl+Espace
- Fermeture du document

## 4. Livrables

1. **État session** dans `SuggestionService.cs` (`_expansionLevel`,
   `_initialZone`, `_currentZone`).
2. **Logique de détection / extension** :
   - `DetectAndShowPopup()` (refactor du handler Ctrl+Espace existant) —
     reset état + détection initiale.
   - `ExtendZoneOneMoreStop()` — extension d'un cran.
   - `FindPreviousStopWordBoundary(Range range)` — calcule le point
     d'arrêt suivant à gauche.
3. **Stop words** : utiliser l'existant si présent, sinon créer
   `data/stopwords-fr.txt` + `data/stopwords-en.txt` minimaux.
4. **Reset propre** sur fermeture popup / mouvement curseur / validation /
   frappe.
5. **Tests d'intégration** :
   - Manuel dans Word (cf. §5)
   - Si testable côté unit (mock Range), ajouter quelques tests sur
     `FindPreviousStopWordBoundary`.
6. **ADR** : `docs/dev/decisions/2026-04-XX-Feat-iterative-zone-expansion-ctrl-space.md`
   - Kind = Feat, Température = molle, Statut = acté
   - Citation utilisateur = ce brief

## 5. Cas de test obligatoires (manuels dans Word)

### 5.1. Extension simple
Texte : `On a vu que f(x) = 2x + 1`. Caret après le `1`.
- Ctrl+Espace → popup ouvre sur `2x + 1` (zone NER)
- Ctrl+Espace → popup étend à `f(x) = 2x + 1` (stop word `que`)
- Ctrl+Espace → popup étend à `vu que f(x) = 2x + 1`... non, "que" est
  inclus comme limite donc on s'arrête juste avant. Vérifier comportement
  exact selon §3.4.
- Esc → popup ferme, état reset.
- Ctrl+Espace → re-détecte zone initiale `2x + 1`.

### 5.2. Extension jusqu'à début paragraphe
Texte court : `f(x) = 2x + 1`. Caret en fin.
- Ctrl+Espace → popup sur `2x + 1`
- Ctrl+Espace → étend à `f(x) = 2x + 1`
- Ctrl+Espace → déjà au début, ne change pas (pas de bip ni d'erreur)
- Ctrl+Espace × N → idem, popup stable

### 5.3. Validation au milieu de l'extension
Texte : `Soit g de R dans R, alors g(x) = x^2`. Caret après `x^2`.
- Ctrl+Espace → popup sur `g(x) = x^2`
- Ctrl+Espace → étend à `alors g(x) = x^2`... attendu : non, `alors` est
  un stop word, donc l'extension s'arrête avant. Zone = `g(x) = x^2`
  (déjà la zone initiale). Cf. §3.4 — vérifier que `alors` est bien
  dans la liste.
- Si on retire `alors` de la zone : zone = `g(x) = x^2`, mais alors
  Ctrl+Espace n'a rien étendu. Comportement à valider : soit on
  affiche que la zone n'a pas changé, soit on saute au stop word
  d'avant (`,` puis `R` puis `dans R` etc.).

### 5.4. Reset par déplacement curseur
- Ctrl+Espace 1, popup ouvre.
- Ctrl+Espace 2, popup étend.
- Click ailleurs dans le doc.
- Popup ferme. État reset.
- Ctrl+Espace → re-détecte zone initiale (pas la zone étendue d'avant).

### 5.5. Reset par frappe
- Ctrl+Espace ouvre popup.
- Élève tape un caractère (ex: `+`).
- Popup se ferme (probablement déjà le cas), état reset.
- Ctrl+Espace → nouvelle détection.

### 5.6. Reset par validation
- Ctrl+Espace ouvre popup.
- Ctrl+Espace étend zone.
- Élève fait Entrée (accepte une suggestion).
- OMath inséré sur la zone étendue.
- État reset.
- Ctrl+Espace ailleurs → détection neuve.

## 6. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` | Pilotage popup + Ctrl+Espace (à modifier) |
| `adapter-vsto/src/MathCursor/UI/SuggestionPopupWindow.cs` | UI popup, événements de fermeture (à hooker pour le reset) |
| `core-csharp/src/MathCursor.Core/Lattice/Vocabulary.cs` | Possible source pour stop words |
| `data/` (à explorer) | Possibles stopwords*.json |
| `docs/dev/decisions/2026-04-23-Feat-trigger-ctrl-space.md` | ADR Ctrl+Espace original (contexte) |

## 7. Ce qu'il NE faut PAS faire

- ❌ Étendre vers la droite dans ce brief. Hors scope, à voir plus tard
  si le besoin est confirmé.
- ❌ Garder l'état d'extension entre deux ouvertures de popup. La règle
  est : fermeture popup = reset complet.
- ❌ Sauter plus d'un stop word par appui Ctrl+Espace. C'est un cran à
  la fois — l'élève contrôle la granularité.
- ❌ Étendre au-delà du paragraphe. Le saut de ligne est une frontière
  dure.
- ❌ Bipper / afficher un message d'erreur quand on a atteint le début
  du paragraphe. Comportement silencieux.
- ❌ Hardcoder une liste de stop words sans regarder s'il en existe déjà
  dans `data/` (DRY).
- ❌ Toucher à la logique de détection NER ou au LatticeEngine. Tout se
  passe côté adapter-vsto.

## 8. Validation

1. `dotnet build MathCursor.sln` → 0 erreur.
2. `dotnet test adapter-vsto/tests/` → tests passent.
3. Test manuel des 6 cas du §5 dans Word.
4. Vérifier dans les logs `%APPDATA%\MathCursor\logs\mathcursor.log` que
   chaque Ctrl+Espace étend logiquement (préfixe `expand` ou similaire,
   à ajouter pour debug).
5. ADR créé.

## 9. Estimation

| Tâche | Durée |
|-------|-------|
| Lecture `SuggestionService.cs` + flux Ctrl+Espace actuel | 1 h |
| Détection / réutilisation stopwords list | 0.5-1 h |
| Logique extension `FindPreviousStopWordBoundary` + reset hooks | 2-3 h |
| Refactor du handler Ctrl+Espace pour distinguer 1er appui vs suivants | 1-2 h |
| Tests manuels + debug | 2 h |
| ADR + commit | 30 min |
| **Total estimé** | **~1 jour** |

## 10. Interaction avec d'autres briefs

- Compatible avec
  [`2026-04-29-merge-adjacent-omaths.md`](2026-04-29-merge-adjacent-omaths.md)
  : la fusion d'OMath adjacents s'applique sur la zone validée, peu
  importe qu'elle vienne d'une détection initiale ou d'une extension
  itérative.
- Compatible avec
  [`2026-04-29-implication-equivalence-arrows.md`](2026-04-29-implication-equivalence-arrows.md)
  : les nouveaux opérateurs `=>` / `<=>` sont juste du contenu, ne
  changent pas le mécanisme de zone.
- Le brief mode édition
  [`2026-04-27-edit-mode-revert-to-source.md`](2026-04-27-edit-mode-revert-to-source.md)
  est aussi compatible : Ctrl+E sur un OMath revient au source, pas
  d'interaction avec l'extension itérative.
