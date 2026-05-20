# Fix — Ghost doc invisible dès création + pre-warming au boot

**Date :** 2026-05-13
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-12-Refactor-pure-merger-atomic-insert.md](2026-05-12-Refactor-pure-merger-atomic-insert.md)
(ADR qui a introduit le ghost doc pour isoler `BuildOMathXml` du doc user)

## Citation acté

> « il faudra qu'on rechallenge l'insertion dans le WORD la j'ai dit de
> passer par un ghost doc, mais c'est vraiment foireux.. on voit la
> ghost doc se creer, ca fait crara » — utilisateur, 2026-05-13
>
> « on tente C en effet » (validation du plan A+B combinés)

## Contexte

L'ADR du 12-05 a introduit un `OMathStagingService` qui crée un Word
`Document` fantôme pour héberger le pipeline `Insert unicodeMath →
BuildUp → capture WordOpenXML` sans muter le doc user. Architecturalement
propre (zéro perte de donnée sur abort, undo stack clean).

UX en pratique : flash visible côté user au 1ᵉʳ commit math. La fenêtre
du ghost doc apparaît brièvement puis disparaît. La cause :

```csharp
_stagingDoc = _app.Documents.Add();              // ← fenêtre créée VISIBLE
_stagingDoc.ActiveWindow.Visible = false;        // ← masquée APRÈS (trop tard)
```

`ScreenUpdating=false` posté juste avant ne suffit pas — il limite les
modifs intra-window, pas la création de nouvelle window. Et même
`Visible=false` post-création peut laisser un flash de focus / taskbar
entry selon la version Word.

## Décision

Deux mécanismes combinés (A+B du plan proposé) :

### A — `Documents.Add(Visible: false)` dès l'appel

Word COM API : `Documents.Add(Template, NewTemplate, DocumentType, Visible)`.
Le 4ᵉ param est un `ref object Visible` bool. Passer `false` dès l'Add
crée la window invisible **immédiatement**, sans fenêtre intermédiaire
visible.

```csharp
object missing = Type.Missing;
object visibleArg = false;
_stagingDoc = _app.Documents.Add(
    Template: ref missing,
    NewTemplate: ref missing,
    DocumentType: ref missing,
    Visible: ref visibleArg);
```

Bretelle conservée : si une vieille version Word ignore le param,
`Visible=false` est forcé après création (no-op si déjà invisible).

### B — Pre-warming au boot via `OMathStagingService.WarmUp()`

Nouvelle méthode publique idempotente sur le service. Appelée depuis
`SuggestionService.Install()` (boot addin, après les hooks events) :

```csharp
public void WarmUp()
{
    if (_disposed) return;
    EnsureStagingDoc();
}
```

Le ghost doc se crée donc **au boot** de l'addin, pas au 1ᵉʳ commit
user. Word est déjà en train de tout charger à ce moment-là — un
`Documents.Add` invisible se noie dans le bruit visuel du démarrage.

Coût : ~10-20 MB de RAM constants pour héberger un doc Word vide.
Acceptable vu la machine cible (lycée, PC desktop standard).

## Tradeoff & alternatives écartées

- **Position de la window offscreen** (`Left/Top = -10000`) + minimized.
  Reporté en fallback si A+B ne suffisent pas en pratique. Plus fragile
  (Word peut "ramener" la window au focus dans certains scénarios).
- **Retour à insertion+delete dans le user doc** wrappée dans
  `Application.UndoRecord`. Rejeté — l'ADR du 12-05 a éliminé cette
  voie pour cause de perte de données potentielle. Réintroduire = dette
  qu'on avait actée de virer.
- **Process Word séparé** (lance un autre `WINWORD.EXE` headless).
  Rejeté — latence boot ~2s, hors budget.
- **OpenXML SDK hors Interop** pour construire l'OMath sans Word.
  Rejeté — pas de BuildUp natif dans OpenXML SDK, on perd la conversion
  unicodeMath → OMath qui dépend du moteur Word.

## Conséquences

- `OMathStagingService.EnsureStagingDoc` : utilise `Documents.Add` avec
  `ref` params COM pour passer `Visible:false` directement. La bretelle
  `Visible=false` post-création reste pour les vieux Word.
- `OMathStagingService.WarmUp()` : nouvelle méthode publique. Appelée
  depuis `SuggestionService.Install()` à la fin (après hooks + timer).
  Erreur silencieuse — le lazy-init prend le relais si le warm-up rate.
- **Aucune modification d'API publique** des call-sites existants.
  `BuildOMathXml` continue de fonctionner exactement comme avant — au
  1ᵉʳ appel post-WarmUp, le ghost doc est déjà créé donc l'appel est
  ~50ms plus rapide aussi (bonus).
- **0 régression test** : 419/419 Adapter, 935/944 Core.

## Validation post-fix

Le user observera côté UX au prochain lancement de Word + commit math :
- **Plus de flash** de la fenêtre ghost à la 1ʳᵉ insertion math
  (objectif principal).
- Word boot ~100ms plus long (création du ghost doc + masquage). Acceptable.
- 1ʳᵉ insertion math ~50ms plus rapide (pas de création de ghost doc
  à ce moment).

Si A+B ne suffisent pas (flash quand même visible), on bascule sur
offscreen + minimized comme fallback.

## Plan harnais — observation in vivo

Aucun test automatique ne peut vérifier l'absence de flash visuel.
Validation = observation user au prochain build/run VS.
