# Brief — Fusionner OMath adjacents lors d'une conversion

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-29
**Branche :** `lattice-engine`
**Public cible :** agent C#/VSTO autonome qui ne connaît pas le projet.

---

## 1. Le besoin

Aujourd'hui, quand un élève tape une formule à côté d'un OMath existant
puis convertit (Ctrl+Espace), MathCursor crée un **second OMath séparé**.
Visuellement les deux blocs sont collés, mais Word les traite comme deux
équations distinctes — pas de copier-coller de l'ensemble propre, pas
d'édition fluide.

Exemple concret :

```
[OMath: f(x) = 2x + 1]   ← formule existante (insérée précédemment)
                       _ ← caret ici
                       + 3   ← élève tape ça puis Ctrl+Espace
```

Aujourd'hui :
- `[OMath: f(x) = 2x + 1]` (intact) + `[OMath: + 3]` (nouvelle)
- 2 OMath séparés côte à côte

Voulu :
- `[OMath: f(x) = 2x + 1 + 3]` (un seul OMath fusionné)

Pareil si un espace ou plusieurs séparent les deux :
```
[OMath: f(x) = 2x + 1] + 3   ← élève a tapé un espace et "+ 3"
```
→ doit fusionner en `[OMath: f(x) = 2x + 1 + 3]`.

## 2. Périmètre — décisions à acter

### 2.1. Direction de fusion
Au moment de la conversion :
- **OMath à gauche** (avant la zone à convertir) → fusionner avec
- **OMath à droite** (après la zone à convertir) → **également** fusionner avec
- **Les deux** côtés → fusionner les trois en un seul

### 2.2. Séparateurs tolérés entre OMath et nouvelle zone
- Aucun séparateur (collés directement) → fusion
- 1 ou plusieurs **espaces** → fusion (les espaces deviennent un espace
  unique dans le source mergé)
- **Tab** → fusion (tab → espace dans le source mergé)
- **Saut de ligne / nouveau paragraphe** → **PAS de fusion** (séparation
  intentionnelle)
- Tout autre caractère (lettre, ponctuation) entre → **PAS de fusion**

### 2.3. OMath non-MathCursor
Un OMath présent dans le document mais sans `EquationHandle` connue de
`IEquationStore` (= équation tapée à la main par l'utilisateur ou
collée depuis ailleurs) :
- **PAS fusionné** automatiquement. On ne sait pas reconstruire son source
  texte d'origine, donc on ne peut pas merger proprement.
- Comportement de fallback : le nouveau OMath est créé séparément, comme
  aujourd'hui.

## 3. Architecture

### 3.1. Détection des OMath adjacents
Fichier : `adapter-vsto/src/MathCursor/Host/SuggestionService.cs`.

La méthode qui pilote la conversion (probablement `ApplyConversion()` ou
similaire) doit, **avant d'insérer le nouvel OMath** :

1. Récupérer le `Range` de la zone à convertir.
2. Étendre ce range vers la gauche tant que les caractères sont des
   espaces/tabs simples → on attrape l'OMath qui touche le côté gauche.
