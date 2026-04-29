# Brief — Mode édition : revenir à la saisie initiale depuis un OMath

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-27
**Branche :** `lattice-engine`
**Public cible :** agent C#/VSTO autonome qui ne connaît pas le projet.

---

## 1. Le besoin

Aujourd'hui, quand un élève tape `f(x) = 2x + 1` puis convertit (Ctrl+Espace),
MathCursor remplace le texte par un OMath natif Word. Si l'élève veut
*modifier* sa formule, il n'a aucun moyen propre de revenir au texte source —
il doit retaper depuis zéro ou éditer l'OMath caractère par caractère via les
contrôles natifs Word, ce qui casse la fluidité au clavier.

**Solution voulue :** quand le curseur entre dans un OMath qui a été produit
par MathCursor, ouvrir un popup proposant de revenir à la saisie initiale.
Si l'utilisateur accepte, l'OMath entier est remplacé par le texte source
d'origine (sans accent, sans formatage, exactement ce qu'il avait tapé).
L'élève peut alors corriger / ajouter et reconvertir.

## 2. UX

### Trigger
Le popup s'ouvre quand **toutes** ces conditions sont vraies :
- Le curseur (`Selection.Range`) est dans un `OMath` Word
- Cet OMath est associé à une `EquationHandle` connue de
  `IEquationStore` (i.e. il a été produit par MathCursor, pas une formule
  écrite à la main par un autre outil)
- Le popup n'est pas déjà affiché pour ce même OMath dans la session courante
  (éviter le clignotement à chaque mouvement de curseur dans la formule)

### Wording (FR)
- Titre / corps unique : `Modifier cette formule ?`
- Bouton primaire : **Revenir à la saisie initiale**
- Bouton secondaire : **Annuler** (ou ✕ / Esc)

Pas de "Êtes-vous sûr ?" supplémentaire — un seul niveau de confirmation.

### Action OUI
1. Récupérer le `source` depuis `IEquationStore.RetrieveAsync(handle)`
2. Sélectionner le `Range` complet de l'OMath
3. Remplacer ce range par le texte source brut (`Range.Text = source`)
4. Supprimer la `EquationHandle` de l'`IEquationStore`
   (`RemoveAsync(handle)`) — la formule n'existe plus comme OMath, donc plus
   besoin de stocker le source
