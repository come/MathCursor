# Brief — Mode liste multi-ligne (style bullet list Word)

**Date :** 2026-05-05
**Statut :** rédigé, en attente d'ADR

## Contexte

Aujourd'hui, après avoir créé un multi-ligne via cross-merge (ex.
`X+1=2` puis `<=> 2x=4`), l'utilisateur qui veut continuer la chaîne
doit re-taper le marker sur chaque ligne :

```
X+1=2 ⏎  →  OMath multi-ligne
<=> 2x=4 ⏎  →  cross-merge dans le bloc existant
<=> x=2 ⏎  →  cross-merge à nouveau
```

Friction : retaper `<=>` à chaque ligne. Inspiration ergonomique : les
listes à puces Word, où Enter répète automatiquement la puce.

## Solution

Ajouter une **machine d'état list-mode** qui, après un cross-merge réussi
sur un marker donné, mémorise ce marker et le **pré-injecte** quand
l'utilisateur tape Enter sur une nouvelle ligne vide juste en dessous.
Double-Enter (= ligne vide ou marker-only au moment du Enter) **sort du
mode** et laisse passer le `\r` normal.

### États

- **Inactive** : default, Enter passe normalement
- **Active(marker)** : list-mode actif avec un marker mémorisé

### Transitions

```
Inactive --[cross-merge succeeded(marker)]--> Active(marker)
Active(marker) --[Enter on line with content]--> stay Active (cross-merge fires for current line)
Active(marker) --[Enter on empty/marker-only line]--> Inactive (passthrough Enter)
Active(marker) --[caret leaves the zone]--> Inactive
Active(marker) --[user clicks elsewhere / undo]--> Inactive
```

### Comportement Enter en mode actif

```
function onEnter(currentLineText, marker):
    trimmed = currentLineText.TrimStart()
    if trimmed == "" or trimmed == marker.Trim():
        # Ligne vide ou juste le marker → exit
        return ExitListMode  (= remove auto-inserted marker if any, then passthrough \r)
    else:
        # Ligne a du contenu réel → cross-merge dans le bloc, puis re-active
        return ValidateLineThenInsertMarker
```

### Trigger d'activation : moments précis

Active list-mode UNIQUEMENT après un cross-merge **multi-ligne réussi**
qui crée ou étend un bloc. Pas après un single-eq classique. Marker =
celui détecté par la cascade Mode 1 / Mode 2 (le préfixe en col1 du
MultiLineBlock, ou le `=` solo).

### Comportement détaillé Phase 1

Phase 1 = markers align* (`<=>`, `=>`, `<=`, `=`) :

```
État doc après cross-merge : 
    [OMath multi-ligne avec markers ↓]
    [(¶ vide créé par AppendEmptyParagraphAfterOMath, caret ici)]

User scenario A : Enter immédiatement (sans rien taper)
    → currentLine = "" (vide)
    → ExitListMode → passthrough Enter (= un nouveau ¶ vide en dessous)
    → state: Inactive

User scenario B : User tape "X = 4" puis Enter
    → currentLine = "X = 4"  (note: PAS de marker encore — voir scenario A bis)
    → Hmm — mais alors comment cross-merger ? Il faut le marker.

… c'est là que l'ergonomie devient tendue. 2 options :

### Option 1 : Pré-injection à l'arrivée du caret

Quand le caret atterrit sur le ¶ vide juste après le multi-ligne (post
cross-merge), on auto-INSÈRE le marker :

```
[OMath multi-ligne]
<=> | (caret ici, marker pré-injecté)
```

User tape `X = 4` → ligne devient `<=> X = 4`. Enter → cross-merge,
absorbe la ligne dans le bloc. Caret sur nouveau ¶ vide. Re-injecte
`<=>` automatiquement.

Pour SORTIR : user fait Enter sans rien taper → ligne est juste
`<=> ` → on détecte ça, on EFFACE le marker auto-inséré, on passe Enter.

**Inconvénient** : l'utilisateur qui voulait juste taper du texte normal
voit le `<=>` apparaître par surprise.

**Mitigation** : afficher le marker en GRISÉ (placeholder visuel) qui
disparaît dès que l'utilisateur tape autre chose qu'un caractère
compatible avec une équation. C'est ce que font certains éditeurs avec
les bullet lists (placeholder bullet visible).

Phase 1 simplifiée : pas de placeholder grisé, on injecte le marker en
texte normal. Si le user veut sortir : Backspace sur le marker, ou Enter
sur ligne marker-only.

### Option 2 : Détection à la frappe (post-Enter)

User tape `X = 4` directement (sans marker, ligne courante = `X = 4`).
Enter pressé. On détecte qu'on est en list-mode actif et que la ligne
n'a pas de marker. **Choix** :
- (a) Préfixer le source AVANT d'envoyer au lattice : transformer en
  `<=> X = 4` côté logique, puis cross-merge. Visuellement, l'utilisateur
  voit juste son texte se convertir directement en équation chaînée
  sans avoir tapé `<=>`.
- (b) Demander confirmation (popup "Continuer la chaîne avec `<=>` ?") —
  trop friction.

**(a) est la meilleure UX** : zéro friction, list-mode invisible mais
puissant. L'utilisateur ne voit jamais le marker comme texte, il voit
juste son équation s'ajouter à la chaîne.

### Choix Phase 1 : Option 2.(a)

Le list-mode active est INVISIBLE. La machine d'état mémorise le marker.
Quand l'utilisateur tape une ligne et fait Enter, on **préfixe**
silencieusement la source par le marker avant de la passer au pipeline
de cross-merge.

```
État active(marker = "<=>")
User tape "X = 4" puis Enter
→ source virtuelle = "<=> X = 4"
→ cascade Mode 1 → cross-merge → multi-ligne étendu
```

Sortie de mode :
- Ligne vide → Enter passe-through, mode désactivé
- Caret quitte la nouvelle ligne (clic ailleurs) → mode désactivé
- L'utilisateur tape un marker DIFFÉRENT (ex. tape `=>` alors que
  list-mode était `<=>`) → on respecte le marker tapé, le list-mode
  switch ou est désactivé selon design (= switch pour fluidité)

## Edge cases

1. **Marker `=` (chaîne d'égalités)** : list-mode active avec `=`.
   User tape `X+5=10` Enter → préfixé en `= X+5=10`. Lattice détecte
   marker `=` en début de ligne → cross-merge en chaîne `=`.

2. **User tape EXPLICITEMENT le marker** : ligne `<=> X = 4` Enter.
   Logique : la ligne a déjà le marker → ne pas re-préfixer (sinon
   `<=> <=> X = 4` doublé). Détection : si ligne TrimStart commence
   déjà par marker connu, ne pas re-préfixer.

3. **Marker mismatch** : list-mode `<=>`, user tape `=> X = 4`. La ligne
   commence par un autre marker. → ne pas préfixer, laisser le marker
   tapé. Le list-mode peut switcher vers `=>` (= update active marker)
   ou rester `<=>`. Choix Phase 1 : **switch** (suivre le user).

4. **Multi-OMath dans le doc** : list-mode est UNIQUEMENT actif si caret
   est dans le ¶ juste après le multi-ligne du dernier cross-merge.
   Si user clique sur un AUTRE OMath ailleurs, list-mode désactivé.

## Implémentation

### Helper pur (testable)

`MathCursor.Host.ListModeStateMachine` :

```csharp
internal sealed class ListModeStateMachine
{
    public string ActiveMarker { get; private set; } // null = inactive