3. Étendre vers la droite pareil → OMath côté droit.
4. Pour chaque OMath détecté adjacent :
   - Tester si une `EquationHandle` existe dans `IEquationStore`
     (chercher par range overlap ou via le content control wrapper si
     c'est la convention adoptée pour `MathNerDetector`).
   - Si oui : récupérer le `source` via `IEquationStore.RetrieveAsync`.
   - Si non : ne pas merger ce côté (fallback).

### 3.2. Construction du source mergé
Pour chaque côté qui peut être mergé, concaténer dans l'ordre :

```
mergedSource = leftSource + " " + middleSource + " " + rightSource
```

Avec :
- `leftSource` : le source de l'OMath à gauche (ou vide si pas mergeable)
- `middleSource` : la nouvelle zone que l'élève vient de taper
- `rightSource` : le source de l'OMath à droite (ou vide)
- Les espaces de jointure : un espace simple entre chaque morceau,
  indépendamment du nombre d'espaces tapés. Le LatticeEngine s'occupe
  ensuite du rendu.

### 3.3. Remplacement atomique dans Word
Le range remplacé doit couvrir **tout le bloc** : OMath gauche + espaces
+ middle + espaces + OMath droite.

1. Calculer le range `expandedRange` qui englobe tout.
2. Convertir `mergedSource` via le LatticeEngine → OMML.
3. Effacer `expandedRange`.
4. Insérer le nouvel OMML à la place.
5. Côté `IEquationStore` :
   - `RemoveAsync` les `EquationHandle` des OMath fusionnés (ils
     n'existent plus en tant que blocs séparés)
   - `StoreAsync` la nouvelle `EquationHandle` avec le source mergé

### 3.4. Curseur après l'opération
Positionner le caret en fin de l'OMath fusionné (ou juste après, dans le
flux texte). Comportement à valider en test manuel — l'objectif est que
l'élève puisse continuer à taper sans surprise.

## 4. Livrables

1. **Détection adjacence** dans `SuggestionService.cs` :
   - Méthode `FindAdjacentMergeableOMath(Range zone, Direction direction)`
     → renvoie l'`OMath` + son `EquationHandle` si mergeable, null sinon.
2. **Logique de fusion** :
   - Méthode `MergeOMathsAndConvert(Range zone, OMath left, OMath right, string newSource)`
     qui orchestre le replacement atomique.
3. **Mise à jour de l'`IEquationStore`** :
   - Suppression des handles des OMath fusionnés
   - Création de la nouvelle handle avec source mergé
   - Pas de fuite de handles orphelines dans `Document.CustomXMLParts`
4. **Tests d'intégration** :
   - Manuel dans Word (cf. §5)
   - Si tests automatisés possibles côté adapter-vsto (mock Word ?), ajouter.
5. **ADR** : `docs/dev/decisions/2026-04-XX-Feat-merge-adjacent-omaths.md`
   - Kind = Feat, Température = molle, Statut = acté
   - Citation utilisateur = ce brief

## 5. Cas de test obligatoires (manuels dans Word)

### 5.1. Fusion à gauche (OMath existant à gauche)

**Setup** : taper `f(x) = 2x + 1`, Ctrl+Espace → OMath créé.
**Action** : taper directement après ` + 3` (avec un espace), Ctrl+Espace.
**Attendu** : un seul OMath `f(x) = 2x + 1 + 3`. La handle de l'ancien
OMath est supprimée du store, une nouvelle handle créée.

### 5.2. Fusion à droite

**Setup** : taper `+ 3`, Ctrl+Espace → OMath créé.
**Action** : positionner le caret juste avant cet OMath, taper
`f(x) = 2x + 1` puis Ctrl+Espace.
**Attendu** : un seul OMath `f(x) = 2x + 1 + 3`.

### 5.3. Fusion sandwich (gauche + droite)

**Setup** : taper `f(x) =`, Ctrl+Espace → OMath A. Aller en fin de
paragraphe, taper `+ 1`, Ctrl+Espace → OMath B. Maintenant le caret est
au milieu, entre A et B.
**Action** : taper `2x` au milieu, Ctrl+Espace.
**Attendu** : un seul OMath `f(x) = 2x + 1` qui fusionne les 3 morceaux.

### 5.4. Pas de fusion à travers un saut de ligne

**Setup** : OMath `f(x) = 2x + 1` puis Entrée, puis taper `+ 3` sur la
nouvelle ligne.
**Action** : Ctrl+Espace sur `+ 3`.
**Attendu** : 2 OMaths séparés (un par paragraphe). Pas de fusion.

### 5.5. Pas de fusion avec OMath non-MathCursor

**Setup** : insérer une équation Word native (Insertion → Équation) ou
coller un OMath d'un autre document. Cet OMath n'a pas de handle dans le
store.
**Action** : taper `+ 3` après cet OMath, Ctrl+Espace.
**Attendu** : nouveau OMath séparé pour `+ 3`. L'OMath natif reste intact,
pas tenté de merger.

### 5.6. Espaces multiples / tab

**Setup** : OMath `f(x) = 2x + 1`. Taper trois espaces puis `+ 3`.
**Action** : Ctrl+Espace.
**Attendu** : fusion en un OMath `f(x) = 2x + 1 + 3` (espaces collapsés en
un seul dans le source mergé).

### 5.7. Pas de fusion si caractère intermédiaire

**Setup** : OMath `f(x) = 2x + 1`. Taper ` donc + 3`.
**Action** : Ctrl+Espace sur `+ 3`.
**Attendu** : OMath `f(x) = 2x + 1` intact, nouveau OMath `+ 3`. Le mot
"donc" entre les deux empêche la fusion.

### 5.8. Anti-régression Ctrl+Z (undo)

**Setup** : faire un sandwich §5.3.
**Action** : Ctrl+Z.
**Attendu** : Word annule l'opération, on revient à l'état pré-fusion
(les 2 OMaths séparés). Word natif gère l'undo si on a fait l'opération
en un seul `Range.Text` change ou via une `UndoRecord`.

## 6. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` | Pilotage conversion (à modifier) |
| `adapter-vsto/src/MathCursor/Host/VstoEquationStore.cs` | Persistance par handle (Store/Remove à appeler proprement) |
| `host-contract-csharp/src/MathCursor.HostContract/IEquationStore.cs` | Interface du store |
| `core-csharp/src/MathCursor.Core/Lattice/LatticeEngine.cs` (façade) | API de conversion source → OMML |
| `docs/dev/briefs/2026-04-27-edit-mode-revert-to-source.md` | Brief mode édition (mécanisme `EquationHandle ↔ OMath` déjà discuté là-bas, à réutiliser) |
| `docs/dev/decisions/2026-04-23-Feat-trigger-ctrl-space.md` | ADR Ctrl+Espace |

## 7. Ce qu'il NE faut PAS faire

- ❌ Fusionner agressivement à travers des sauts de ligne ou des
  caractères non-blancs. La règle = espaces/tabs uniquement entre les
  OMaths et la zone tapée.
- ❌ Tenter de merger un OMath non-MathCursor (sans handle dans le store).
  On ne peut pas reconstruire son source, donc on ne peut pas garantir un
  rendu correct après merge. Fallback = OMath séparés.
- ❌ Laisser des handles orphelines dans `Document.CustomXMLParts`. Bien
  appeler `RemoveAsync` sur chaque handle des OMaths fusionnés.
- ❌ Casser le mode édition (brief
  [`2026-04-27-edit-mode-revert-to-source.md`](2026-04-27-edit-mode-revert-to-source.md)).
  Le mode revert lit le source via la handle — donc après merge, taper
  Ctrl+E (ou équivalent) sur l'OMath fusionné doit donner accès au source
  fusionné, pas aux sources individuels.
- ❌ Ajouter un dialog de confirmation "voulez-vous fusionner ?". Le
  comportement attendu est silencieux : si fusion possible → on fusionne,
  sinon → comportement actuel.
- ❌ Toucher au LatticeEngine ou au core-csharp. Tout se passe dans
  `adapter-vsto/`.

## 8. Validation

1. `dotnet build MathCursor.sln` → 0 erreur.
2. `dotnet test adapter-vsto/tests/` → tests passent, plus les nouveaux si
   on en ajoute.
3. Test manuel des 8 cas du §5 dans Word.
4. Vérifier `Document.CustomXMLParts` après plusieurs fusions : pas de
   handle orpheline (chaque entry doit correspondre à un OMath visible
   dans le document).
5. Test manuel : Ctrl+E (mode édition revert) sur un OMath fusionné →
   le source affiché est bien le source mergé, pas un des morceaux.
6. ADR créé.

## 9. Estimation

| Tâche | Durée |
|-------|-------|
| Lecture `SuggestionService.cs` + compréhension flux conversion actuel | 1 h |
| Détection adjacence (gauche/droite + handle lookup) | 2-3 h |
| Logique de fusion + replacement atomique du range | 2-3 h |
| Maj store (remove anciennes, add nouvelle) | 1 h |
| Tests manuels + debug | 2-3 h |
| ADR + commit propre | 30 min |
| **Total estimé** | **~1-1.5 jour** |

## 10. Interaction avec d'autres briefs

- Ce brief assume que la convention OMath ↔ EquationHandle est posée
  (probablement via content control wrapper, à voir avec le brief
  `2026-04-27-edit-mode-revert-to-source.md` qui touche le même mapping).
- Si le mapping n'est pas encore en place côté code, prioriser ce brief
  ou l'autre dépendamment de l'ordre d'implémentation.
- Compatible avec brief
  `2026-04-29-implication-equivalence-arrows.md` (orthogonal — concerne
  le moteur core, pas le adapter).