5. Positionner le caret en fin du texte inséré (l'élève reprend à taper)
6. Fermer le popup

### Action NON / Esc / clic ailleurs
Fermer le popup. Aucune mutation du document. L'OMath reste intact.

### Conformité avec règle popup silencieux
La memory `project_ergo_brief.md` et l'ADR
[`2026-04-24-UX-popup-silent-until-interaction.md`](../decisions/2026-04-24-UX-popup-silent-until-interaction.md)
imposent : popup silencieux jusqu'à interaction utilisateur.

**Arbitrage à valider avec l'utilisateur** : faut-il faire ce popup-revert
silencieux aussi (= n'apparaît qu'après une touche raccourci type `Ctrl+E`) ?
Le brief actuel suit la demande littérale (popup auto à l'entrée OMath). Si
trop agressif à l'usage, fallback : ne pas afficher au mouvement, attendre
qu'un raccourci dédié soit pressé.

## 3. Architecture

### Détection cursor-in-OMath
Event Word à hooker : `Application.WindowSelectionChange` (déjà utilisé
ailleurs dans le projet, voir `SuggestionService.cs`). Dans le handler :
```csharp
var sel = app.Selection;
if (sel.OMaths.Count > 0)
{
    var omath = sel.OMaths[1]; // 1-based
    var handle = TryGetHandleFromOMath(omath); // voir §4
    if (handle != null && IsKnownEquation(handle))
    {
        ShowRevertPopup(omath, handle);
    }
}
```

### Mapping OMath ↔ EquationHandle
**Hypothèse à valider en lecture du code existant** : la conversion
Ctrl+Espace écrit-elle déjà l'`EquationHandle` quelque part dans le document
(content control wrapper, propriété sur l'OMath, ou autre) ?

- Si **oui** : utiliser l'existant (probablement un `ContentControl.Tag` qui
  porte l'ID).
- Si **non** : ajouter un `ContentControl` (type=Rich Text) qui enveloppe
  chaque OMath créé par MathCursor, avec `Tag = handle.Id`. C'est la même
  technique que `ContentControlOnEnter` utilisé ailleurs dans le projet.
  Dans ce cas, le brief doit aussi mettre à jour le code de conversion
  (probablement dans `SuggestionService.ApplyConversion()` ou équivalent)
  pour wrapper le nouvel OMath.

À regarder en premier dans le code :
- `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` — méthode qui
  insère l'OMath dans Word.
- `adapter-vsto/src/MathCursor/Host/VstoEquationStore.cs` — déjà existant,
  utilise `Document.CustomXMLParts` namespace
  `http://mathcursor.app/equations/v1`. Stocke par `EquationHandle.Id`.

### UI
Réutiliser le pattern de
`adapter-vsto/src/MathCursor/UI/SuggestionPopupWindow.cs` (WPF popup
positionné au caret) — soit en étendant la fenêtre existante avec un nouveau
mode "revert", soit en créant `EditModePopupWindow.cs` à côté. À l'agent de
juger ce qui est plus propre selon l'état du code.

## 4. Livrables attendus

1. **Détection cursor-in-OMath**
   - Hook `WindowSelectionChange` (ou étendre le hook existant si déjà là).
   - Fonction `TryGetHandleFromOMath(OMath) → EquationHandle?` qui résout via
     content control wrapper ou autre marqueur.

2. **Popup d'édition**
   - Nouvelle fenêtre WPF (ou nouveau mode dans la popup existante) avec le
     wording §2.
   - Position : au caret (réutiliser la logique de `SuggestionPopupWindow`,
     déjà ajustée pixels physiques → DIPs WPF, voir commit `65595bb`).

3. **Action revert**
   - Méthode `RevertOMathToSource(OMath, EquationHandle)` :
     1. Lit le source via `IEquationStore.RetrieveAsync`
     2. Remplace le range OMath (incluant son content control wrapper si
        présent) par le texte source
     3. Supprime du store via `RemoveAsync`
     4. Positionne le caret en fin de texte inséré

4. **Tests d'intégration**
   - Test manuel : taper `f(x) = 2x + 1`, Ctrl+Espace → OMath créé. Cliquer
     dans l'OMath → popup s'ouvre. Cliquer "Revenir à la saisie initiale" →
     l'OMath redevient `f(x) = 2x + 1` en texte brut, le store ne contient
     plus la formule.
   - Test : un OMath créé hors MathCursor (ex : équation Word native existante)
     → popup ne s'ouvre PAS (pas de handle dans le store).
   - Test : popup ouvert, l'utilisateur tape Esc → popup se ferme, document
     non modifié.
   - Test : Ctrl+Z après revert → l'OMath revient (Word natif gère l'undo de
     `Range.Text` change).

5. **ADR** dans
   `docs/dev/decisions/2026-04-XX-Feat-edit-mode-revert-source.md`,
   Kind=Feat, Température=molle, citation utilisateur = ce brief.

## 5. Cas de test obligatoires

| Cas | Attendu |
|-----|---------|
| Curseur entre dans un OMath produit par MathCursor | Popup apparaît (ou rien si arbitrage = silent + raccourci) |
| Curseur entre dans un OMath natif Word non-MathCursor | Aucun popup |
| Click "Revenir à la saisie initiale" | OMath remplacé par texte source, caret en fin |
| Click "Annuler" / Esc | Popup se ferme, document inchangé |
| Ctrl+Z après revert | OMath rétabli (undo Word natif) |
| Curseur sort puis re-rentre dans le même OMath dans la même session | Popup ne réapparaît pas (anti-spam) |
| OMath wrappé dans content control sans handle correspondant en store | Aucun popup (state inconsistent → log warn dans `mathcursor.log`) |

## 6. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` | Insertion OMath, polling, hook events |
| `adapter-vsto/src/MathCursor/Host/VstoEquationStore.cs` | Persistence sources via CustomXMLParts |
| `adapter-vsto/src/MathCursor/UI/SuggestionPopupWindow.cs` | Popup WPF existant (référence pattern) |
| `host-contract-csharp/src/MathCursor.HostContract/IEquationStore.cs` | Interface store, méthodes Retrieve/Remove à utiliser |
| `host-contract-csharp/src/MathCursor.HostContract/IEditorSurface.cs` | Si trigger UI passe par cette interface (à vérifier) |
| `briefs/architecture-flow.md` | Source de vérité des règles de flow (memory `reference_architecture_flow.md`) |
| `docs/dev/decisions/2026-04-24-UX-popup-silent-until-interaction.md` | ADR à respecter pour l'arbitrage popup auto vs raccourci |

## 7. Ce qu'il NE faut PAS faire

- ❌ Implémenter une "édition inline" de l'OMath (parser l'OMath et le
  modifier sur place). C'est un sac de nœuds, et le brief ne demande qu'un
  revert simple → re-conversion via le pipeline existant.
- ❌ Toucher à `core-csharp/` — la couche métier ne connaît ni Word ni les
  OMath. Tout reste dans `adapter-vsto/`.
- ❌ Stocker l'`EquationHandle` ailleurs que dans le content control / la
  technique déjà choisie — éviter de dupliquer l'état (sinon désync garantie).
- ❌ Effacer manuellement le content control sans aussi `RemoveAsync` du
  store. Sinon CustomXMLParts grossit avec des entries orphelines.
- ❌ Faire surgir le popup à chaque `WindowSelectionChange` même quand le
  curseur reste dans le même OMath (anti-spam = mémoriser le dernier OMath
  pour lequel on a affiché).
- ❌ Bloquer l'édition Word native dans l'OMath. L'utilisateur doit garder
  la possibilité de modifier l'OMath caractère par caractère via Word s'il
  le souhaite — le popup est une *option*, pas une obligation.

## 8. Validation finale

1. `dotnet build MathCursor.sln` → 0 erreur, 0 warning.
2. `dotnet test adapter-vsto/tests/` → tous les tests passent.
3. Test manuel dans Word :
   - Taper 3 formules, les convertir, en éditer une via revert, retaper,
     re-convertir.
   - Vérifier dans `Document.CustomXMLParts` (via debug ou vidage XML) que
     l'entry de la formule revertée a bien disparu.
   - Vérifier que les autres formules sont intactes.
4. ADR créé.
5. Commit séparé pour : (a) wrapping content control si besoin,
   (b) détection + popup, (c) action revert, (d) ADR.

## 9. Estimations

- Lecture du code existant (mapping OMath↔handle) : 1 h
- Wrapping content control si pas en place : 2-3 h
- Détection cursor-in-OMath + popup : 3-4 h
- Action revert + tests : 2 h
- ADR + validation Word : 1 h
- **Total estimé** : ~1 jour