    public void OnCrossMergeSucceeded(string markerUsed) { ActiveMarker = markerUsed; }
    public void OnSelectionMoved() { ActiveMarker = null; }  // ou plus nuancé

    public EnterAction OnEnterPressed(string currentLineText)
    {
        if (ActiveMarker == null) return EnterAction.Passthrough;
        string trimmed = (currentLineText ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed)) return EnterAction.ExitListMode;
        // Si user a déjà tapé le marker, ne pas re-préfixer
        if (StartsWithKnownMarker(trimmed, out _)) return EnterAction.ValidateAsIs;
        return EnterAction.PrefixWithActiveMarker;
    }
}

enum EnterAction { Passthrough, ExitListMode, PrefixWithActiveMarker, ValidateAsIs }
```

### Hook dans SuggestionService / KeyboardInterceptor

- À la fin de `CommitLatexAndOMath` (cascade Mode 1 / Mode 2 succès) :
  `_listMode.OnCrossMergeSucceeded(matchedMarker)` si c'était un
  cross-merge multi-ligne.
- Dans `KeyboardInterceptor.OnEnterPressed` :
  - Si `_listMode.ActiveMarker != null` :
    - Lire la ligne courante
    - `EnterAction action = _listMode.OnEnterPressed(currentLineText)`
    - Si `PrefixWithActiveMarker` : préfixer le source de la zone
      courante avant le commit (= modifier `_lastZoneSource` ou
      équivalent), puis laisser le commit normal s'occuper du reste
    - Si `ExitListMode` ou `Passthrough` : reset state, laisser Enter
      passer
- Dans `OnSelectionChange` : si caret quitte le ¶ post-multi-ligne →
  `_listMode.OnSelectionMoved()`.

## Tests-first (avant implémentation)

Tests purs sur `ListModeStateMachine` (pas de dépendance Word) :

1. État initial → `OnEnterPressed("anything")` retourne `Passthrough`
2. `OnCrossMergeSucceeded("<=>")` → `OnEnterPressed("X=1")` retourne `PrefixWithActiveMarker`
3. Active → `OnEnterPressed("")` retourne `ExitListMode`
4. Active → `OnEnterPressed("   ")` (whitespace only) retourne `ExitListMode`
5. Active(`<=>`) → `OnEnterPressed("<=> X=1")` retourne `ValidateAsIs`
6. Active(`<=>`) → `OnEnterPressed("=> X=1")` retourne `ValidateAsIs` (marker différent, on n'oblige pas)
7. Active → `OnSelectionMoved()` → état Inactive
8. Active(`=`) → `OnEnterPressed("4")` retourne `PrefixWithActiveMarker`
9. Active(`<=>`) → `OnEnterPressed("⇔ X=1")` retourne `ValidateAsIs` (marker Unicode)
10. Active → `OnEnterPressed(null)` retourne `ExitListMode` (null safety)

## Validation utilisateur attendue

Au commit + test manuel : taper une chaîne `X+1=2 / <=> 2x=4 / <=> x=2`
en mode list-mode invisible. Chaque ligne doit être absorbée dans le
bloc multi-ligne sans avoir tapé `<=>` pour les lignes 2 et 3 (juste
le contenu). Enter sur ligne vide doit sortir proprement.

## Liens

- ADR refactor cross-merge : [`2026-05-04-Meta-cross-merge-pipeline-refactor.md`](../decisions/2026-05-04-Meta-cross-merge-pipeline-refactor.md)
- ADR édition multi-ligne cascade : [`2026-05-04-Feat-multiline-edit-cascade-merge.md`](../decisions/2026-05-04-Feat-multiline-edit-cascade-merge.md)
- Brief multi-ligne Phase 1 : [`2026-04-30-multiline-systems-equivalences.md`](2026-04-30-multiline-systems-equivalences.md)
