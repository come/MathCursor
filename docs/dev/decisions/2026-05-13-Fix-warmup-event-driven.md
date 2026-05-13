# Fix — WarmUp ghost doc déclenché par le 1ᵉʳ `WindowActivate`, plus inline dans `Install()`

**Date :** 2026-05-13
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [`2026-05-13-Fix-ghost-doc-invisible.md`](2026-05-13-Fix-ghost-doc-invisible.md) (qui a introduit le WarmUp inline et `Visible:false` lors du Documents.Add)

## Citation acté

> « et attention SetTimer ou truc qui se fait au temps ca pue ! » — utilisateur, 2026-05-13
> « yes good » — utilisateur, 2026-05-13
> « yes ok » — utilisateur, 2026-05-13 (validation du plan)

## Contexte

L'ADR précédent (`Fix-ghost-doc-invisible`) faisait `_omathStaging?.WarmUp()`
**inline dans `SuggestionService.Install()`**, juste après le démarrage du
`_pollTimer`. Objectif : pré-créer le ghost doc pendant que Word boot pour
éviter le flash visible au 1ᵉʳ commit user.

Symptôme observé au démarrage Word (output debug VS) :

```
Exception levée : 'System.Runtime.InteropServices.COMException' dans MathCursor.dll
Microsoft.Office.Interop.Word._Application.Selection.get retournée null.
```

Mécanisme probable :
1. `Install()` démarre `_pollTimer` (tick toutes les `PollIntervalMs`).
2. `Install()` enchaîne sur `WarmUp()` qui fait `Documents.Add(Visible:false)` →
   `EnsureStagingDoc` active brièvement le ghost doc puis réactive le user doc.
3. Pendant cette danse d'activation, `_app.Selection` peut être transitoirement
   `null` côté Word interop.
4. Le `_pollTimer` tire `CheckAndUpdate()` qui tape sur `_app.Selection` → COMException.

L'exception est probablement catchée par les `try { _app.Selection… } catch { }`
défensifs, mais pollue le log et signale un timing fragile. Surtout : la
solution « différer le WarmUp d'un tick de timer » serait une heuristique
temporelle — contraire à la doctrine projet (CLAUDE.md §Règles de dev :
« Events natifs VSTO → pas d'heuristiques fragiles »).

## Décision

Le WarmUp est déclenché par le **1ᵉʳ event natif `Application.WindowActivate`** :
ce signal garantit que Word a fini son boot ET qu'un user doc est rendu —
condition suffisante pour qu'un `Documents.Add(Visible:false)` ne provoque
plus de race sur `Selection`.

Pattern : **handler one-shot auto-désabonnant**.

```csharp
public void Install()
{
    …
    _app.WindowActivate += OnFirstWindowActivateForWarmUp;
    _installed = true;
}

private void OnFirstWindowActivateForWarmUp(Word.Document doc, Word.Window wnd)
{
    try { _app.WindowActivate -= OnFirstWindowActivateForWarmUp; } catch { }
    try { _omathStaging?.WarmUp(); } catch { }
}

public void Dispose()
{
    …
    // Défensif : si Dispose() avant 1ᵉʳ fire, le handler n'a pas pu se désabonner.
    try { if (_installed) _app.WindowActivate -= OnFirstWindowActivateForWarmUp; } catch { }
    …
}
```

Le détachement est la seule source de vérité d'état (« one-shot » sans field
flag). `_omathStaging.WarmUp()` reste idempotent : si le mécanisme se fait
contourner (cas extrême), l'`EnsureStagingDoc` lazy du 1ᵉʳ `BuildOMathXml`
prend le relais.

## Tradeoff & alternatives écartées

- **Alt A — Flag once dans `OnSelectionChange`** : WarmUp au 1ᵉʳ
  `WindowSelectionChange`. Rejetée : mélange une responsabilité d'init dans un
  handler déjà très chargé (100+ lignes, NER, popup, mode édition, etc.).
  Separation of concerns dégradée.
- **Alt C — Greffe sur `OnWindowActivate` existant** : ajout d'un check
  `if (!_warmedUp)` dans le handler existant. Rejetée pour la même raison
  (`OnWindowActivate` a déjà une responsabilité claire = relancer `_pollTimer`).
  Aussi : nécessite un field flag explicite alors que l'auto-désabonnement
  encode l'état dans le mécanisme.
- **Alt D — Différer WarmUp via `DispatcherTimer` à délai T** : rejetée
  explicitement par l'utilisateur, contraire à la doctrine projet. Une
  heuristique temporelle ne résout pas le problème structurel et masque un
  symptôme au lieu d'attendre la condition réelle (Word prêt).

## Conséquences

- **Code touché** :
  - `adapter-vsto/src/MathCursor/Host/SuggestionService.cs`
    - `Install()` : abonnement à `OnFirstWindowActivateForWarmUp` au lieu de
      l'appel direct à `_omathStaging?.WarmUp()`
    - Nouveau handler privé `OnFirstWindowActivateForWarmUp(Word.Document, Word.Window)`
      après `OnWindowActivate`
    - `Dispose()` : désabonnement défensif
- **Tests** : aucun test unitaire ajouté (handler interop Word, déjà couvert
  par la validation manuelle en Word). Le build VSTO reste vert.
- **API publique** : aucun changement.
- **Règles MC impactées** : aucune (pas de regex/XML, pas de splice, pas de
  SuppressMessage).

## Validation post-fix

Lancer Word en debug VS et vérifier que **plus aucune** ligne
`Microsoft.Office.Interop.Word._Application.Selection.get retournée null`
n'apparait dans la fenêtre Output pendant le démarrage (avant le 1ᵉʳ click
user dans le doc).

Vérifier ensuite que le 1ᵉʳ commit user (Ctrl+Espace après le premier
`WindowActivate`) ne fait pas apparaître de flash de fenêtre invisible —
le ghost doc doit déjà être pré-créé.

## Plan en cours — état d'avancement

Cette fix fait partie du chantier d'épuration boot/startup post-refacto
ghost doc. Pas de séquence multi-étapes : ADR atomique.

Mise à jour ROADMAP.md : pas de case dédiée (fix interstitiel hors des
4 chantiers principaux).
